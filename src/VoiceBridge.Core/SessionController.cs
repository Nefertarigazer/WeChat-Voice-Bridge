using System.Diagnostics;
using System.Threading.Channels;

namespace VoiceBridge.Core;

public sealed class SessionController : IAsyncDisposable
{
    private const int MaxTextLength = SpeechText.MaximumLength;
    private readonly object gate = new();
    private Session? current;
    private long generation;
    private bool disposed;
    private BridgeStatus status = new(BridgeState.Idle, 0, "等待开始", null);

    public BridgeStatus Status { get { lock (gate) return status; } }
    public SessionStamp? ActiveStamp { get { lock (gate) return current?.Stamp; } }

    // Notifications run under the state gate. Subscribers should post, never wait on a dispatcher.
    public event Action<BridgeStatus>? Changed;
    public event Action<ProcessingEvent>? Processed;

    public bool Start(Target target, bool consent, int capacity, bool preview,
        Func<IMessageSource> sourceFactory, ISpeech speech, IPlayer player,
        Func<ChatMessage, CancellationToken, Task<string>>? prepareSpeech = null,
        Func<ChatMessage, bool>? admitFilter = null) =>
        Start(new[] { target }, consent, capacity, preview, sourceFactory, speech, player, prepareSpeech, admitFilter);

    public bool Start(IReadOnlyList<Target> targets, bool consent, int capacity, bool preview,
        Func<IMessageSource> sourceFactory, ISpeech speech, IPlayer player,
        Func<ChatMessage, CancellationToken, Task<string>>? prepareSpeech = null,
        Func<ChatMessage, bool>? admitFilter = null)
    {
        lock (gate)
        {
            if (disposed || status.State != BridgeState.Idle)
                return false;
            var selected = targets.ToArray();
            if (!consent || selected.Length is < 1 or > 8 || capacity is < 1 or > 100 ||
                selected.Any(target => string.IsNullOrWhiteSpace(target.AccountId) ||
                    string.IsNullOrWhiteSpace(target.ConversationId) || target.AccountId.Contains('\0') ||
                    target.ConversationId.Contains('\0') || target.ConversationId.EndsWith("@chatroom", StringComparison.Ordinal)) ||
                selected.Select(target => target.AccountId).Distinct(StringComparer.Ordinal).Count() != 1 ||
                selected.Select(target => target.ConversationId).Distinct(StringComparer.Ordinal).Count() != selected.Length)
                throw new BridgeException("请选择同一账号下的 1–8 个不同的一对一联系人，并确认授权与队列容量。");

            var session = new Session(new(Guid.NewGuid(), ++generation), selected, capacity, preview, speech, player,
                prepareSpeech, admitFilter);
            current = session;
            Set(BridgeState.Starting, 0, "正在建立消息基线", null);
            session.Completion = Task.Run(() => SuperviseAsync(session, sourceFactory));
            return true;
        }
    }

    public Task EndAsync()
    {
        lock (gate)
        {
            if (current is not { } session)
                return Task.CompletedTask;
            BeginStop(session);
            return session.Completion;
        }
    }

    public bool Reset()
    {
        lock (gate)
        {
            if (status.State != BridgeState.Error || current != null)
                return false;
            Set(BridgeState.Idle, 0, "等待开始", null);
            return true;
        }
    }

    public void SetPreview(bool preview)
    {
        lock (gate)
            if (current is { } session) session.Preview = preview;
    }

    public void SkipCurrent()
    {
        lock (gate)
        {
            if (status.State != BridgeState.Running || current is not { Utterance: { } utterance } ||
                utterance.IsCancellationRequested)
                return;
            current.Skipped.Add(current.CurrentKey ?? "");
            if (current.Prefetched is { } pending && pending.Key == current.CurrentKey) _ = pending.Stop.CancelAsync();
            current.UtteranceCancellation = utterance.CancelAsync();
        }
    }

    public void ClearQueue()
    {
        lock (gate)
        {
            if (status.State != BridgeState.Running || current is not { } session)
                return;
            ++session.QueueEpoch;
            session.Chunks.Clear();
            if (session.Prefetched is { } pending) _ = pending.Stop.CancelAsync();
            while (session.Queue.Reader.TryRead(out _)) { }
            Set(status.State, 0, status.Activity, status.Error);
            Processed?.Invoke(new("已清空待播队列"));
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate) disposed = true;
        await EndAsync();
    }

    private async Task SuperviseAsync(Session session, Func<IMessageSource> sourceFactory)
    {
        IMessageSource? source = null;
        Task producer = Task.CompletedTask;
        Task consumer = Task.CompletedTask;
        string? error = null;
        try
        {
            session.Token.ThrowIfCancellationRequested();
            source = sourceFactory();
            await source.PrepareAsync(session.Stamp, session.Targets[0], session.Token);
            lock (gate)
            {
                session.Token.ThrowIfCancellationRequested();
                if (status.State != BridgeState.Starting || session.Stamp.Generation != generation)
                    return;
                consumer = Task.Run(() => ConsumeAsync(session));
                Set(BridgeState.Running, 0, "等待新消息", null);
            }
            producer = source.RunAsync(message => Admit(session, message), session.Token);
            await await Task.WhenAny(producer, consumer);
            if (!session.Token.IsCancellationRequested)
                error = "消息读取或语音处理提前退出。";
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested) { }
        catch (BridgeException exception) { if (!session.Token.IsCancellationRequested) error = exception.Message; }
        catch (Exception) { if (!session.Token.IsCancellationRequested) error = "会话遇到异常，已停止。"; }
        finally
        {
            lock (gate) BeginStop(session);
            try { await producer; }
            catch (Exception) { /* The initiating failure is handled above. */ }
            if (source != null)
            {
                try { await source.DisposeAsync(); }
                catch (Exception) { error ??= "消息 Worker 或临时文件清理失败。"; }
            }
            try { await consumer; }
            catch (Exception) { /* The initiating failure is handled above. */ }
            try { await Task.WhenAll(session.Cancellation, session.AudioStop); }
            catch (Exception) { error ??= "会话取消回调失败。"; }
            lock (gate)
            {
                session.Stop.Dispose();
                current = null;
                Set(error == null ? BridgeState.Idle : BridgeState.Error, 0,
                    error == null ? "等待开始" : "会话已停止", error);
            }
        }
    }

    private void BeginStop(Session session)
    {
        if (current != session || status.State == BridgeState.Stopping)
            return;
        ++generation;
        Set(BridgeState.Stopping, 0, "正在停止播放并释放 Worker", null);
        session.Queue.Writer.TryComplete();
        session.Cancellation = session.Stop.CancelAsync();
        session.AudioStop = Task.Run(session.Player.Stop);
        while (session.Queue.Reader.TryRead(out _)) { }
    }

    private Admission Admit(Session session, ChatMessage message)
    {
        var receivedAt = Stopwatch.GetTimestamp();
        lock (gate)
        {
            if (!IsRunning(session))
                return Admission.Closed;
            if (message.AccountId != session.Targets[0].AccountId)
                throw new BridgeException("检测到账号变化，监听已停止。", true);
            if (!session.Conversations.Contains(message.ConversationId) ||
                session.AdmitFilter is { } admit && !admit(message) ||
                message.Direction != Direction.Incoming || !message.IsText || string.IsNullOrWhiteSpace(message.Text))
                return Admission.Ignored;
            if (string.IsNullOrWhiteSpace(message.Key))
                throw new BridgeException("消息缺少可验证的标识，监听已停止。", true);
            if (!session.Seen.Add(message.AccountId + "\0" + message.ConversationId + "\0" + message.Key))
                return Admission.Duplicate;
            if (session.Seen.Count > 100_000)
                throw new BridgeException("本次会话消息数已达上限，请重新开始。", true);
            if (message.Text.Length > MaxTextLength || !session.Queue.Writer.TryWrite(message))
            {
                Processed?.Invoke(new(message.Text.Length > MaxTextLength ? "跳过超长消息" : "队列已满，跳过新消息"));
                return Admission.Dropped;
            }
            Set(status.State, status.Pending + 1, status.Activity, null);
            var preview = session.Preview ? string.Concat(message.Text.EnumerateRunes().Take(60)) : null;
            Processed?.Invoke(new("已接收", preview, message.FixtureDetectionMs, message.QueryMs,
                message.ConversationId));
            if (message.FileEventAt is { } eventAt)
                Processed?.Invoke(new("文件通知 → 接收", Milliseconds: Stopwatch.GetElapsedTime(eventAt, receivedAt).TotalMilliseconds));
            if (message.QueryRequestedAt is { } queryAt)
                Processed?.Invoke(new("查询请求 → 接收", Milliseconds: Stopwatch.GetElapsedTime(queryAt, receivedAt).TotalMilliseconds));
            return Admission.Accepted;
        }
    }

    private sealed record Chunk(ChatMessage Message, string Text, ISpeech Speech);
    private static string MessageIdentity(ChatMessage message) => message.AccountId + "\0" + message.ConversationId + "\0" + message.Key;
    private sealed class Work(ChatMessage message, long epoch, CancellationToken token)
    {
        public string Key => MessageIdentity(message);
        public ChatMessage Message => message;
        public long Epoch => epoch;
        public CancellationTokenSource Stop { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        public Task<AudioData> Audio { get; set; } = null!;
        public long SentAt { get; set; }
    }

    private async Task<Work?> NextAsync(Session session)
    {
        while (!session.Token.IsCancellationRequested)
        {
            Work? work = null;
            Chunk? chunk = null;
            lock (gate)
            {
                if (!IsRunning(session)) return null;
                while (session.Chunks.TryDequeue(out var candidate))
                    if (!session.Skipped.Contains(MessageIdentity(candidate.Message))) { chunk = candidate; break; }
                if (chunk != null) work = new(chunk.Message, session.QueueEpoch, session.Token);
                else if (session.Queue.Reader.TryRead(out var message))
                {
                    work = new(message, session.QueueEpoch, session.Token);
                    Set(status.State, Math.Max(0, status.Pending - 1), status.Activity, null);
                }
                if (work != null) session.Prefetched = work;
            }
            if (work != null)
            {
                work.Audio = BuildAudioAsync(session, work, chunk);
                return work;
            }
            if (!await session.Queue.Reader.WaitToReadAsync(session.Token)) return null;
        }
        return null;
    }

    private async Task<AudioData> BuildAudioAsync(Session session, Work work, Chunk? chunk)
    {
        var token = work.Stop.Token;
        var speech = chunk?.Speech ?? session.Speech;
        var text = chunk?.Text;
        if (text == null)
        {
            text = session.PrepareSpeech is null ? work.Message.Text : await session.PrepareSpeech(work.Message, token);
            token.ThrowIfCancellationRequested();
            if (speech is IMessageSpeechResolver resolver)
            {
                var resolved = resolver.Resolve(work.Message, text);
                speech = resolved.Speech; text = resolved.Text;
            }
            if (string.IsNullOrWhiteSpace(text)) throw new BridgeException("消息没有可朗读文字。");
            if (text.Length > MaxTextLength) throw new BridgeException("跳过超长消息（超过两万字）");
            var parts = SpeechText.Split(text).ToArray();
            lock (gate)
            {
                if (!IsRunning(session) || work.Epoch != session.QueueEpoch || session.Skipped.Contains(work.Key))
                    throw new OperationCanceledException(token);
                foreach (var part in parts.Skip(1)) session.Chunks.Enqueue(new(work.Message, part, speech));
            }
            text = parts[0];
            if (parts.Length > 1) Record(session, new($"长文分为 {parts.Length} 段朗读"));
        }
        token.ThrowIfCancellationRequested();
        work.SentAt = Stopwatch.GetTimestamp();
        void FirstAudio() => Record(session, new("收到首个有效音频帧", Milliseconds: Stopwatch.GetElapsedTime(work.SentAt).TotalMilliseconds));
        return speech is IMessageSpeech routed
            ? await routed.SynthesizeAsync(work.Message, text, FirstAudio, token)
            : await speech.SynthesizeAsync(text, FirstAudio, token);
    }

    private async Task ConsumeAsync(Session session)
    {
        Task<Work?>? next = null;
        try
        {
            while (true)
            {
                var work = await (next ?? NextAsync(session));
                next = null;
                if (work == null) return;
                var utterance = work.Stop;
                lock (gate)
                {
                    session.Utterance = utterance;
                    session.CurrentKey = work.Key;
                    if (ReferenceEquals(session.Prefetched, work)) session.Prefetched = null;
                    if (work.Epoch != session.QueueEpoch || session.Skipped.Contains(work.Key)) _ = utterance.CancelAsync();
                    if (IsRunning(session)) Set(status.State, status.Pending, "正在合成", null);
                }
                try
                {
                    var audio = await work.Audio;
                    utterance.Token.ThrowIfCancellationRequested();
                    // One look-ahead request overlaps playback. Audio remains bounded and playback is sequential.
                    next = NextAsync(session);
                    long started = 0;
                    await session.Player.PlayAsync(audio, action =>
                    {
                        lock (gate)
                        {
                            if (!IsRunning(session) || utterance.IsCancellationRequested) return false;
                            action(); started = Stopwatch.GetTimestamp();
                            Set(status.State, status.Pending, "正在播放", null);
                            Processed?.Invoke(new("已向音频设备提交播放", Milliseconds: Stopwatch.GetElapsedTime(work.SentAt, started).TotalMilliseconds));
                            return true;
                        }
                    }, utterance.Token);
                    if (started != 0 && !utterance.IsCancellationRequested)
                        Record(session, new("播放完成", Milliseconds: Stopwatch.GetElapsedTime(started).TotalMilliseconds));
                }
                catch (OperationCanceledException) when (utterance.IsCancellationRequested || work.Epoch != session.QueueEpoch) { }
                catch (BridgeException exception) when (!exception.Fatal) { Record(session, new(exception.Message)); }
                finally
                {
                    Task skipped;
                    lock (gate)
                    {
                        session.Utterance = null; session.CurrentKey = null;
                        skipped = session.UtteranceCancellation; session.UtteranceCancellation = Task.CompletedTask;
                        if (IsRunning(session)) Set(status.State, status.Pending, "等待新消息", null);
                    }
                    try { await skipped; } finally { utterance.Dispose(); }
                }
            }
        }
        finally
        {
            lock (gate) BeginStop(session);
            if (next != null)
            {
                var pending = await next;
                if (pending != null)
                {
                    await pending.Stop.CancelAsync();
                    try { await pending.Audio; } catch (Exception) { }
                    pending.Stop.Dispose();
                }
            }
        }
    }

    private bool IsRunning(Session session) =>
        status.State == BridgeState.Running && current == session && generation == session.Stamp.Generation;

    private void Record(Session session, ProcessingEvent result)
    {
        lock (gate)
            if (IsRunning(session))
                Processed?.Invoke(result);
    }

    private void Set(BridgeState state, int pending, string activity, string? error)
    {
        status = new(state, pending, activity, error);
        Changed?.Invoke(status);
    }

    private sealed class Session(SessionStamp stamp, Target[] targets, int capacity, bool preview,
        ISpeech speech, IPlayer player, Func<ChatMessage, CancellationToken, Task<string>>? prepareSpeech,
        Func<ChatMessage, bool>? admitFilter)
    {
        public SessionStamp Stamp { get; } = stamp;
        public Target[] Targets { get; } = targets;
        /// <summary>Re-checked per message so a peer removed while running is ignored without rebuilding the baseline.</summary>
        public Func<ChatMessage, bool>? AdmitFilter { get; } = admitFilter;
        public HashSet<string> Conversations { get; } = targets.Select(target => target.ConversationId).ToHashSet(StringComparer.Ordinal);
        public bool Preview { get; set; } = preview;
        public ISpeech Speech { get; } = speech;
        public Func<ChatMessage, CancellationToken, Task<string>>? PrepareSpeech { get; } = prepareSpeech;
        public IPlayer Player { get; } = player;
        public CancellationTokenSource Stop { get; } = new();
        public CancellationToken Token => Stop.Token;
        public Task Completion { get; set; } = Task.CompletedTask;
        public Task Cancellation { get; set; } = Task.CompletedTask;
        public Task AudioStop { get; set; } = Task.CompletedTask;
        public Channel<ChatMessage> Queue { get; } = Channel.CreateBounded<ChatMessage>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
        });
        public Queue<Chunk> Chunks { get; } = new();
        public HashSet<string> Skipped { get; } = new(StringComparer.Ordinal);
        public long QueueEpoch { get; set; }
        public Work? Prefetched { get; set; }
        public string? CurrentKey { get; set; }
        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource? Utterance { get; set; }
        public Task UtteranceCancellation { get; set; } = Task.CompletedTask;
    }
}
