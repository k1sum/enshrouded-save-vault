using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Enshrouded.SaveCore;

namespace Enshrouded.SaveDesktop;

// A real Go daemon and real HTTP/ZIP operations, confined to generated demo data.
public sealed class DemoBackend(string root) : IAsyncDisposable
{
    public string Root { get; } = Path.GetFullPath(root);
    public string Server { get; private set; } = "";
    public WorldInfo World { get; } = new("shared", "friends", "共同世界（示例）", "3ad85aea", true);
    public Dictionary<string, string> Tokens { get; } = [];
    private Process? process;
    private HttpClient? admin;
    private StreamWriter? log;
    public async Task StartAsync()
    {
        Directory.CreateDirectory(Root);
        var serverBinary = Path.Combine(AppContext.BaseDirectory, "save-server");
        if (!File.Exists(serverBinary)) throw new IOException("缺少随应用打包的本机服务程序。");
        var portFile = Path.Combine(Root, "port.txt");
        var requestedPort = File.Exists(portFile) && int.TryParse(File.ReadAllText(portFile), out var savedPort) && savedPort > 1024 && savedPort < 65536 ? savedPort : 0;
        var listener = new TcpListener(IPAddress.Loopback, requestedPort); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); File.WriteAllText(portFile, port.ToString());
        Server = $"http://127.0.0.1:{port}";
        var cloud = Path.Combine(Root, "cloud"); Directory.CreateDirectory(cloud);
        // Unix-domain socket paths have a small platform limit. Keep the daemon data path short.
        if (Encoding.UTF8.GetByteCount(Path.Combine(cloud, "admin.sock")) > 100) throw new IOException("演示目录过长，请使用较短的 --data-root 路径。");
        var start = new ProcessStartInfo(serverBinary) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "--data", cloud, "serve", "--listen", $"127.0.0.1:{port}", "--allow-http-dev", "--min-free-bytes", "0" }) start.ArgumentList.Add(arg);
        log = new StreamWriter(Path.Combine(Root, "server.log"), true) { AutoFlush = true };
        process = new Process { StartInfo = start };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
        process.Start(); process.BeginErrorReadLine(); process.BeginOutputReadLine();
        using (var ping = new HttpClient { Timeout = TimeSpan.FromSeconds(1) })
        {
            var ready = false;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (process.HasExited) throw new IOException("本机服务启动失败，请查看演示目录中的 server.log。");
                try { var response = await ping.GetAsync(Server + "/healthz"); if (response.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { } catch (TaskCanceledException) { }
                await Task.Delay(100);
            }
            if (!ready) throw new IOException("本机演示服务启动超时。");
        }
        var handler = new SocketsHttpHandler { ConnectCallback = async (_, ct) => { var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified); try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(cloud, "admin.sock")), ct); return new NetworkStream(socket, true); } catch { socket.Dispose(); throw; } } };
        admin = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var state = await Admin(new { action = "status" });
        foreach (var pair in new[] { ("me", "我"), ("a", "A"), ("b", "B") })
        { if (!state.GetProperty("users").TryGetProperty(pair.Item1, out _)) await Admin(new { action = "user.add", id = pair.Item1, name = pair.Item2 }); }
        if (!state.GetProperty("libraries").TryGetProperty("friends", out _)) await Admin(new { action = "library.add", id = "friends", name = "演示存档库" });
        if (!state.GetProperty("worlds").TryGetProperty("shared", out _)) await Admin(new { action = "world.add", id = "shared", library_id = "friends", name = World.Name, save_key = World.SaveKey });
        // This directory is used exclusively by the demo. Credentials never leave memory.
        await Admin(new { action = "token.revoke-all" });
        foreach (var user in new[] { "me", "a", "b" })
        {
            await Admin(new { action = "member.grant", user_id = user, library_id = "friends", write = true });
            var issued = await Admin(new { action = "token.issue", user_id = user, device = "Mac 本机演示", days = 1 }); Tokens[user] = issued.GetProperty("token").GetString()!;
        }
        if (state.GetProperty("backup_count").GetInt32() == 0) await SeedAsync();
        var owner = SettingsFor("me"); var ownerState = StateFor("me");
        if (!Directory.EnumerateFiles(owner.SaveDirectory).Any())
        {
            using var api = ApiFor("me"); var latest = (await api.ListAsync("shared", "", "newest", 1, 1)).Items[0];
            var file = await api.DownloadAsync(latest, ownerState.NewFolder("downloads"));
            var scanner = new SaveScanner(new DemoGuard(), TimeSpan.FromMilliseconds(300));
            await new RestoreEngine(ownerState, scanner, new DemoGuard()).RestoreAsync(owner, latest, file);
            SimulateProgress("me");
        }
    }
    private async Task<JsonElement> Admin(object request)
    {
        using var response = await admin!.PostAsync("http://unix/admin", new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync(); if (!response.IsSuccessStatusCode) throw new IOException("演示服务配置失败：" + body);
        using var parsed = JsonDocument.Parse(body); return parsed.RootElement.Clone();
    }
    private async Task SeedAsync()
    {
        var path = Path.Combine(Root, "seed-world"); Directory.CreateDirectory(path);
        var scanner = new SaveScanner(new DemoGuard(), TimeSpan.Zero);
        var samples = new[] { ("me", "三人新世界", "我们的第一个共同世界。"), ("a", "营地第一晚", "床和储物箱都准备好了。"), ("b", "铁匠安置好了", "铁匠在营地，下次可以升级装备。"), ("a", "打完第一个 Boss", "战利品在门口箱子里。"), ("b", "探索北边结束", "材料都带回来了，先回家补给。"), ("me", "主基地建好了", "材料在一楼，二楼给 A、B 留了装备。") };
        string? parent = null; var i = 0;
        foreach (var (user, name, note) in samples)
        {
            File.WriteAllText(Path.Combine(path, World.SaveKey), $"DEMO DATA ONLY - NOT A GAME SAVE\n{name}\n{i++}");
            File.WriteAllText(Path.Combine(path, World.SaveKey + "_info"), JsonSerializer.Serialize(new { demo = true, world = "共同世界", revision = i }));
            File.WriteAllText(Path.Combine(path, World.SaveKey + "-1"), "DEMO backup " + i);
            var scan = await scanner.StableScanAsync(path, World.SaveKey);
            var local = await scanner.SnapshotAsync(path, World.SaveKey, scan, Path.Combine(Root, "seed-packages", Guid.NewGuid().ToString("N")));
            using var api = ApiFor(user);
            var pending = new PendingUpload(Server, "demo", Guid.NewGuid().ToString("N"), new("shared", name, note, parent, local.ArchiveSha256, scan.Files), local.ArchivePath, scan.Fingerprint);
            parent = (await api.UploadAsync(pending)).Id;
        }
    }
    public ClientSettings SettingsFor(string user)
    { var path = Path.Combine(Root, "players", user, "world"); Directory.CreateDirectory(path); return new() { Server = Server, World = World, SaveDirectory = path, FilesConfirmed = true, SteamCloudAcknowledged = true }; }
    public ClientState StateFor(string user) => new(Path.Combine(Root, "players", user, "client"));
    public VaultClient ApiFor(string user) => new(Server, Tokens[user], true);
    public void SimulateProgress(string user)
    {
        var path = SettingsFor(user).SaveDirectory; if (!File.Exists(Path.Combine(path, World.SaveKey))) throw new IOException("先选择一份云端存档恢复，然后再模拟游玩。");
        File.AppendAllText(Path.Combine(path, World.SaveKey), "\nDEMO new progress " + DateTimeOffset.UtcNow.ToString("O"));
        File.WriteAllText(Path.Combine(path, World.SaveKey + "_info"), JsonSerializer.Serialize(new { demo = true, changed_at = DateTimeOffset.UtcNow }));
    }
    public async ValueTask DisposeAsync()
    {
        admin?.Dispose();
        if (process != null) { try { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } } catch (InvalidOperationException) { } process.Dispose(); }
        log?.Dispose();
    }
}
public sealed class DemoGuard : IGameGuard { public bool IsRunning() => false; public void EnsureStopped() { } }
