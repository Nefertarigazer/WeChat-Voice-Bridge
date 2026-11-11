namespace VoiceBridge.Core;

/// <param name="Since">For a scheduled reminder, the reached step (3, 6, 9… minutes); otherwise the real time.</param>
public sealed record DueReminder(string ConversationId, int Count, TimeSpan Since, IReadOnlyList<string> Texts,
    bool Continuous);

/// <summary>
/// Who is waiting for a reply, and when to remind. Rules agreed with the user:
/// <list type="bullet">
/// <item>One group per contact. Each new message adds one; only a reply, 已读 or the end of
/// listening clears it. A finished round does not.</item>
/// <item>A round starts at the newest message and reminds every interval until the duration
/// has passed. A new message starts a new round.</item>
/// <item>Continuous reminders start one minute after the newest message and repeat five seconds
/// after the previous one finished.</item>
/// </list>
/// Not thread-safe: use it from one thread (the UI thread).
/// </summary>
public sealed class ReplyTracker
{
    private const int KeptTexts = 100;
    /// <summary>A reminder that never reports back (queue cleared, worker stopped) frees its contact after this.</summary>
    private static readonly TimeSpan InFlightLimit = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, Waiting> waiting = new(StringComparer.Ordinal);

    /// <summary>The number of waiting messages of a contact changed.</summary>
    public event Action<string>? Changed;

    public int Count(string conversation) => waiting.TryGetValue(conversation, out var item) ? item.Messages.Count : 0;

    /// <param name="sequence">The message's shared timestamp, compared with the user's replies.</param>
    public void Received(string conversation, long sequence, string text, DateTimeOffset now)
    {
        var item = Get(conversation);
        if (sequence > 0 && sequence <= item.Replied)
            return;
        item.Messages.Add(new(sequence, text));
        if (item.Messages.Count > KeptTexts)
            item.Messages[^(KeptTexts + 1)] = item.Messages[^(KeptTexts + 1)] with { Text = null };
        item.Latest = now;
        item.Announced = 0;
        item.ReadyAt = now + ReminderSettings.ContinuousStart;
        item.InFlightSince = null;
        Changed?.Invoke(conversation);
    }

    /// <summary>The user wrote in this chat: everything older than the reply is answered.</summary>
    public void Replied(string conversation, long sequence)
    {
        var item = Get(conversation);
        item.Replied = Math.Max(item.Replied, sequence);
        if (item.Messages.RemoveAll(message => message.Sequence < sequence) == 0)
            return;
        if (item.Messages.Count == 0) item.Quiet();
        Changed?.Invoke(conversation);
    }

    public void MarkRead(string conversation)
    {
        if (!waiting.TryGetValue(conversation, out var item) || item.Messages.Count == 0)
            return;
        item.Replied = Math.Max(item.Replied, item.Messages.Max(message => message.Sequence));
        item.Messages.Clear();
        item.Quiet();
        Changed?.Invoke(conversation);
    }

    /// <summary>Listening ended: replies can no longer be seen, so nothing is waiting any more.</summary>
    public void Clear()
    {
        var cleared = waiting.Where(pair => pair.Value.Messages.Count > 0).Select(pair => pair.Key).ToArray();
        waiting.Clear();
        foreach (var conversation in cleared) Changed?.Invoke(conversation);
    }

    /// <summary>A continuous reminder finished playing; the next one may follow after the gap.</summary>
    public void Finished(string conversation, DateTimeOffset now)
    {
        if (!waiting.TryGetValue(conversation, out var item) || item.InFlightSince is null)
            return;
        item.InFlightSince = null;
        item.ReadyAt = now + ReminderSettings.ContinuousGap;
    }

    public IReadOnlyList<DueReminder> Due(DateTimeOffset now, Func<string, ReminderSettings?> settingsFor)
    {
        var due = new List<DueReminder>();
        foreach (var (conversation, item) in waiting)
        {
            if (item.Messages.Count == 0 || settingsFor(conversation) is not { Enabled: true } settings)
                continue;
            if (settings.Continuous)
            {
                if (item.InFlightSince is { } flight)
                {
                    if (now - flight < InFlightLimit) continue;
                    item.InFlightSince = null;
                    item.ReadyAt = now;
                }
                if (item.ReadyAt is not { } ready || now < ready)
                    continue;
                item.InFlightSince = now;
                item.ReadyAt = null;
                due.Add(new(conversation, item.Messages.Count, now - item.Latest, item.Texts(), true));
                continue;
            }
            var elapsed = (int)Math.Min(int.MaxValue, Math.Floor((now - item.Latest).TotalMinutes));
            var step = elapsed / settings.Interval * settings.Interval;
            if (step < settings.Interval || step > settings.Duration || step <= item.Announced)
                continue;
            item.Announced = step;
            due.Add(new(conversation, item.Messages.Count, TimeSpan.FromMinutes(step), item.Texts(), false));
        }
        return due;
    }

    private Waiting Get(string conversation)
    {
        if (!waiting.TryGetValue(conversation, out var item))
            waiting[conversation] = item = new();
        return item;
    }

    private sealed record Unanswered(long Sequence, string? Text);

    private sealed class Waiting
    {
        public List<Unanswered> Messages { get; } = [];
        public long Replied { get; set; }
        public DateTimeOffset Latest { get; set; }
        /// <summary>The last scheduled step already spoken in this round, in minutes.</summary>
        public int Announced { get; set; }
        public DateTimeOffset? ReadyAt { get; set; }
        public DateTimeOffset? InFlightSince { get; set; }

        public string[] Texts() => Messages.Where(message => message.Text is { Length: > 0 })
            .Select(message => message.Text!).ToArray();

        public void Quiet()
        {
            Announced = 0;
            ReadyAt = null;
            InFlightSince = null;
        }
    }
}
