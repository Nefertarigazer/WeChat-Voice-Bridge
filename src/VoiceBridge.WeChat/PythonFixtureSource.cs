using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VoiceBridge.Core;

namespace VoiceBridge.WeChat;

public enum WatchMode { EventDriven, PollingOnly, Benchmark }

public sealed record WatchOptions(WatchMode Mode, int PollMs, int CoalesceMs, int[] RetryMs)
{
    public static WatchOptions Default { get; } = new(WatchMode.EventDriven, 1000, 30, [50, 100, 200]);
}

public sealed class PythonFixtureSource(WatchOptions options, string python, string script, WeChatSession? wechat = null,
    IReadOnlyList<ReadingRule>? rules = null, Func<IReadOnlyList<ReadingRule>>? currentReadings = null,
    IReadOnlyList<string>? conversations = null) : IMessageSource
{
    public static Target DemoTarget { get; } = new("demo-local", "demo-peer");
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly System.Text.RegularExpressions.Regex WeChatMessageFile =
        new(@"\Amessage_[0-9]{1,3}\.db(?:-wal)?\z");
    private readonly Channel<byte> wake = Channel.CreateBounded<byte>(1);
    private sealed record SpeechRequest(SpeechParts Speech, IReadOnlyList<ReadingRule> Rules,
        CancellationToken Cancellation, TaskCompletionSource<string> Result);
    private readonly Channel<SpeechRequest> speechRequests = Channel.CreateUnbounded<SpeechRequest>(
        new() { SingleReader = true });
    // Only the worker loop accesses this value after initialization.
    private IReadOnlyList<ReadingRule>? appliedReadings;
    private int demos;
    private int overflow;
    private long fileEvent;
    private SessionCache? cache;
    private WorkerJob? job;
    private Process? process;
    private bool attached;
    private FileSystemWatcher? watcher;
    private Task? stderrDrain;
    private SessionStamp stamp;
    private string[] peers = [];
    private bool stopped;
    private long nextProcessCheck;

    /// <summary>
    /// Querying WeChat's process on every poll (about once a second) keeps an open
    /// handle on it for the whole session. WeChat checks its own process during
    /// voice/video call setup, so only re-validate at most every 10 seconds.
    /// </summary>
    private void CheckWeChatOccasionally()
    {
        if (wechat is null) return;
        var now = Stopwatch.GetTimestamp();
        if (now < Volatile.Read(ref nextProcessCheck)) return;
        Volatile.Write(ref nextProcessCheck, now + Stopwatch.Frequency * 10);
        wechat.CheckRunning();
    }

    public event Action<string>? Warning;
    public event Action<IReadOnlyList<ContactEntry>>? ContactsReady;
    internal int? WorkerId => process?.Id;
    internal string? FixtureDirectory => cache?.PathName;
    internal bool Watching => watcher != null;

    public static string ResolvePython() =>
        Environment.GetEnvironmentVariable("WVB_PYTHON") ??
        Path.Combine(AppContext.BaseDirectory, "runtime", "python", "python.exe");

    public void RequestDemo()
    {
        if (Interlocked.Increment(ref demos) > 30)
        {
            Interlocked.Decrement(ref demos);
            return;
        }
        wake.Writer.TryWrite(0);
    }

    public async Task PrepareAsync(SessionStamp stamp, Target target, CancellationToken token)
    {
        this.stamp = stamp;
        peers = conversations?.ToArray() ?? [target.ConversationId];
        if (peers.Length is < 1 or > 16 || peers[0] != target.ConversationId ||
            peers.Distinct(StringComparer.Ordinal).Count() != peers.Length ||
            peers.Any(peer => string.IsNullOrWhiteSpace(peer) || peer.Length > 256 ||
                peer.EndsWith("@chatroom", StringComparison.Ordinal) || peer == "filehelper"))
            throw new BridgeException("请选择 1 至 16 个不同的一对一联系人。");
        if (wechat == null && (target.AccountId != DemoTarget.AccountId ||
            peers.Any(peer => !peer.StartsWith("demo-", StringComparison.Ordinal))))
            throw new BridgeException("模拟会话身份无效。");
        if (wechat != null && target.AccountId != wechat.Account)
            throw new BridgeException("Worker 无法确认所选微信账号。");
        if (options.PollMs is < 100 or > 10_000 || options.CoalesceMs is < 10 or > 200 ||
            options.RetryMs.Length > 5 || options.RetryMs.Any(ms => ms is < 10 or > 1000))
            throw new BridgeException("监听参数超出支持范围。");
        if (!Path.IsPathFullyQualified(python) || !File.Exists(python) || !File.Exists(script))
            throw new BridgeException("缺少 Python 3.12 Worker。请运行发布版或按配置说明创建虚拟环境。");

        cache = new(stamp.Id, wechat?.Root);
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
        process = Process.Start(info) ?? throw new BridgeException("无法启动 Python Worker。");
        job.Attach(process);
        attached = true;
        stderrDrain = WorkerLog.DrainAsync(process.StandardError);

        var mode = wechat == null ? "fixture" : "wechat";
        appliedReadings = currentReadings?.Invoke() ?? rules;
        using var deadline = Deadline(token, TimeSpan.FromSeconds(wechat == null ? 8 : 45));
        try
        {
            await SendAsync(new { type = "start", session = stamp.Id, generation = stamp.Generation,
                account = target.AccountId, conversation = target.ConversationId, mode,
                conversations = peers,
                root = wechat?.Root, pids = wechat?.Pids,
                includeSpeech = currentReadings != null,
                rules = WireReadings(appliedReadings) }, deadline.Token);
            var capabilities = await ReceiveAsync(deadline.Token, "capabilities");
            var shards = capabilities.GetProperty("shards").EnumerateArray().Select(item => item.GetString()).ToArray();
            if (capabilities.GetProperty("mode").GetString() != mode ||
                (wechat == null ? !shards.SequenceEqual(SessionCache.Shards) :
                    shards.Length is < 1 or > 20 || shards.Any(name => name == null ||
                        name.EndsWith("-wal", StringComparison.Ordinal) || !WeChatMessageFile.IsMatch(name))))
                throw Protocol();
            var ready = await ReceiveAsync(deadline.Token, "ready");
            if (ready.GetProperty("account").GetString() != target.AccountId ||
                ready.GetProperty("conversation").GetString() != target.ConversationId)
                throw new BridgeException("Worker 无法确认目标账号或会话。");
            if (ready.TryGetProperty("conversations", out var confirmed))
            {
                if (confirmed.ValueKind != JsonValueKind.Array ||
                    !confirmed.EnumerateArray().Select(item => item.GetString()).SequenceEqual(peers))
                    throw new BridgeException("Worker 无法确认全部获准联系人。");
            }
            else if (peers.Length != 1)
                throw new BridgeException("消息 Worker 不支持多人监听，请重新安装完整版本。");
            if (ready.TryGetProperty("contacts", out var portraits) && portraits.ValueKind == JsonValueKind.Array)
            {
                var contacts = new List<ContactEntry>();
                foreach (var entry in portraits.EnumerateArray().Take(8))
                {
                    var id = entry.GetProperty("id").GetString();
                    if (id is null || !peers.Contains(id, StringComparer.Ordinal)) continue;
                    var name = entry.GetProperty("name").GetString() ?? id;
                    var alias = entry.GetProperty("wechat").GetString() ?? id;
                    var avatar = entry.TryGetProperty("avatarUrl", out var url) ? WeChatDirectory.SafeAvatar(url.GetString()) : null;
                    if (name.Length <= 512 && alias.Length <= 256) contacts.Add(new(id, name, alias, avatar));
                }
                if (contacts.Count > 0) ContactsReady?.Invoke(contacts);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new BridgeException("Python Worker 初始化超时。");
        }
        if (options.Mode != WatchMode.PollingOnly)
        {
            watcher = new(wechat?.DirectoryPath ?? cache.PathName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            if (wechat != null)
            {
                watcher.Filters.Add("message_*.db");
                watcher.Filters.Add("message_*.db-wal");
            }
            else foreach (var shard in SessionCache.Shards)
            {
                watcher.Filters.Add(shard);
                watcher.Filters.Add(shard + "-wal");
            }
            watcher.Changed += (_, args) => SignalFile(args.Name);
            watcher.Created += (_, args) => SignalFile(args.Name);
            watcher.Deleted += (_, args) => SignalFile(args.Name);
            watcher.Renamed += (_, args) => { SignalFile(args.OldName); SignalFile(args.Name); };
            watcher.Error += (_, _) => SignalOverflow();
            watcher.EnableRaisingEvents = true;
        }
    }

    public async Task RunAsync(Func<ChatMessage, Admission> admit, CancellationToken token)
    {
        var nextPoll = Stopwatch.GetTimestamp(); // Covers the baseline-to-RUNNING transition.
        var consecutiveBusy = 0;
        while (!token.IsCancellationRequested)
        {
            while (wake.Reader.TryRead(out _)) { }
            if (process!.HasExited)
                throw new BridgeException("消息 Worker 已退出。");
            await RenderQueuedSpeechAsync(token);
            var pendingDemos = Interlocked.Exchange(ref demos, 0);
            for (var i = 0; i < pendingDemos; i++)
                await ExchangeAsync("demo", token, "injected");
            var notice = Interlocked.Exchange(ref fileEvent, 0);
            var recoveredOverflow = Interlocked.Exchange(ref overflow, 0) != 0;
            if (recoveredOverflow)
                Warning?.Invoke("文件通知异常，使用增量查询与轮询兜底。");

            if (notice != 0 || recoveredOverflow || Stopwatch.GetTimestamp() >= nextPoll)
            {
                if (notice != 0)
                {
                    await Task.Delay(options.CoalesceMs, token);
                    Interlocked.Exchange(ref fileEvent, 0);
                }
                var retry = 0;
                int count;
                do
                {
                    await RenderQueuedSpeechAsync(token);
                    count = await QueryAsync(admit, notice, token);
                    consecutiveBusy = count < 0 ? consecutiveBusy + 1 : 0;
                    if (consecutiveBusy >= 8)
                        throw new BridgeException("数据库持续繁忙，监听已停止。");
                    if (count > 0 || notice == 0 || retry == options.RetryMs.Length)
                        break;
                    await Task.Delay(options.RetryMs[retry++], token);
                } while (true);

                // A full page can leave unread rows. Yield between pages while preserving single flight.
                nextPoll = count >= 32
                    ? Stopwatch.GetTimestamp()
                    : Stopwatch.GetTimestamp() + Stopwatch.Frequency * options.PollMs / 1000;
                continue;
            }

            using var delay = Deadline(token, Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), nextPoll));
            try { await wake.Reader.WaitToReadAsync(delay.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }
    }

    public async Task<string> RenderSpeechAsync(ChatMessage message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (currentReadings is null || message.Speech is not { } speech)
            return message.Text;
        var snapshot = currentReadings();
        if (ReferenceEquals(speech.PreviewRules, snapshot))
            return message.Text;

        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!speechRequests.Writer.TryWrite(new(speech, snapshot, token, result)))
            throw new BridgeException("消息 Worker 已停止。");
        wake.Writer.TryWrite(0);
        return await result.Task.WaitAsync(token);
    }

    private async Task RenderQueuedSpeechAsync(CancellationToken token)
    {
        while (speechRequests.Reader.TryRead(out var request))
        {
            if (request.Cancellation.IsCancellationRequested)
                continue;
            using var deadline = Deadline(token, TimeSpan.FromSeconds(3));
            try
            {
                // This loop owns both ends of IPC. Even if playback is skipped,
                // drain the response before starting the next database query.
                if (!ReferenceEquals(appliedReadings, request.Rules))
                {
                    await SendAsync(new { type = "readings", session = stamp.Id, generation = stamp.Generation,
                        rules = WireReadings(request.Rules) }, deadline.Token);
                    await ReceiveAsync(deadline.Token, "readings");
                    appliedReadings = request.Rules;
                }
                if (request.Cancellation.IsCancellationRequested)
                    continue;
                await SendAsync(new { type = "speak", session = stamp.Id, generation = stamp.Generation,
                    parts = request.Speech.Parts, announcement = request.Speech.Announcement }, deadline.Token);
                var packet = await ReceiveAsync(deadline.Token, "spoken");
                request.Result.TrySetResult(Required(packet, "text"));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new BridgeException("词库朗读转换超时，监听已停止。");
            }
        }
    }

    private static object? WireReadings(IEnumerable<ReadingRule>? snapshot) =>
        snapshot?.Select(rule => new { match = rule.Match, say = rule.Say,
            anywhere = rule.Anywhere, scope = rule.Scope }).ToArray();

    public static bool CanSendReadings(IEnumerable<ReadingRule> snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(WireReadings(snapshot)).Length <= 60_000;

    private async Task<int> QueryAsync(Func<ChatMessage, Admission> admit, long notice, CancellationToken token)
    {
        CheckWeChatOccasionally();
        var requested = Stopwatch.GetTimestamp();
        var packet = await ExchangeAsync("query", token, "message", "heartbeat", "warning");
        var kind = packet.GetProperty("type").GetString();
        if (kind == "heartbeat")
            return 0;
        if (kind == "warning")
        {
            Warning?.Invoke("数据库暂时繁忙，等待重试。");
            return -1;
        }
        var keys = new List<string>();
        var queryMs = packet.GetProperty("queryMs").GetDouble();
        foreach (var row in packet.GetProperty("messages").EnumerateArray())
        {
            var direction = row.GetProperty("direction").GetString() switch
            {
                "incoming" => Direction.Incoming, "outgoing" => Direction.Outgoing,
                "system" => Direction.System, _ => Direction.Unknown
            };
            var detection = row.GetProperty("fixtureDetectionMs");
            SpeechParts? speech = null;
            if (currentReadings != null && row.TryGetProperty("speech", out var raw))
                speech = new(raw.GetProperty("parts").EnumerateArray().Select(item => item.GetString() ?? "").ToArray(),
                    raw.GetProperty("announcement").GetBoolean(), appliedReadings);
            var message = new ChatMessage(Required(row, "key"), Required(row, "account"), Required(row, "conversation"),
                direction, row.GetProperty("isText").GetBoolean(), Required(row, "text"),
                row.GetProperty("sequence").GetInt64(), detection.ValueKind == JsonValueKind.Number ? detection.GetDouble() : null,
                queryMs, notice != 0 ? notice : null, requested, speech);
            if (admit(message) == Admission.Closed)
                return 0;
            keys.Add(message.Key);
        }
        using var deadline = Deadline(token, TimeSpan.FromSeconds(3));
        try
        {
            await SendAsync(new { type = "ack", session = stamp.Id, generation = stamp.Generation, keys }, deadline.Token);
            await ReceiveAsync(deadline.Token, "ack");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new BridgeException("消息确认超时，监听已停止。");
        }
        return keys.Count;
    }

    private void SignalFile(string? name)
    {
        if (wechat == null ? !SessionCache.Shards.Any(shard => name == shard || name == shard + "-wal") :
            name == null || !WeChatMessageFile.IsMatch(name))
            return;
        Interlocked.CompareExchange(ref fileEvent, Stopwatch.GetTimestamp(), 0);
        wake.Writer.TryWrite(0);
    }

    internal void SignalOverflow()
    {
        Interlocked.Exchange(ref overflow, 1);
        wake.Writer.TryWrite(0);
    }

    private async Task<JsonElement> ExchangeAsync(string type, CancellationToken token, params string[] responseTypes)
    {
        using var deadline = Deadline(token, TimeSpan.FromSeconds(type == "query" && peers.Length > 1 ? 8 : 3));
        try
        {
            await SendAsync(new { type, session = stamp.Id, generation = stamp.Generation }, deadline.Token);
            return await ReceiveAsync(deadline.Token, responseTypes);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new BridgeException("消息 Worker 无响应，监听已停止。");
        }
    }

    private async Task SendAsync<T>(T payload, CancellationToken token)
    {
        var frame = JsonSerializer.Serialize(payload);
        if (Utf8.GetByteCount(frame) > 65_534)
            throw new BridgeException("词库或消息超过 Worker 通信大小上限。");
        await process!.StandardInput.WriteLineAsync(frame.AsMemory(), token);
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
                    throw Protocol();
                var packet = JsonSerializer.Deserialize<JsonElement>(line);
                if (packet.GetProperty("session").GetGuid() != stamp.Id ||
                    packet.GetProperty("generation").GetInt64() != stamp.Generation)
                    continue;
                var kind = Required(packet, "type");
                if (kind == "error")
                    throw new BridgeException(Required(packet, "code") switch
                    {
                        "identity" => "Worker 无法确认目标账号或会话。",
                        "runtime" => "消息 Worker 需要 Python 3.12 x64。",
                        "disk" => "磁盘空间不足，无法复制微信数据库。请清理磁盘后重试。",
                        _ => "消息数据库查询失败。"
                    });
                if (!expected.Contains(kind))
                    throw Protocol();
                return packet;
            }
        }
        catch (Exception error) when (error is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or DecoderFallbackException)
        {
            throw Protocol();
        }
    }

    private static string Required(JsonElement data, string property) => data.GetProperty(property).GetString() ?? throw Protocol();
    private static BridgeException Protocol() => new("Python Worker 输出了无效的协议数据。");
    private static CancellationTokenSource Deadline(CancellationToken token, TimeSpan delay)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        source.CancelAfter(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
        return source;
    }

    public async ValueTask DisposeAsync()
    {
        if (stopped)
            return;
        stopped = true;
        wake.Writer.TryComplete();
        speechRequests.Writer.TryComplete();
        watcher?.Dispose();
        watcher = null;
        try
        {
            if (process is { } child && !child.HasExited)
            {
                try
                {
                    using var grace = new CancellationTokenSource(TimeSpan.FromMilliseconds(650));
                    if (attached)
                    {
                        await SendAsync(new { type = "stop", session = stamp.Id, generation = stamp.Generation }, grace.Token);
                        child.StandardInput.Close();
                        // Read until exit to let a final response drain, including a canceled query.
                        while (await child.StandardOutput.ReadLineAsync(grace.Token) != null) { }
                        await child.WaitForExitAsync(grace.Token);
                    }
                }
                catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException) { }
                finally
                {
                    if (attached)
                        job?.Dispose();
                    else if (!child.HasExited)
                        child.Kill(entireProcessTree: true);
                }
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        finally
        {
            job?.Dispose();
            process?.Dispose();
            if (stderrDrain != null)
            {
                try { await stderrDrain.WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (Exception error) when (error is IOException or ObjectDisposedException or TimeoutException) { }
            }
            cache?.Dispose();
        }
    }
}
