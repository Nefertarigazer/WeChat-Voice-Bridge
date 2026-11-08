using System.Collections.Concurrent;
using System.Threading.Channels;
using VoiceBridge.Core;
using VoiceBridge.WeChat;

namespace VoiceBridge.Tests;

internal sealed class ProbeSource : IMessageSource
{
    private readonly Channel<ChatMessage> inbox = Channel.CreateUnbounded<ChatMessage>();
    public TaskCompletionSource Entered { get; } = NewSignal();
    public TaskCompletionSource Ready { get; } = NewSignal();
    public TaskCompletionSource Stopped { get; } = NewSignal();
    public Func<ChatMessage, Admission>? LateDelivery { get; private set; }
    public SessionStamp Stamp { get; private set; }

    public async Task PrepareAsync(SessionStamp stamp, Target target, CancellationToken token)
    {
        Stamp = stamp;
        Entered.SetResult();
        await Ready.Task.WaitAsync(token);
    }

    public async Task RunAsync(Func<ChatMessage, Admission> admit, CancellationToken token)
    {
        LateDelivery = admit;
        await foreach (var message in inbox.Reader.ReadAllAsync(token))
            admit(message);
    }

    public void Send(ChatMessage message) => inbox.Writer.TryWrite(message);
    public ValueTask DisposeAsync() { Stopped.TrySetResult(); return ValueTask.CompletedTask; }
    public static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ProbeSpeech(Func<string, CancellationToken, Task<AudioData>>? synthesize = null) : ISpeech
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public async Task<AudioData> SynthesizeAsync(string text, Action firstAudio, CancellationToken token)
    {
        Calls.Enqueue(text);
        var audio = synthesize == null ? new AudioData([]) : await synthesize(text, token);
        firstAudio();
        return audio;
    }
}

internal sealed class ProbePlayer(bool block = false) : IPlayer
{
    public int Played;
    public int Stopped;
    public TaskCompletionSource Playing { get; } = ProbeSource.NewSignal();

    public async Task PlayAsync(AudioData audio, Func<Action, bool> startIfCurrent, CancellationToken token)
    {
        if (!startIfCurrent(() =>
        {
            Interlocked.Increment(ref Played);
            Playing.TrySetResult();
        }))
            return;
        if (block)
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }

    public void Stop() => Interlocked.Increment(ref Stopped);
}

internal static class TestSupport
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    public static Target DemoTarget => PythonFixtureSource.DemoTarget;

    public static ChatMessage Message(string id, string text = "哈哈哈") =>
        new(id, DemoTarget.AccountId, DemoTarget.ConversationId, Direction.Incoming, true, text, 10);

    public static async Task Until(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(Limit);
        while (!predicate())
            await Task.Delay(10, deadline.Token);
    }

    public static PythonFixtureSource Fixture(WatchOptions? options = null, string script = "worker/main.py") =>
        new(options ?? WatchOptions.Default, PythonFixtureSource.ResolvePython(), Path.Combine(AppContext.BaseDirectory, script));
}
