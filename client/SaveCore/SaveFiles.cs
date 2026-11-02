using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Enshrouded.SaveCore;

public interface IGameGuard { bool IsRunning(); void EnsureStopped(); }
public sealed class GameGuard : IGameGuard
{
    public bool IsRunning()
    {
        foreach (var name in new[] { "enshrouded", "enshrouded_server" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (var p in processes) p.Dispose(); }
        }
        return false;
    }
    public void EnsureStopped() { if (IsRunning()) throw new IOException("游戏仍在运行，请完全退出游戏后再检查、上传或恢复。"); }
}

public static class SaveRules
{
    public const int MaxFiles = 128;
    public const long MaxExpanded = 2L << 30;
    public const long MaxArchive = 512L << 20;
    public static bool ValidKey(string key) => Regex.IsMatch(key, "^[0-9a-f]{8}$");
    public static bool ValidName(string name, string key)
    {
        if (!ValidKey(key) || name.Length > 128 || name.Trim() != name || name.EndsWith('.')) return false;
        if (name == key) return true;
        if (!name.StartsWith(key, StringComparison.Ordinal) || name.Length <= key.Length + 1) return false;
        var suffix = name[key.Length..];
        return "._-".Contains(suffix[0]) && Regex.IsMatch(suffix, "^[a-zA-Z0-9_.-]+$") && !suffix.Contains("..");
    }
    public static bool Belongs(string name, string key) => name == key || name.StartsWith(key + "_", StringComparison.Ordinal) || name.StartsWith(key + "-", StringComparison.Ordinal) || name.StartsWith(key + ".", StringComparison.Ordinal);
    public static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if (Path.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("存档路径不能包含符号链接或目录联接：" + p);
    }
    public static void ValidateManifest(IEnumerable<SaveFile> files, string key, bool allowEmpty = false)
    {
        var list = files.ToList();
        if ((!allowEmpty && list.Count == 0) || list.Count > MaxFiles) throw new IOException("世界文件数量不正确。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long sum = 0;
        foreach (var f in list)
        {
            if (!ValidName(f.Path, key) || !seen.Add(f.Path) || f.Size < 0 || f.Size > MaxExpanded || !Regex.IsMatch(f.Sha256, "^[0-9a-f]{64}$")) throw new IOException("不安全或重复的世界文件：" + f.Path);
            sum = checked(sum + f.Size); if (sum > MaxExpanded) throw new IOException("世界存档超过大小限制。");
        }
    }
    public static string Fingerprint(IEnumerable<SaveFile> files)
    {
        var text = string.Join("\n", files.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => $"{f.Path}\0{f.Size}\0{f.Sha256}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    public static string HashFile(string path)
    { NoLinks(path); using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexStringLower(SHA256.HashData(f)); }
    public static List<string> Candidates()
    {
        var candidates = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "enshrouded") };
        var steamRoots = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"), @"C:\Program Files (x86)\Steam", @"D:\Steam" };
        foreach (var steam in steamRoots.Distinct())
        { var users = Path.Combine(steam, "userdata"); if (!Directory.Exists(users)) continue; try { foreach (var u in Directory.EnumerateDirectories(users)) candidates.Add(Path.Combine(u, "1203620", "remote")); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        return candidates.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class SaveScanner(IGameGuard guard, TimeSpan? stabilityDelay = null)
{
    private readonly TimeSpan delay = stabilityDelay ?? TimeSpan.FromMilliseconds(750);
    public LocalScan Scan(string directory, string key, bool allowEmpty = false)
    {
        guard.EnsureStopped(); SaveRules.NoLinks(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("找不到存档目录，请检查设置。");
        if (!SaveRules.ValidKey(key)) throw new IOException("世界文件标识必须是 8 位小写十六进制字符。");
        var files = new List<SaveFile>(); long total = 0; var modified = DateTimeOffset.MinValue;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path); if (!SaveRules.Belongs(name, key)) continue;
            SaveRules.NoLinks(path); if (!SaveRules.ValidName(name, key) || Directory.Exists(path)) throw new IOException("世界文件组中存在不支持的项目：" + name);
            var fi = new FileInfo(path); if (fi.Length > SaveRules.MaxExpanded || files.Count >= SaveRules.MaxFiles) throw new IOException("世界文件数量或大小超过限制。");
            var hash = SaveRules.HashFile(path); fi.Refresh(); total = checked(total + fi.Length); if (total > SaveRules.MaxExpanded) throw new IOException("存档过大。");
            files.Add(new(name, fi.Length, hash)); if (fi.LastWriteTimeUtc > modified) modified = fi.LastWriteTimeUtc;
        }
        if (files.Count == 0 && !allowEmpty) throw new IOException("没有找到这个世界的文件。请核对目录和世界标识；新房主可先从云端恢复。");
        SaveRules.ValidateManifest(files, key, allowEmpty);
        return new(files, SaveRules.Fingerprint(files), total, modified);
    }
    public async Task<LocalScan> StableScanAsync(string directory, string key, bool allowEmpty = false, CancellationToken ct = default)
    {
        var first = await Task.Run(() => Scan(directory, key, allowEmpty), ct); await Task.Delay(delay, ct);
        var second = await Task.Run(() => Scan(directory, key, allowEmpty), ct);
        if (first.Fingerprint != second.Fingerprint) throw new IOException("存档仍在保存，请稍后重新检查。");
        return second;
    }
    public async Task<LocalCheck> CheckAsync(ClientSettings c, ClientState state, CancellationToken ct = default)
    {
        if (File.Exists(state.JournalPath)) throw new IOException("有未完成的恢复，请先恢复本地旧档。");
        var scan = await StableScanAsync(c.SaveDirectory, c.World!.SaveKey, false, ct); var baseline = state.GetBaseline(c);
        var before = baseline?.Files.ToDictionary(f => f.Path, StringComparer.Ordinal) ?? [];
        int changes = scan.Files.Count(f => !before.TryGetValue(f.Path, out var old) || old.Sha256 != f.Sha256 || old.Size != f.Size) + before.Keys.Count(n => !scan.Files.Any(f => f.Path == n));
        return new(scan, changes, baseline?.Fingerprint != scan.Fingerprint, baseline == null);
    }
    public async Task<LocalSnapshot> SnapshotAsync(string directory, string key, LocalScan expected, string destination, CancellationToken ct = default)
    {
        guard.EnsureStopped(); Directory.CreateDirectory(destination); var archive = Path.Combine(destination, "world.zip");
        try
        {
            await Task.Run(() => {
                using var file = new FileStream(archive, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true))
                foreach (var f in expected.Files)
                {
                    ct.ThrowIfCancellationRequested(); guard.EnsureStopped(); var source = Path.Combine(directory, f.Path); SaveRules.NoLinks(source);
                    using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length != f.Size) throw new IOException("存档已变化，请重新检查。");
                    var entry = zip.CreateEntry(f.Path, CompressionLevel.Optimal); using var output = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; int count; long copied = 0;
                    while ((count = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); hash.AppendData(buffer, 0, count); copied += count; if (copied > f.Size) throw new IOException("存档已变化。"); }
                    if (copied != f.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != f.Sha256) throw new IOException("存档已变化，请重新检查后确认。");
                }
                file.Flush(true);
            }, ct);
            if (new FileInfo(archive).Length > SaveRules.MaxArchive) throw new IOException("压缩后存档超过 512 MB 限制。");
            var after = await StableScanAsync(directory, key, expected.Files.Count == 0, ct);
            if (after.Fingerprint != expected.Fingerprint) throw new IOException("打包期间存档发生变化，请重新检查。");
            return new(archive, SaveRules.HashFile(archive), expected);
        }
        catch { if (File.Exists(archive)) File.Delete(archive); throw; }
    }
}

public static class ArchiveCodec
{
    public static void ExtractVerified(string archive, string destination, List<SaveFile> files, string key, bool allowEmpty = false)
    {
        SaveRules.ValidateManifest(files, key, allowEmpty); SaveRules.NoLinks(destination); Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archive); if (zip.Entries.Count != files.Count) throw new IOException("归档文件数量与清单不一致。");
        var expected = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            if (!expected.Remove(e.FullName, out var f)) throw new IOException("归档包含未授权或重复的文件。");
            var type = (e.ExternalAttributes >> 16) & 0xF000;
            if ((type != 0 && type != 0x8000) || (e.ExternalAttributes & 0x400) != 0 || e.Length != f.Size) throw new IOException("归档包含链接或非法文件。");
            if (e.Length > 1048576 && (e.CompressedLength == 0 || e.Length / e.CompressedLength > 1000)) throw new IOException("归档压缩比异常。");
            var path = Path.Combine(destination, f.Path); SaveRules.NoLinks(path);
            using var input = e.Open(); using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; int n; long copied = 0;
            while ((n = input.Read(buffer)) > 0) { copied += n; if (copied > f.Size) throw new IOException("解压文件过大。"); output.Write(buffer, 0, n); hash.AppendData(buffer, 0, n); }
            output.Flush(true); if (copied != f.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != f.Sha256) throw new IOException("文件校验失败：" + f.Path);
        }
    }
}
