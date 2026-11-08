using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VoiceBridge.App;

/// <summary>Only small official WeChat avatar images are downloaded after an explicit contact search.</summary>
internal sealed class AvatarCache : IDisposable
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeChatVoiceBridge", "avatars");
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(6) };
    private readonly SemaphoreSlim slots = new(3);
    private readonly object cacheGate = new();
    private int generation;

    private string FileFor(string account, string peer) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\0" + peer))).ToLowerInvariant() + ".img");

    public ImageSource? Cached(string account, string peer)
    {
        try { return Decode(File.ReadAllBytes(FileFor(account, peer))); }
        catch (Exception) { return null; }
    }

    public async Task<ImageSource?> GetAsync(string account, string peer, string? url, CancellationToken token)
    {
        var cacheGeneration = Volatile.Read(ref generation);
        if (Cached(account, peer) is { } existing) return existing;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Port is not (80 or 443))
            return null;
        var host = uri.IdnHost.ToLowerInvariant();
        if (!(host == "qlogo.cn" || host.EndsWith(".qlogo.cn", StringComparison.Ordinal) ||
              host == "qpic.cn" || host.EndsWith(".qpic.cn", StringComparison.Ordinal) ||
              host.EndsWith(".weixin.qq.com", StringComparison.Ordinal))) return null;
        if (uri.Scheme == "http") uri = new UriBuilder(uri) { Scheme = "https", Port = 443 }.Uri;
        if (uri.Scheme != "https") return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        token = deadline.Token;
        var entered = false;
        try
        {
            await slots.WaitAsync(token);
            entered = true;
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 256 * 1024) return null;
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/png" or "image/jpeg" or "image/gif")) return null;
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(chunk, token)) > 0)
            {
                if (buffer.Length + read > 256 * 1024) return null;
                buffer.Write(chunk, 0, read);
            }
            token.ThrowIfCancellationRequested();
            var bytes = buffer.ToArray();
            var image = Decode(bytes);
            lock (cacheGate)
            {
                if (cacheGeneration != generation || token.IsCancellationRequested) return null;
                Directory.CreateDirectory(directory);
                var file = FileFor(account, peer);
                var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, file, true); }
                finally { File.Delete(temporary); }
                return image;
            }
        }
        catch (Exception) { return null; }
        finally { if (entered) slots.Release(); }
    }

    private static ImageSource Decode(byte[] bytes)
    {
        if (bytes.Length > 256 * 1024) throw new InvalidDataException();
        using var stream = new MemoryStream(bytes, false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 96;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void Clear()
    {
        lock (cacheGate)
        {
            Interlocked.Increment(ref generation);
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*.img", SearchOption.TopDirectoryOnly))
                File.Delete(file);
        }
    }
    public void Dispose() => http.Dispose();
}
