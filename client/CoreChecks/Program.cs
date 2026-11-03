using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Enshrouded.SaveCore;

var root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "enshrouded-core-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task ExpectFailure(Func<Task> action, string description) { try { await action(); } catch (Exception) { return; } throw new Exception("Expected failure: " + description); }
async Task Test(string name, Func<Task> test) { await test(); passed++; Console.WriteLine("PASS " + name); }
var guard = new FakeGuard();
var scanner = new SaveScanner(guard, TimeSpan.Zero);
var state = new ClientState(Path.Combine(root, "state"));
var local = Path.Combine(root, "local"); Directory.CreateDirectory(local);
var settings = new ClientSettings { Server = "https://save.example.com", SaveDirectory = local, World = new("shared", "friends", "共同世界", "3ad85aea", true), FilesConfirmed = true, SteamCloudAcknowledged = true };
File.WriteAllText(Path.Combine(local, "3ad85aea"), "old world");
File.WriteAllText(Path.Combine(local, "3ad85aea_info"), "old info");
File.WriteAllText(Path.Combine(local, "characters"), "do not change");
File.WriteAllText(Path.Combine(local, "1234abcd"), "other world");

try
{
    await Test("scan separates world files, compares contents, blocks running game", async () => {
        var c = await scanner.CheckAsync(settings, state); Assert(c.Scan.Files.Count == 2 && c.IsFirstBackup, "incorrect world group");
        state.SetBaseline(settings, new("baseline", c.Scan.Fingerprint, c.Scan.Files)); Assert(!(await scanner.CheckAsync(settings, state)).HasChanges, "baseline ignored");
        File.WriteAllText(Path.Combine(local, "3ad85aea"), "new world"); Assert((await scanner.CheckAsync(settings, state)).HasChanges, "content change missed");
        guard.Running = true; await ExpectFailure(async () => { await scanner.CheckAsync(settings, state); }, "running game"); guard.Running = false;
    });
    LocalSnapshot? snapshot = null;
    await Test("snapshot roundtrip and checksum verification", async () => {
        var scan = await scanner.StableScanAsync(local, settings.Layout); snapshot = await scanner.SnapshotAsync(local, settings.Layout, scan, state.NewFolder("snapshots"));
        var dest = Path.Combine(root, "extracted"); ArchiveCodec.ExtractVerified(snapshot.ArchivePath, dest, scan.Files, "3ad85aea");
        Assert(File.ReadAllText(Path.Combine(dest, "3ad85aea")) == "new world", "wrong archive content");
        var wrong = scan.Files.Select(f => f with { Sha256 = new string('0', 64) }).ToList();
        await ExpectFailure(() => Task.Run(() => ArchiveCodec.ExtractVerified(snapshot.ArchivePath, Path.Combine(root, "bad-hash"), wrong, "3ad85aea")), "hash mismatch");
    });
    await Test("restore rolls back partial writes and preserves characters", async () => {
        File.WriteAllText(Path.Combine(local, "3ad85aea"), "valuable local progress"); File.WriteAllText(Path.Combine(local, "3ad85aea-1"), "old rotation");
        var old = await scanner.StableScanAsync(local, settings.Layout); state.SetBaseline(settings, new("old", old.Fingerprint, old.Files));
        var b = new BackupInfo { Id = "restored", WorldId = "shared", SaveKey = "3ad85aea", Files = snapshot!.Scan.Files, ArchiveSha256 = snapshot.ArchiveSha256 };
        var engine = new RestoreEngine(state, scanner, guard) { AfterWrite = n => { if (n == 1) throw new IOException("simulated disk failure"); } };
        await ExpectFailure(() => engine.RestoreAsync(settings, b, snapshot.ArchivePath), "partial write");
        Assert((await scanner.StableScanAsync(local, settings.Layout)).Fingerprint == old.Fingerprint, "rollback lost files");
        Assert(state.GetBaseline(settings)?.BackupId == "old", "rollback lost baseline");
        Assert(!File.Exists(state.JournalPath), "rollback left journal");
        engine.AfterWrite = null; await engine.RestoreAsync(settings, b, snapshot.ArchivePath);
        Assert(File.ReadAllText(Path.Combine(local, "3ad85aea")) == "new world", "restore failed");
        Assert(!File.Exists(Path.Combine(local, "3ad85aea-1")), "stale managed file retained");
        Assert(File.ReadAllText(Path.Combine(local, "characters")) == "do not change" && File.ReadAllText(Path.Combine(local, "1234abcd")) == "other world", "other files touched");
    });
    await Test("interrupted restore is recoverable after process restarts", async () => {
        File.WriteAllText(Path.Combine(local, "3ad85aea"), "checkpoint before restore");
        var before = await scanner.StableScanAsync(local, settings.Layout);
        var engine = new RestoreEngine(state, scanner, guard) { AfterWrite = _ => { guard.Running = true; throw new IOException("game started during restore"); } };
        var b = new BackupInfo { Id = "new", WorldId = "shared", SaveKey = "3ad85aea", Files = snapshot!.Scan.Files, ArchiveSha256 = snapshot.ArchiveSha256 };
        await ExpectFailure(() => engine.RestoreAsync(settings, b, snapshot.ArchivePath), "interrupted restore");
        Assert(File.Exists(state.JournalPath), "interruption not journaled"); guard.Running = false;
        await new RestoreEngine(state, scanner, guard).RecoverAsync();
        Assert((await scanner.StableScanAsync(local, settings.Layout)).Fingerprint == before.Fingerprint, "restart recovery failed");
    });
    await Test("reject path escape, duplicate filenames, and symlinks", async () => {
        foreach (var name in new[] { "../3ad85aea", "3ad85aea:evil", "3ad85aea.", "3ad85aea/evil", "characters" })
            Assert(!SaveRules.ValidName(name, "3ad85aea"), "unsafe file accepted: " + name);
        var files = new List<SaveFile> { new("3ad85aea_info", 0, new string('0',64)), new("3ad85aea_INFO", 0, new string('0',64)) };
        await ExpectFailure(() => Task.Run(() => SaveRules.ValidateManifest(files, "3ad85aea")), "case collision");
        var archive = Path.Combine(root, "malicious.zip"); using (var z = ZipFile.Open(archive, ZipArchiveMode.Create)) { var e = z.CreateEntry("../escape"); using var w = new StreamWriter(e.Open()); w.Write("evil"); }
        await ExpectFailure(() => Task.Run(() => ArchiveCodec.ExtractVerified(archive, Path.Combine(root, "unsafe"), snapshot!.Scan.Files, "3ad85aea")), "unexpected ZIP entry");
        if (!OperatingSystem.IsWindows()) { var link = Path.Combine(local, "3ad85aea_link"); File.CreateSymbolicLink(link, Path.Combine(local, "characters")); await ExpectFailure(() => scanner.StableScanAsync(local, settings.Layout), "symlink"); File.Delete(link); }
    });
    await Test("pending upload persists no access token and validates source package", async () => {
        var meta = new UploadMetadata("shared", "pending", "note", null, snapshot!.ArchiveSha256, snapshot.Scan.Files);
        var pending = new PendingUpload(settings.Server, settings.ProfileKey, "unique", meta, snapshot.ArchivePath, snapshot.Scan.Fingerprint);
        state.SavePending(settings, pending); Assert(state.GetPending(settings)?.OperationId == "unique", "pending task lost");
        var handler = new FakeHandler(async request => {
            Assert(request.Headers.Authorization?.Parameter == "test-token", "authorization missing"); Assert(request.Headers.GetValues("Idempotency-Key").Single() == "unique", "idempotency missing");
            var payload = await request.Content!.ReadAsStringAsync(); Assert(!payload.Contains(settings.SaveDirectory), "absolute path leaked");
            return new(HttpStatusCode.Created) { Content = new StringContent(JsonSerializer.Serialize(new BackupInfo { Id = "saved", WorldId = "shared", SaveKey = "3ad85aea", Files = meta.Files }, JsonData.Options)) };
        });
        using var client = new VaultClient(settings.Server, "test-token", false, handler); var saved = await client.UploadAsync(pending); Assert(saved.Id == "saved", "upload JSON decode failed");
        Assert(!File.ReadAllText(state.PendingPath(settings)).Contains("test-token"), "secret persisted");
        await ExpectFailure(() => Task.Run(() => new VaultClient("http://example.com", "token")), "plaintext credentials");
        using var redirected = new VaultClient(settings.Server, "token", false, new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect))));
        await ExpectFailure(() => redirected.MeAsync(), "redirect accepted");
    });
    await Test("game 1.0: slot-free cloud file, cross-slot restore, rollback, characters/global/backups untouched", async () => {
        Assert(SaveRules.ValidKey("world.ksd") && !SaveRules.ValidKey("world0.ksd") && !SaveRules.ValidKey("World.ksd"), "1.0 save key rules");
        foreach (var name in new[] { "world0.ksd", "character0.ksd", "global.ksd", "world.ksd.backup-0", "../world.ksd", "3ad85aea" }) Assert(!SaveRules.ValidName(name, "world.ksd"), "1.0 accepted " + name);
        var ksd = new WorldInfo("ksd", "friends", "1.0 世界", "world.ksd", true);
        // PC A: the shared world is slot 0; next to it sit another world, the character, global data and the game's own folders.
        var dirA = Path.Combine(root, "remote-a"); Directory.CreateDirectory(Path.Combine(dirA, "backup")); Directory.CreateDirectory(Path.Combine(dirA, "migrated"));
        foreach (var (n, v) in new[] { ("world0.ksd", "A progress"), ("world1.ksd", "A other world"), ("character0.ksd", "A char"), ("global.ksd", "A global"), ("user_settings.json", "{}"), ("backup/world0.ksd.backup-0", "A game backup"), ("migrated/3ad85aea", "early access") }) File.WriteAllText(Path.Combine(dirA, n), v);
        var a = new ClientSettings { Server = "https://save.example.com", SaveDirectory = dirA, World = ksd, WorldSlot = 0, FilesConfirmed = true, SteamCloudAcknowledged = true };
        var stateA = new ClientState(Path.Combine(root, "state-a"));
        var check = await scanner.CheckAsync(a, stateA);
        Assert(check.Scan.Files.Count == 1 && check.Scan.Files[0].Path == "world.ksd" && check.IsFirstBackup, "1.0 scan should see only the chosen slot, under its cloud name");
        var pack = await scanner.SnapshotAsync(dirA, a.Layout, check.Scan, stateA.NewFolder("outbox"));
        using (var z = ZipFile.OpenRead(pack.ArchivePath)) Assert(z.Entries.Count == 1 && z.Entries[0].FullName == "world.ksd", "archive must carry the slot-free name");
        Assert(SaveRules.KsdWorlds(dirA).Select(w => w.Slot).Order().SequenceEqual([0, 1]), "slot listing");
        // PC B: the shared world lives in slot 2; slot 0 is an unrelated world.
        var dirB = Path.Combine(root, "remote-b"); Directory.CreateDirectory(dirB);
        foreach (var (n, v) in new[] { ("world2.ksd", "B before"), ("world0.ksd", "B unrelated"), ("character0.ksd", "B char"), ("global.ksd", "B global") }) File.WriteAllText(Path.Combine(dirB, n), v);
        var b = a with { SaveDirectory = dirB, WorldSlot = 2 }; var stateB = new ClientState(Path.Combine(root, "state-b"));
        Assert(a.ProfileKey != (a with { WorldSlot = 2 }).ProfileKey, "slots must not share baselines");
        var backup = new BackupInfo { Id = "ksd-1", WorldId = "ksd", SaveKey = "world.ksd", Files = check.Scan.Files, ArchiveSha256 = pack.ArchiveSha256 };
        var failing = new RestoreEngine(stateB, scanner, guard) { AfterWrite = _ => throw new IOException("simulated disk failure") };
        await ExpectFailure(() => failing.RestoreAsync(b, backup, pack.ArchivePath), "1.0 partial write");
        Assert(File.ReadAllText(Path.Combine(dirB, "world2.ksd")) == "B before" && !File.Exists(stateB.JournalPath), "1.0 rollback");
        var interrupted = new RestoreEngine(stateB, scanner, guard) { AfterWrite = _ => { guard.Running = true; throw new IOException("game started during restore"); } };
        await ExpectFailure(() => interrupted.RestoreAsync(b, backup, pack.ArchivePath), "1.0 interrupted restore");
        Assert(JsonData.Read<RestoreJournal>(stateB.JournalPath).Slot == 2, "journal must remember the slot"); guard.Running = false;
        await new RestoreEngine(stateB, scanner, guard).RecoverAsync();
        Assert(File.ReadAllText(Path.Combine(dirB, "world2.ksd")) == "B before", "1.0 recovery after restart");
        await new RestoreEngine(stateB, scanner, guard).RestoreAsync(b, backup, pack.ArchivePath);
        Assert(File.ReadAllText(Path.Combine(dirB, "world2.ksd")) == "A progress", "cross-slot restore");
        Assert(!File.Exists(Path.Combine(dirB, "world.ksd")), "cloud name leaked to disk");
        Assert(File.ReadAllText(Path.Combine(dirB, "world0.ksd")) == "B unrelated" && File.ReadAllText(Path.Combine(dirB, "character0.ksd")) == "B char" && File.ReadAllText(Path.Combine(dirB, "global.ksd")) == "B global", "other slots, character or global touched");
        Assert(!(await scanner.CheckAsync(b, stateB)).HasChanges, "baseline after 1.0 restore");
        // A new host restores into a slot that does not exist yet.
        var c = b with { WorldSlot = 5 }; await new RestoreEngine(stateB, scanner, guard).RestoreAsync(c, backup, pack.ArchivePath);
        Assert(File.ReadAllText(Path.Combine(dirB, "world5.ksd")) == "A progress", "restore into empty slot");
        // Journals written by 1.0.0 have no slot field.
        var legacyJournal = Path.Combine(root, "old-journal.json"); File.WriteAllText(legacyJournal, """{"target_directory":"x","save_key":"3ad85aea","old_archive":"x","old_files":[],"new_files":[],"baseline_path":"x","stage_directory":"x","staged_files":{},"phase":"writing"}""");
        Assert(JsonData.Read<RestoreJournal>(legacyJournal).Slot == 0, "1.0.0 journal compatibility");
        Assert(File.ReadAllText(Path.Combine(dirA, "backup", "world0.ksd.backup-0")) == "A game backup" && File.ReadAllText(Path.Combine(dirA, "migrated", "3ad85aea")) == "early access", "game folders touched");
    });
    var integrationUrl = Environment.GetEnvironmentVariable("ENSHROUDED_TEST_URL");
    if (!string.IsNullOrEmpty(integrationUrl)) await Test("real Go server and C# client upload/download/restore interoperability", async () => {
        using var client = new VaultClient(integrationUrl, Environment.GetEnvironmentVariable("ENSHROUDED_TEST_TOKEN") ?? "", true);
        var who = await client.MeAsync(); Assert(who.Identity.User.Id == "a", "wrong server identity");
        var meta = new UploadMetadata("shared", "跨端集成", "中文备注：装备在二楼", null, snapshot!.ArchiveSha256, snapshot.Scan.Files);
        var pending = new PendingUpload(integrationUrl, "test", Guid.NewGuid().ToString("N"), meta, snapshot.ArchivePath, snapshot.Scan.Fingerprint);
        var saved = await client.UploadAsync(pending); var retry = await client.UploadAsync(pending); Assert(saved.Id == retry.Id, "real idempotency failed");
        var list = await client.ListAsync("shared", "中文备注", "newest", 1, 5); Assert(list.Items.Count == 1 && list.Items[0].Uploader == "A", "real search/identity failed");
        var download = await client.DownloadAsync(saved, state.NewFolder("real-download"));
        File.WriteAllText(Path.Combine(local, "3ad85aea"), "local before real restore");
        await new RestoreEngine(state, scanner, guard).RestoreAsync(settings with { Server = integrationUrl }, saved, download);
        Assert(File.ReadAllText(Path.Combine(local, "3ad85aea")) == "new world", "real restore failed");
    });
    if (!string.IsNullOrEmpty(integrationUrl)) await Test("real Go server: game 1.0 world uploaded from slot 0 and restored into slot 3", async () => {
        using var client = new VaultClient(integrationUrl, Environment.GetEnvironmentVariable("ENSHROUDED_TEST_TOKEN") ?? "", true);
        var ksd = (await client.MeAsync()).Worlds.Single(w => w.Id == "ksd"); Assert(ksd.SaveKey == "world.ksd", "server world format");
        var src = Path.Combine(root, "int-a"); var dst = Path.Combine(root, "int-b"); Directory.CreateDirectory(src); Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(src, "world0.ksd"), "shared 1.0 world"); File.WriteAllText(Path.Combine(src, "character0.ksd"), "char");
        File.WriteAllText(Path.Combine(dst, "world3.ksd"), "older"); File.WriteAllText(Path.Combine(dst, "global.ksd"), "keep");
        var up = new ClientSettings { Server = integrationUrl, SaveDirectory = src, World = ksd, WorldSlot = 0, FilesConfirmed = true, SteamCloudAcknowledged = true };
        var st = new ClientState(Path.Combine(root, "int-state")); var scan = await scanner.StableScanAsync(src, up.Layout);
        var pack = await scanner.SnapshotAsync(src, up.Layout, scan, st.NewFolder("outbox"));
        var saved = await client.UploadAsync(new PendingUpload(integrationUrl, up.ProfileKey, Guid.NewGuid().ToString("N"), new("ksd", "1.0 世界", "", null, pack.ArchiveSha256, scan.Files), pack.ArchivePath, scan.Fingerprint));
        Assert(saved.Files.Single().Path == "world.ksd", "server stored a slot name");
        var file = await client.DownloadAsync(saved, st.NewFolder("downloads"));
        await new RestoreEngine(new ClientState(Path.Combine(root, "int-state-b")), scanner, guard).RestoreAsync(up with { SaveDirectory = dst, WorldSlot = 3 }, saved, file);
        Assert(File.ReadAllText(Path.Combine(dst, "world3.ksd")) == "shared 1.0 world" && File.ReadAllText(Path.Combine(dst, "global.ksd")) == "keep", "real 1.0 restore");
    });
    Console.WriteLine($"All {passed} core checks passed.");
}
finally { try { Directory.Delete(root, true); } catch { } }

sealed class FakeGuard : IGameGuard { public bool Running; public bool IsRunning() => Running; public void EnsureStopped() { if (Running) throw new IOException("game is running"); } }
sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request); }
