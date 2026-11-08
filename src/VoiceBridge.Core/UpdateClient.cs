using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace VoiceBridge.Core;

public sealed class UpdateClient(HttpClient http, string currentVersion)
{
    private const int MaxManifestBytes = 64 * 1024;
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(30);

    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var request = Request(new Uri(UpdateManifest.Feed));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[MaxManifestBytes + 1];
            var length = await body.ReadAtLeastAsync(buffer, buffer.Length, false, timeout.Token);
            if (length > MaxManifestBytes) throw new InvalidDataException("更新清单过大。");
            var manifest = UpdateManifest.Parse(buffer.AsMemory(0, length));
            return UpdateManifest.CompareVersions(manifest.Version, currentVersion) > 0 ? manifest : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("检查更新超时，请重试。");
        }
    }

    public static string ArchiveName(UpdateManifest manifest) =>
        $"WeChat-Voice-Bridge-V{manifest.Version}-{manifest.Sha256}.zip";

    private static string StateName(UpdateManifest manifest) => ArchiveName(manifest) + ".state.json";

    // Persist the original six-field contract. File length, never a saved counter, is the resume offset.
    private static void WriteState(UpdateManifest manifest, string updates)
    {
        var path = Path.Combine(updates, StateName(manifest));
        UpdateInstaller.EnsurePlainPath(path);
        UpdateInstaller.EnsurePlainPath(path + ".tmp");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = manifest.Version, publishedAt = manifest.PublishedAt, url = manifest.Url.AbsoluteUri,
            sha256 = manifest.Sha256, size = manifest.Size, notes = manifest.Notes
        });
        File.WriteAllBytes(path + ".tmp", payload);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public async Task<UpdateManifest?> ReadPendingAsync(string updates, CancellationToken cancellationToken)
    {
        UpdateInstaller.EnsurePlainPath(updates);
        if (!Directory.Exists(updates)) return null;
        UpdateManifest? newest = null;
        foreach (var file in Directory.EnumerateFiles(updates, "*.zip.state.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc).Take(32))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                UpdateInstaller.EnsurePlainPath(file);
                if (new FileInfo(file).Length > MaxManifestBytes) continue;
                var manifest = UpdateManifest.Parse(await File.ReadAllBytesAsync(file, cancellationToken));
                if (Path.GetFileName(file) != StateName(manifest) ||
                    UpdateManifest.CompareVersions(manifest.Version, currentVersion) <= 0) continue;
                var archive = Path.Combine(updates, ArchiveName(manifest));
                UpdateInstaller.EnsurePlainPath(archive);
                UpdateInstaller.EnsurePlainPath(archive + ".part");
                if (!File.Exists(archive) && !File.Exists(archive + ".part")) continue;
                if (newest is null || UpdateManifest.CompareVersions(manifest.Version, newest.Version) > 0)
                    newest = manifest;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // A damaged sidecar must not prevent other valid cached downloads from being found.
            }
        }
        return newest;
    }

    public async Task<string?> GetCachedAsync(UpdateManifest manifest, string updates, CancellationToken cancellationToken)
    {
        var path = Path.Combine(updates, ArchiveName(manifest));
        UpdateInstaller.EnsurePlainPath(path);
        if (!File.Exists(path)) return null;
        if (await MatchesAsync(path, manifest, cancellationToken)) return path;
        File.Delete(path);
        return null;
    }

    public async Task<string> DownloadAsync(UpdateManifest manifest, string updates, IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateInstaller.EnsurePlainPath(updates);
        Directory.CreateDirectory(updates);
        var cached = await GetCachedAsync(manifest, updates, cancellationToken);
        if (cached is not null) { progress.Report(100); return cached; }
        var verified = Path.Combine(updates, ArchiveName(manifest));
        var partial = verified + ".part";
        UpdateInstaller.EnsurePlainPath(partial);
        WriteState(manifest, updates);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadIdleTimeout);
        try
        {
            long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (offset > manifest.Size) { File.Delete(partial); offset = 0; }
            if (offset == manifest.Size)
            {
                if (!await MatchesAsync(partial, manifest, cancellationToken))
                    throw new InvalidDataException("更新包 SHA-256 校验失败，请重新下载。");
                File.Move(partial, verified, overwrite: true);
                progress.Report(100);
                return verified;
            }
            using var request = Request(manifest.Url);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range?.Unit != "bytes" || range.From != offset || range.Length != manifest.Size ||
                    range.To != manifest.Size - 1)
                    throw new InvalidDataException("更新服务返回了无效的续传范围，请重新下载。");
            }
            else if (response.StatusCode == HttpStatusCode.OK) offset = 0; // Server ignored Range: replace, never append.
            else throw new InvalidDataException("更新服务返回了不支持的下载响应。");
            if (response.Content.Headers.ContentLength is long length && length != manifest.Size - offset)
                throw new InvalidDataException("更新包大小与清单不符。");

            progress.Report((int)(offset * 100 / manifest.Size));
            await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Open,
                FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                output.Position = offset;
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
                var buffer = new byte[64 * 1024];
                var lastPercent = (int)(offset * 100 / manifest.Size);
                while (true)
                {
                    timeout.CancelAfter(DownloadIdleTimeout);
                    var count = await body.ReadAsync(buffer, timeout.Token);
                    if (count == 0) break;
                    if (offset + count > manifest.Size) throw new InvalidDataException("更新包超过清单记录的大小。");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    offset += count;
                    var percent = (int)(offset * 100 / manifest.Size);
                    if (percent != lastPercent) { progress.Report(percent); lastPercent = percent; }
                }
            }
            // A truncated connection is resumable. Only an actual validation failure discards bytes.
            if (offset != manifest.Size) throw new IOException("下载中断，已保留进度，点击更新可继续下载。");
            if (!await MatchesAsync(partial, manifest, cancellationToken))
                throw new InvalidDataException("更新包 SHA-256 校验失败，请重新下载。");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, verified, overwrite: true);
            progress.Report(100);
            return verified;
        }
        catch (InvalidDataException)
        {
            File.Delete(partial);
            File.Delete(Path.Combine(updates, StateName(manifest)));
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("下载超时，已保留进度，可继续下载。");
        }
    }

    private static async Task<bool> MatchesAsync(string path, UpdateManifest manifest, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, useAsync: true);
        return file.Length == manifest.Size && Convert.ToHexString(await SHA256.HashDataAsync(file, token))
            .Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"WeChatVoiceBridge/{currentVersion}");
        return request;
    }
}
