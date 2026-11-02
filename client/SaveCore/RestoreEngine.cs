namespace Enshrouded.SaveCore;

public sealed record RestoreJournal(string TargetDirectory, string SaveKey, string OldArchive, List<SaveFile> OldFiles, List<SaveFile> NewFiles, string BaselinePath, Baseline? OldBaseline, string StageDirectory, Dictionary<string, string> StagedFiles, string Phase);

public sealed class RestoreEngine(ClientState state, SaveScanner scanner, IGameGuard guard)
{
    // Test hook exercises failure and rollback after a partial write, not used by the GUI.
    public Action<int>? AfterWrite { get; set; }
    public async Task RestoreAsync(ClientSettings settings, BackupInfo backup, string archive, CancellationToken ct = default)
    {
        guard.EnsureStopped(); if (File.Exists(state.JournalPath)) throw new IOException("存在未完成的恢复，请先恢复旧档。");
        if (settings.World == null || backup.WorldId != settings.World.Id || backup.SaveKey != settings.World.SaveKey) throw new IOException("所选备份不属于当前配置的世界。");
        SaveRules.NoLinks(settings.SaveDirectory);
        if (SaveRules.HashFile(archive) != backup.ArchiveSha256) throw new IOException("下载的归档校验失败。");
        var work = state.NewFolder("restore"); var fresh = Path.Combine(work, "new");
        ArchiveCodec.ExtractVerified(archive, fresh, backup.Files, backup.SaveKey);
        var before = await scanner.StableScanAsync(settings.SaveDirectory, backup.SaveKey, true, ct);
        var previous = await scanner.SnapshotAsync(settings.SaveDirectory, backup.SaveKey, before, Path.Combine(work, "old"), ct);
        JsonData.Write(Path.Combine(work, "old-manifest.json"), before);
        var staged = new Dictionary<string, string>(StringComparer.Ordinal);
        RestoreJournal? journal = null;
        try
        {
            foreach (var file in backup.Files)
            {
                ct.ThrowIfCancellationRequested(); guard.EnsureStopped();
                var path = Path.Combine(settings.SaveDirectory, ".mist-" + Guid.NewGuid().ToString("N") + ".tmp");
                CopyDurable(Path.Combine(fresh, file.Path), path); staged[file.Path] = path;
            }
            // Recheck the preimage after staging. No files have been replaced yet.
            if ((await scanner.StableScanAsync(settings.SaveDirectory, backup.SaveKey, true, ct)).Fingerprint != before.Fingerprint) throw new IOException("恢复前本地存档发生变化，请重新检查。");
            journal = new(Path.GetFullPath(settings.SaveDirectory), backup.SaveKey, previous.ArchivePath, before.Files, backup.Files, state.BaselinePath(settings), state.GetBaseline(settings), work, staged, "writing");
            JsonData.Write(state.JournalPath, journal);
            var writes = 0;
            foreach (var file in backup.Files)
            {
                ct.ThrowIfCancellationRequested(); guard.EnsureStopped(); var target = Path.Combine(settings.SaveDirectory, file.Path); SaveRules.NoLinks(target);
                File.Move(staged[file.Path], target, true); AfterWrite?.Invoke(++writes);
            }
            foreach (var old in before.Files.Where(f => !backup.Files.Any(n => n.Path == f.Path)))
            { guard.EnsureStopped(); var target = Path.Combine(settings.SaveDirectory, old.Path); SaveRules.NoLinks(target); File.Delete(target); }
            var after = await scanner.StableScanAsync(settings.SaveDirectory, backup.SaveKey, false, ct);
            if (after.Fingerprint != SaveRules.Fingerprint(backup.Files)) throw new IOException("恢复后文件校验失败。");
            state.SetBaseline(settings, new(backup.Id, after.Fingerprint, backup.Files));
            JsonData.Write(state.JournalPath, journal with { Phase = "complete" }); File.Delete(state.JournalPath);
        }
        catch
        {
            if (journal != null && !guard.IsRunning())
            { try { await RecoverAsync(CancellationToken.None); } catch (Exception e) { throw new IOException("恢复中断，旧档仍已保留。请退出游戏并点击“恢复旧档”。原因：" + e.Message, e); } }
            throw;
        }
        finally { if (!File.Exists(state.JournalPath)) foreach (var path in staged.Values) if (File.Exists(path)) File.Delete(path); }
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        if (!File.Exists(state.JournalPath)) return; guard.EnsureStopped();
        var j = JsonData.Read<RestoreJournal>(state.JournalPath);
        if (j.Phase == "complete") { File.Delete(state.JournalPath); return; }
        if (j.Phase != "writing") throw new IOException("无法识别恢复日志，请保留本地备份并联系管理员。");
        SaveRules.ValidateManifest(j.OldFiles, j.SaveKey, true); SaveRules.ValidateManifest(j.NewFiles, j.SaveKey);
        SaveRules.NoLinks(j.TargetDirectory); SaveRules.NoLinks(j.OldArchive);
        var rollback = Path.Combine(j.StageDirectory, "rollback-" + Guid.NewGuid().ToString("N"));
        ArchiveCodec.ExtractVerified(j.OldArchive, rollback, j.OldFiles, j.SaveKey, true);
        foreach (var old in j.OldFiles)
        {
            ct.ThrowIfCancellationRequested(); guard.EnsureStopped(); var target = Path.Combine(j.TargetDirectory, old.Path); SaveRules.NoLinks(target);
            var adjacent = Path.Combine(j.TargetDirectory, ".mist-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { CopyDurable(Path.Combine(rollback, old.Path), adjacent); File.Move(adjacent, target, true); }
            finally { if (File.Exists(adjacent)) File.Delete(adjacent); }
        }
        foreach (var fresh in j.NewFiles.Where(f => !j.OldFiles.Any(o => o.Path == f.Path)))
        { guard.EnsureStopped(); var path = Path.Combine(j.TargetDirectory, fresh.Path); SaveRules.NoLinks(path); if (File.Exists(path)) File.Delete(path); }
        var after = await scanner.StableScanAsync(j.TargetDirectory, j.SaveKey, true, ct);
        if (after.Fingerprint != SaveRules.Fingerprint(j.OldFiles)) throw new IOException("旧档还原后校验不一致，请保留恢复记录，不要启动游戏。");
        if (j.OldBaseline == null) { if (File.Exists(j.BaselinePath)) File.Delete(j.BaselinePath); }
        else JsonData.Write(j.BaselinePath, j.OldBaseline);
        foreach (var path in j.StagedFiles.Values)
        {
            if (!Path.GetFileName(path).StartsWith(".mist-", StringComparison.Ordinal) || Path.GetDirectoryName(Path.GetFullPath(path)) != Path.GetFullPath(j.TargetDirectory)) throw new IOException("恢复临时文件路径不合法。");
            if (File.Exists(path)) File.Delete(path);
        }
        JsonData.Write(state.JournalPath, j with { Phase = "complete" }); File.Delete(state.JournalPath);
    }
    private static void CopyDurable(string from, string to)
    { SaveRules.NoLinks(from); SaveRules.NoLinks(to); using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read); using var dest = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None); source.CopyTo(dest); dest.Flush(true); }
}
