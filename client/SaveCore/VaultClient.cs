using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Enshrouded.SaveCore;

public sealed class VaultClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string token;
    public VaultClient(string server, string accessToken, bool allowLoopbackDevelopment = false, HttpMessageHandler? testHandler = null)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/") throw new ArgumentException("服务器地址应为 https://域名，不要填写路径、账号或查询参数。");
        if (uri.Scheme != "https" && !(allowLoopbackDevelopment && uri.Scheme == "http" && uri.IsLoopback)) throw new ArgumentException("必须使用 HTTPS 服务器地址。");
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Any(char.IsWhiteSpace)) throw new ArgumentException("请输入有效访问码。");
        token = accessToken;
        http = new HttpClient(testHandler ?? new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = uri, Timeout = TimeSpan.FromMinutes(10) };
    }
    private HttpRequestMessage Request(HttpMethod method, string path)
    { var r = new HttpRequestMessage(method, path); r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return r; }
    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var message = (int)response.StatusCode switch { 401 => "访问码无效、已过期或已撤销，请联系管理员。", 403 or 404 => "无权访问或存档不存在，请检查所选世界。", 409 => "上传操作记录冲突，请查看云端列表后再处理。", 413 => "存档超过服务端大小限制。", 429 => "请求太频繁或服务器忙，请稍后重试。", 507 => "服务器磁盘空间或存档配额不足。", >= 300 and < 400 => "服务器返回了重定向，请填写最终 HTTPS 地址。", _ => "服务器请求失败（" + (int)response.StatusCode + "）。" };
        // Do not echo arbitrary server bodies: they may contain paths or sensitive values.
        await Task.CompletedTask; ct.ThrowIfCancellationRequested(); throw new ApiException((int)response.StatusCode, message);
    }
    private async Task<T> ReadAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using (request) using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
        { await EnsureSuccess(response, ct); await using var stream = await response.Content.ReadAsStreamAsync(ct); using var limited = new MemoryStream(); var buffer = new byte[81920]; int n; while ((n = await stream.ReadAsync(buffer, ct)) > 0) { if (limited.Length + n > 4 << 20) throw new IOException("服务器响应过大。"); limited.Write(buffer, 0, n); } return JsonSerializer.Deserialize<T>(limited.ToArray(), JsonData.Options) ?? throw new IOException("服务器响应格式错误。"); }
    }
    public Task<MeResponse> MeAsync(CancellationToken ct = default) => ReadAsync<MeResponse>(Request(HttpMethod.Get, "/v1/me"), ct);
    public Task<BackupPage> ListAsync(string world, string query, string sort, int page, int pageSize, long snapshot = 0, CancellationToken ct = default)
        => ReadAsync<BackupPage>(Request(HttpMethod.Get, $"/v1/backups?world_id={Uri.EscapeDataString(world)}&q={Uri.EscapeDataString(query)}&sort={sort}&page={page}&page_size={pageSize}&snapshot={snapshot}"), ct);
    public async Task<BackupInfo> UploadAsync(PendingUpload pending, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(pending.ArchivePath) || SaveRules.HashFile(pending.ArchivePath) != pending.Metadata.ArchiveSha256) throw new IOException("待上传的本地备份损坏或缺失，请保留现场并重新检查。");
        using var request = Request(HttpMethod.Post, "/v1/backups"); request.Headers.Add("Idempotency-Key", pending.OperationId);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(pending.Metadata, JsonData.Options), Encoding.UTF8, "application/json"), "metadata");
        multipart.Add(new ProgressFileContent(pending.ArchivePath, progress), "archive", "world.zip"); request.Content = multipart;
        return await ReadAsync<BackupInfo>(request, ct);
    }
    public async Task<string> DownloadAsync(BackupInfo backup, string folder, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        if (backup.ArchiveSize <= 0 || backup.ArchiveSize > SaveRules.MaxArchive) throw new IOException("服务端归档大小不合法。");
        Directory.CreateDirectory(folder); var path = Path.Combine(folder, "download.zip");
        using var request = Request(HttpMethod.Get, "/v1/backups/" + Uri.EscapeDataString(backup.Id));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); await EnsureSuccess(response, ct);
        if (response.Content.Headers.ContentLength is long size && size != backup.ArchiveSize) throw new IOException("下载长度与备份记录不一致。");
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            { var buffer = new byte[81920]; int n; long total = 0; while ((n = await input.ReadAsync(buffer, ct)) > 0) { total += n; if (total > backup.ArchiveSize) throw new IOException("下载超过声明大小。"); await output.WriteAsync(buffer.AsMemory(0, n), ct); progress?.Report(total); } output.Flush(true); if (total != backup.ArchiveSize) throw new IOException("下载未完成。"); }
            if (SaveRules.HashFile(path) != backup.ArchiveSha256) throw new IOException("下载校验失败。"); return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    public void Dispose() => http.Dispose();
    private sealed class ProgressFileContent : HttpContent
    {
        private readonly string path; private readonly IProgress<long>? progress;
        public ProgressFileContent(string path, IProgress<long>? progress) { this.path = path; this.progress = progress; Headers.ContentType = new("application/zip"); }
        protected override bool TryComputeLength(out long length) { length = new FileInfo(path).Length; return true; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        { await using var input = File.OpenRead(path); var buffer = new byte[81920]; int n; long total = 0; while ((n = await input.ReadAsync(buffer)) > 0) { await stream.WriteAsync(buffer.AsMemory(0, n)); total += n; progress?.Report(total); } }
    }
}
