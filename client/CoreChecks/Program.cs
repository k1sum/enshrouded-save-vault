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
        var scan = await scanner.StableScanAsync(local, "3ad85aea"); snapshot = await scanner.SnapshotAsync(local, "3ad85aea", scan, state.NewFolder("snapshots"));
        var dest = Path.Combine(root, "extracted"); ArchiveCodec.ExtractVerified(snapshot.ArchivePath, dest, scan.Files, "3ad85aea");
        Assert(File.ReadAllText(Path.Combine(dest, "3ad85aea")) == "new world", "wrong archive content");
        var wrong = scan.Files.Select(f => f with { Sha256 = new string('0', 64) }).ToList();
        await ExpectFailure(() => Task.Run(() => ArchiveCodec.ExtractVerified(snapshot.ArchivePath, Path.Combine(root, "bad-hash"), wrong, "3ad85aea")), "hash mismatch");
    });
    await Test("restore rolls back partial writes and preserves characters", async () => {
        File.WriteAllText(Path.Combine(local, "3ad85aea"), "valuable local progress"); File.WriteAllText(Path.Combine(local, "3ad85aea-1"), "old rotation");
        var old = await scanner.StableScanAsync(local, "3ad85aea"); state.SetBaseline(settings, new("old", old.Fingerprint, old.Files));
        var b = new BackupInfo { Id = "restored", WorldId = "shared", SaveKey = "3ad85aea", Files = snapshot!.Scan.Files, ArchiveSha256 = snapshot.ArchiveSha256 };
        var engine = new RestoreEngine(state, scanner, guard) { AfterWrite = n => { if (n == 1) throw new IOException("simulated disk failure"); } };
        await ExpectFailure(() => engine.RestoreAsync(settings, b, snapshot.ArchivePath), "partial write");
        Assert((await scanner.StableScanAsync(local, "3ad85aea")).Fingerprint == old.Fingerprint, "rollback lost files");
        Assert(state.GetBaseline(settings)?.BackupId == "old", "rollback lost baseline");
        Assert(!File.Exists(state.JournalPath), "rollback left journal");
        engine.AfterWrite = null; await engine.RestoreAsync(settings, b, snapshot.ArchivePath);
        Assert(File.ReadAllText(Path.Combine(local, "3ad85aea")) == "new world", "restore failed");
        Assert(!File.Exists(Path.Combine(local, "3ad85aea-1")), "stale managed file retained");
        Assert(File.ReadAllText(Path.Combine(local, "characters")) == "do not change" && File.ReadAllText(Path.Combine(local, "1234abcd")) == "other world", "other files touched");
    });
    await Test("interrupted restore is recoverable after process restarts", async () => {
        File.WriteAllText(Path.Combine(local, "3ad85aea"), "checkpoint before restore");
        var before = await scanner.StableScanAsync(local, "3ad85aea");
        var engine = new RestoreEngine(state, scanner, guard) { AfterWrite = _ => { guard.Running = true; throw new IOException("game started during restore"); } };
        var b = new BackupInfo { Id = "new", WorldId = "shared", SaveKey = "3ad85aea", Files = snapshot!.Scan.Files, ArchiveSha256 = snapshot.ArchiveSha256 };
        await ExpectFailure(() => engine.RestoreAsync(settings, b, snapshot.ArchivePath), "interrupted restore");
        Assert(File.Exists(state.JournalPath), "interruption not journaled"); guard.Running = false;
        await new RestoreEngine(state, scanner, guard).RecoverAsync();
        Assert((await scanner.StableScanAsync(local, "3ad85aea")).Fingerprint == before.Fingerprint, "restart recovery failed");
    });
    await Test("reject path escape, duplicate filenames, and symlinks", async () => {
        foreach (var name in new[] { "../3ad85aea", "3ad85aea:evil", "3ad85aea.", "3ad85aea/evil", "characters" })
            Assert(!SaveRules.ValidName(name, "3ad85aea"), "unsafe file accepted: " + name);
        var files = new List<SaveFile> { new("3ad85aea_info", 0, new string('0',64)), new("3ad85aea_INFO", 0, new string('0',64)) };
        await ExpectFailure(() => Task.Run(() => SaveRules.ValidateManifest(files, "3ad85aea")), "case collision");
        var archive = Path.Combine(root, "malicious.zip"); using (var z = ZipFile.Open(archive, ZipArchiveMode.Create)) { var e = z.CreateEntry("../escape"); using var w = new StreamWriter(e.Open()); w.Write("evil"); }
        await ExpectFailure(() => Task.Run(() => ArchiveCodec.ExtractVerified(archive, Path.Combine(root, "unsafe"), snapshot!.Scan.Files, "3ad85aea")), "unexpected ZIP entry");
        if (!OperatingSystem.IsWindows()) { var link = Path.Combine(local, "3ad85aea_link"); File.CreateSymbolicLink(link, Path.Combine(local, "characters")); await ExpectFailure(() => scanner.StableScanAsync(local, "3ad85aea"), "symlink"); File.Delete(link); }
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
    Console.WriteLine($"All {passed} core checks passed.");
}
finally { try { Directory.Delete(root, true); } catch { } }

sealed class FakeGuard : IGameGuard { public bool Running; public bool IsRunning() => Running; public void EnsureStopped() { if (Running) throw new IOException("game is running"); } }
sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request); }
