using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.WeChat;

public sealed record ContactEntry(string Id, string Name, string Wechat, string? AvatarUrl = null);

/// <summary>
/// A short-lived, read-only helper that lists contacts and reads the last
/// incoming message of one conversation. It never writes to the database and
/// it is only started when the user asks for the contact list or a preview.
/// </summary>
public sealed class WeChatDirectory : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string python;
    private readonly string script;
    private readonly Func<IReadOnlyList<ReadingRule>>? rules;
    private Process? process;
    private WorkerJob? job;
    private SessionCache? cache;
    private Task? stderrDrain;
    private readonly Guid session = Guid.NewGuid();
    private const long Generation = 1;
    private readonly ContactSearchCache contactIndex = new();

    public WeChatDirectory(string python, string script,
        Func<IReadOnlyList<ReadingRule>>? rules = null)
    {
        this.python = python;
        this.script = script;
        this.rules = rules;
    }

    public async Task<IReadOnlyList<ContactEntry>> SearchAsync(WeChatSession session, string keyword,
        CancellationToken token)
    {
        session.CheckRunning();
        token.ThrowIfCancellationRequested();
        if (contactIndex.TrySearch(session, keyword, out var cached)) return cached;
        var fingerprint = ContactSearchCache.Fingerprint(session);
        await EnsureStartedAsync(session, token);
        var packet = await ExchangeAsync("contacts", new { query = keyword ?? "" }, token, "contacts");
        var items = new List<ContactEntry>();
        foreach (var row in packet.GetProperty("contacts").EnumerateArray())
        {
            var id = row.GetProperty("id").GetString();
            var name = row.GetProperty("name").GetString();
            // The wechat id is the one the user reads in WeChat, the plain
            // wxid is only the fallback for a contact without one.
            var wechat = row.TryGetProperty("wechat", out var alias) ? alias.GetString() : null;
            var avatar = row.TryGetProperty("avatarUrl", out var image) ? image.GetString() : null;
            avatar = SafeAvatar(avatar);
            if (!string.IsNullOrWhiteSpace(id))
                items.Add(new(id, string.IsNullOrWhiteSpace(name) ? id : name,
                    string.IsNullOrWhiteSpace(wechat) ? id : wechat, avatar));
        }
        if (fingerprint != null)
        {
            var rows = new List<ContactSearchCache.Row>();
            var offset = 0;
            while (true)
            {
                var batch = await ExchangeAsync("contactindex", new { offset }, token, "contactindex");
                var total = batch.GetProperty("total").GetInt32();
                var next = batch.GetProperty("nextOffset").GetInt32();
                if (total > 50_000 || next < offset || next > total || next == offset && offset < total) break;
                foreach (var row in batch.GetProperty("rows").EnumerateArray())
                {
                    var entry = row[0];
                    var id = entry.GetProperty("id").GetString() ?? "";
                    if (id.Length == 0 || id.EndsWith("@chatroom", StringComparison.Ordinal) || id == "filehelper") continue;
                    rows.Add(new(new(id, entry.GetProperty("name").GetString() ?? id,
                        entry.GetProperty("wechat").GetString() ?? id,
                        entry.TryGetProperty("avatarUrl", out var avatar) ? SafeAvatar(avatar.GetString()) : null),
                        row[1].EnumerateArray().Select(field => field.GetString() ?? "").ToArray(), row[2].GetDouble()));
                }
                offset = next;
                if (offset == total)
                {
                    token.ThrowIfCancellationRequested();
                    contactIndex.Save(session, fingerprint, rows.ToArray());
                    break;
                }
            }
        }
        return items;
    }

    internal static string? SafeAvatar(string? value)
    {
        if (value is null || value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 || uri.Scheme is not ("http" or "https") || uri.Port is not (80 or 443)) return null;
        return uri.Scheme == "http" ? new UriBuilder(uri) { Scheme = "https", Port = 443 }.Uri.AbsoluteUri : value;
    }

    public async Task<string> LastMessageAsync(WeChatSession session, string conversation, CancellationToken token)
    {
        await EnsureStartedAsync(session, token);
        var packet = await ExchangeAsync("lastmessage", new { conversation }, token, "lastmessage");
        return packet.GetProperty("text").GetString() ?? "";
    }

    private async Task EnsureStartedAsync(WeChatSession wechat, CancellationToken token)
    {
        if (process is { HasExited: false })
            return;
        await ResetAsync();
        if (!File.Exists(python) || !File.Exists(script))
            throw new BridgeException("缺少 Python 3.12 Worker。");
        cache = new SessionCache(Guid.NewGuid(), wechat.Root);
        job = new();
        var info = new ProcessStartInfo(python)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-I", "-B", "-u", script, "--fixture", cache.PathName })
            info.ArgumentList.Add(arg);
        info.Environment.Remove("FISH_API_KEY");
        process = Process.Start(info) ?? throw new BridgeException("无法启动消息 Worker。");
        job.Attach(process);
        stderrDrain = WorkerLog.DrainAsync(process.StandardError);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await SendAsync(new { type = "start", session = session.ToString(), generation = Generation,
                account = wechat.Account, conversation = "", mode = "browse",
                root = wechat.Root, pids = wechat.Pids,
                version = wechat.Version,
                rules = rules?.Invoke()?.Select(rule => new { match = rule.Match, say = rule.Say,
                    anywhere = rule.Anywhere, scope = rule.Scope }).ToArray() }, deadline.Token);
            var capabilities = await ReceiveAsync(deadline.Token, "capabilities");
            if (capabilities.GetProperty("mode").GetString() != "browse")
                throw new BridgeException("消息 Worker 返回了意外的读取模式。");
            await ReceiveAsync(deadline.Token, "ready");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new BridgeException("读取微信联系人超时，请确认微信已登录。");
        }
        catch
        {
            await ResetAsync();
            throw;
        }
    }

    private async Task ResetAsync()
    {
        if (process is { } child && !child.HasExited)
        {
            try
            {
                using var grace = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
                await SendAsync(new { type = "stop", session = session.ToString(), generation = Generation }, grace.Token);
                child.StandardInput.Close();
                while (await child.StandardOutput.ReadLineAsync(grace.Token) != null) { }
                await child.WaitForExitAsync(grace.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException) { }
            finally
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            }
        }
        job?.Dispose();
        job = null;
        process?.Dispose();
        process = null;
        cache?.Dispose();
        cache = null;
    }

    private async Task<JsonElement> ExchangeAsync(string type, object payload, CancellationToken token,
        params string[] expected)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await SendAsync(Envelope(type, payload), deadline.Token);
            return await ReceiveAsync(deadline.Token, expected);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new BridgeException("消息 Worker 无响应。");
        }
    }

    /// <summary>
    /// Builds a protocol packet. The worker silently drops packets whose session
    /// or generation do not match the running session, so every command has to
    /// carry the type together with both identifiers.
    /// </summary>
    private Dictionary<string, object?> Envelope(string type, object payload)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["session"] = session.ToString(),
            ["generation"] = Generation
        };
        foreach (var property in JsonSerializer.SerializeToElement(payload).EnumerateObject())
            envelope[property.Name] = property.Value;
        return envelope;
    }

    private async Task SendAsync<T>(T payload, CancellationToken token)
    {
        await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(payload).AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
    }

    private async Task<JsonElement> ReceiveAsync(CancellationToken token, params string[] expected)
    {
        try
        {
            while (true)
            {
                var line = await process!.StandardOutput.ReadLineAsync(token);
                if (line == null)
                    throw new BridgeException("消息 Worker 已退出。");
                if (line.Length > 512_000)
                    throw new BridgeException("消息 Worker 返回了过大的数据。");
                var packet = JsonSerializer.Deserialize<JsonElement>(line);
                if (packet.GetProperty("session").GetGuid() != session ||
                    packet.GetProperty("generation").GetInt64() != Generation)
                    continue;
                var kind = packet.GetProperty("type").GetString();
                if (kind == "error")
                    throw new BridgeException(packet.GetProperty("code").GetString() switch
                    {
                        "identity" => "无法确认微信账号或联系人。",
                        "dependency" => "缺少消息读取依赖，请重新发布安装。",
                        "disk" => "磁盘空间不足，无法复制微信数据库。请清理磁盘后重试。",
                        "unsupported_version" => "不支持此版本。请确认微信 Windows 版在支持范围内（4.1.11 至 4.2.0.13）。",
                        _ => "读取微信数据失败。"
                    });
                if (!expected.Contains(kind))
                    throw new BridgeException("消息 Worker 返回了意外的数据。");
                return packet;
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new BridgeException("消息 Worker 返回了无效的数据。");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        if (stderrDrain != null)
        {
            try { await stderrDrain.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (Exception error) when (error is IOException or ObjectDisposedException or TimeoutException) { }
        }
    }
}
