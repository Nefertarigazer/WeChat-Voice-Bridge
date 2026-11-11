namespace VoiceBridge.Core;

public enum BridgeState { Idle, Starting, Running, Stopping, Error }
public enum Direction { Incoming, Outgoing, System, Unknown }
public enum Admission { Accepted, Ignored, Duplicate, Dropped, Closed }

public readonly record struct SessionStamp(Guid Id, long Generation);
public sealed record Target(string AccountId, string ConversationId);

/// <summary>How a symbol should be read.</summary>
public sealed record ReadingRule(string Match, string Say, bool Anywhere)
{
    /// <summary>
    /// Where the symbol may be read: "standalone" (it stands on its own),
    /// "inside" (it stands inside a sentence) or "both". An empty value comes
    /// from an older build, where <see cref="Anywhere"/> alone decided.
    /// </summary>
    public string Scope { get; init; } = "";
}

/// <summary>A word that should be read in another way.</summary>
public sealed record WordEntry(string Word, string Say);

public sealed record SpeechParts(string[] Parts, bool Announcement, IReadOnlyList<ReadingRule>? PreviewRules);

public sealed record ChatMessage(
    string Key, string AccountId, string ConversationId, Direction Direction, bool IsText,
    string Text, long Sequence, double? FixtureDetectionMs = null, double? QueryMs = null,
    long? FileEventAt = null, long? QueryRequestedAt = null, SpeechParts? Speech = null);
public delegate Task PcmFeed(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consume, CancellationToken token);
public sealed record AudioData(byte[] Bytes, bool IsWave = false, PcmFeed? Pcm = null)
{
    public const int PcmSampleRate = 24000;
}
public sealed record BridgeStatus(BridgeState State, int Pending, string Activity, string? Error);
/// <summary>The user sent something in a watched chat; <paramref name="Sequence"/> is its shared timestamp.</summary>
public sealed record ReplySignal(string ConversationId, long Sequence);
public sealed record ProcessingEvent(string Stage, string? Preview = null, double? Milliseconds = null,
    double? QueryMilliseconds = null, string? ConversationId = null);

public interface IMessageSource : IAsyncDisposable
{
    Task PrepareAsync(SessionStamp stamp, Target target, CancellationToken token);
    Task RunAsync(Func<ChatMessage, Admission> admit, CancellationToken token);
}

public interface ISpeech
{
    Task<AudioData> SynthesizeAsync(string text, Action firstAudio, CancellationToken token);
}

/// <summary>Chooses a voice from the verified sender while sharing one playback queue.</summary>
public interface IMessageSpeech : ISpeech
{
    Task<AudioData> SynthesizeAsync(ChatMessage message, string text, Action firstAudio, CancellationToken token);
}

public interface IPlayer
{
    Task PlayAsync(AudioData audio, Func<Action, bool> startIfCurrent, CancellationToken token);
    void Stop();
}

public sealed record ResolvedSpeech(ISpeech Speech, string Text);
public interface IMessageSpeechResolver
{
    ResolvedSpeech Resolve(ChatMessage message, string text);
}

// Only vetted, fixed messages cross the boundary into the GUI.
public class BridgeException(string message, bool fatal = false) : Exception(message)
{
    public bool Fatal { get; } = fatal;
}
