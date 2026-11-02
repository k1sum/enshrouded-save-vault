using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enshrouded.SaveCore;

public static class JsonData
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new IOException("记录为空，无法继续。");
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(f, value, Options); f.Flush(true); } File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}

public sealed record WorldInfo(string Id, string LibraryId, string Name, string SaveKey, bool CanUpload);
public sealed record UserInfo(string Id, string Name, bool Disabled);
public sealed record IdentityInfo(UserInfo User, string CredentialId, string Device, DateTimeOffset ExpiresAt);
public sealed record MeResponse(IdentityInfo Identity, List<WorldInfo> Worlds);
public sealed record SaveFile(string Path, long Size, string Sha256);
public sealed record UploadMetadata(string WorldId, string Name, string Note, string? ParentId, string ArchiveSha256, List<SaveFile> Files);
public sealed record BackupInfo
{
    public string Id { get; init; } = "";
    public long Sequence { get; init; }
    public string LibraryId { get; init; } = "";
    public string WorldId { get; init; } = "";
    public string WorldName { get; init; } = "";
    public string SaveKey { get; init; } = "";
    public string Name { get; init; } = "";
    public string Note { get; init; } = "";
    public string? ParentId { get; init; }
    public string UserId { get; init; } = "";
    public string Uploader { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public string ArchiveSha256 { get; init; } = "";
    public long ArchiveSize { get; init; }
    public long TotalSize { get; init; }
    public List<SaveFile> Files { get; init; } = [];
}
public sealed record BackupPage(List<BackupInfo> Items, int Total, int Page, int PageSize, long Snapshot);
public sealed record ClientSettings
{
    public string Server { get; init; } = "";
    public WorldInfo? World { get; init; }
    public string SaveDirectory { get; init; } = "";
    public bool SteamCloudAcknowledged { get; init; }
    public bool FilesConfirmed { get; init; }
    [JsonIgnore] public bool Configured => Uri.TryCreate(Server, UriKind.Absolute, out _) && World != null && Directory.Exists(SaveDirectory) && SteamCloudAcknowledged && FilesConfirmed;
    [JsonIgnore] public string ProfileKey => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Server.TrimEnd('/') + "|" + World?.Id + "|" + Path.GetFullPath(SaveDirectory) + "|" + World?.SaveKey)));
}
public sealed record LocalScan(List<SaveFile> Files, string Fingerprint, long TotalSize, DateTimeOffset LastModified);
public sealed record LocalCheck(LocalScan Scan, int ChangedCount, bool HasChanges, bool IsFirstBackup);
public sealed record LocalSnapshot(string ArchivePath, string ArchiveSha256, LocalScan Scan);
public sealed record Baseline(string? BackupId, string Fingerprint, List<SaveFile> Files);
public sealed record PendingUpload(string Server, string ProfileKey, string OperationId, UploadMetadata Metadata, string ArchivePath, string Fingerprint);
public sealed class ApiException(int status, string message) : Exception(message) { public int StatusCode { get; } = status; }

public sealed class ClientState(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string BaselinePath(ClientSettings c) => Path.Combine(Root, "profiles", c.ProfileKey, "baseline.json");
    public string PendingPath(ClientSettings c) => Path.Combine(Root, "profiles", c.ProfileKey, "pending.json");
    public string JournalPath => Path.Combine(Root, "restore-journal.json");
    public Baseline? GetBaseline(ClientSettings c) => File.Exists(BaselinePath(c)) ? JsonData.Read<Baseline>(BaselinePath(c)) : null;
    public PendingUpload? GetPending(ClientSettings c) => File.Exists(PendingPath(c)) ? JsonData.Read<PendingUpload>(PendingPath(c)) : null;
    public void SetBaseline(ClientSettings c, Baseline baseline) => JsonData.Write(BaselinePath(c), baseline);
    public void SavePending(ClientSettings c, PendingUpload pending) => JsonData.Write(PendingPath(c), pending);
    public void ClearPending(ClientSettings c) { if (File.Exists(PendingPath(c))) File.Delete(PendingPath(c)); }
    public string NewFolder(string category) { var p = Path.Combine(Root, category, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
}
