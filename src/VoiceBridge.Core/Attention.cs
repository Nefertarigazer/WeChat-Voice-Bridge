namespace VoiceBridge.Core;

/// <summary>What privacy mode says instead of a message. Stored as a number: append only.</summary>
public enum PrivacyStyle { WeChat = 0, Name = 1, Secret = 2 }

/// <summary>What a reminder says. Stored as a number: append only.</summary>
public enum ReminderWording { Content = 0, WeChat = 1, Name = 2 }

/// <summary>Whether a queued item is a chat message or a reminder composed by the app.</summary>
public enum MessageKind { Chat = 0, Reminder = 1 }

/// <summary>Per contact: announce who wrote (or a code phrase) instead of reading the message.</summary>
public sealed record PrivacySettings
{
    public bool Enabled { get; init; }
    public PrivacyStyle Style { get; init; } = PrivacyStyle.WeChat;
    /// <summary>The phrase for <see cref="PrivacyStyle.Secret"/>, for example 天王盖地虎.</summary>
    public string Secret { get; init; } = "";
}

/// <summary>Per contact: remind again while a message waits for a reply.</summary>
public sealed record ReminderSettings
{
    public const int ShortestMinutes = 1;
    public const int LongestMinutes = 180;
    /// <summary>Continuous reminders give the user this long to answer first.</summary>
    public static readonly TimeSpan ContinuousStart = TimeSpan.FromMinutes(1);
    /// <summary>Continuous reminders pause this long between rounds.</summary>
    public static readonly TimeSpan ContinuousGap = TimeSpan.FromSeconds(5);
    public const string DefaultSound = "Alarm01";

    public bool Enabled { get; init; }
    public int IntervalMinutes { get; init; } = 3;
    public int DurationMinutes { get; init; } = 20;
    /// <summary>Remind every few seconds until the user replies or confirms, ignoring the two times.</summary>
    public bool Continuous { get; init; }
    /// <summary>A Windows sound name such as Alarm01, a .wav/.mp3 path, or empty for no sound.</summary>
    public string Sound { get; init; } = DefaultSound;
    /// <summary>Null follows the privacy style (see <see cref="AttentionText.WordingFor"/>).</summary>
    public ReminderWording? Wording { get; init; }

    public int Duration => Math.Clamp(DurationMinutes, ShortestMinutes, LongestMinutes);
    public int Interval => Math.Min(Math.Clamp(IntervalMinutes, ShortestMinutes, LongestMinutes), Duration);
}

/// <summary>The sentences privacy mode and reminders speak.</summary>
public static class AttentionText
{
    /// <summary>Privacy never reads a message, so it never offers <see cref="ReminderWording.Content"/>.</summary>
    public static IReadOnlyList<ReminderWording> WordingChoices(PrivacySettings privacy) => privacy.Enabled
        ? [ReminderWording.WeChat, ReminderWording.Name]
        : [ReminderWording.Content, ReminderWording.WeChat, ReminderWording.Name];

    public static ReminderWording DefaultWording(PrivacySettings privacy) =>
        !privacy.Enabled ? ReminderWording.Content :
        privacy.Style == PrivacyStyle.Name ? ReminderWording.Name : ReminderWording.WeChat;

    public static ReminderWording WordingFor(PrivacySettings privacy, ReminderSettings reminder)
    {
        var chosen = reminder.Wording ?? DefaultWording(privacy);
        return WordingChoices(privacy).Contains(chosen) ? chosen : DefaultWording(privacy);
    }

    /// <summary>What privacy mode says for <paramref name="count"/> messages read as one.</summary>
    public static string Privacy(PrivacySettings privacy, string name, int count = 1)
    {
        if (privacy.Style == PrivacyStyle.Secret && privacy.Secret.Trim() is { Length: > 0 } secret)
            return secret;
        if (privacy.Style == PrivacyStyle.Name)
            return count > 1 ? $"{Called(name)}给你发了{count}条消息" : $"{Called(name)}给你发消息了";
        return count > 1 ? $"微信来了{count}条消息" : "微信来消息了";
    }

    /// <param name="since">Time since the newest waiting message.</param>
    /// <param name="withPrompt">Adds the contact's own prompt (提示词) to the re-read messages.</param>
    public static string Reminder(ReminderWording wording, string name, TimeSpan since, int count,
        IReadOnlyList<string> texts, Func<string, string>? withPrompt = null)
    {
        var elapsed = Elapsed(since);
        var body = JoinSentences(texts);
        return wording switch
        {
            ReminderWording.Content when body.Length > 0 =>
                $"来自{elapsed}前的消息，" + (withPrompt is null ? body : withPrompt(body)),
            ReminderWording.WeChat => $"微信在{elapsed}前来了{Count(count)}消息",
            _ => $"{Called(name)}在{elapsed}前给您发了{Count(count)}消息"
        };
    }

    /// <summary>Whole minutes, at least one: 3分钟, 1小时, 1小时20分钟.</summary>
    public static string Elapsed(TimeSpan since)
    {
        var minutes = Math.Max(1, (long)Math.Floor(since.TotalMinutes));
        if (minutes < 60) return $"{minutes}分钟";
        var (hours, rest) = Math.DivRem(minutes, 60);
        return rest == 0 ? $"{hours}小时" : $"{hours}小时{rest}分钟";
    }

    private static string Count(int count) => count <= 1 ? "一条" : $"{count}条";
    private static string Called(string name) => string.IsNullOrWhiteSpace(name) ? "联系人" : name.Trim();

    private static string JoinSentences(IEnumerable<string> texts)
    {
        var joined = new System.Text.StringBuilder();
        foreach (var text in texts.Select(text => text.Trim()).Where(text => text.Length > 0))
        {
            if (joined.Length > 0 && !"。！？!?；;…~～".Contains(joined[^1])) joined.Append('；');
            joined.Append(text);
        }
        return joined.ToString();
    }
}
