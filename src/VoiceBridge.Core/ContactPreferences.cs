namespace VoiceBridge.Core;

public enum AnnouncementMode { Automatic, Off, Nickname, Custom, NicknameSuffix }
public enum AnnouncementPosition { Beginning, End }
public sealed record AnnouncementPreset(string Id, string Text, AnnouncementPosition Position);

/// <summary>Non-sensitive preferences are scoped to the stable account and peer IDs.</summary>
public sealed record ContactProfile
{
    public string AccountId { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Alias { get; init; } = "";
    public string VoiceProfileId { get; init; } = "";
    public string ModelOverride { get; init; } = "";
    public string FishLatencyOverride { get; init; } = "";
    public bool Selected { get; init; }
    public AnnouncementMode Announcement { get; init; }
    public string CustomAnnouncement { get; init; } = "";
    public AnnouncementPosition CustomPosition { get; init; }
    public string AnnouncementPresetId { get; init; } = "";
    public List<AnnouncementPreset> AnnouncementPresets { get; init; } = [];
    public string? AvatarUrl { get; init; }

    public SpeechProviderProfile ApplySpeechPreferences(SpeechProviderProfile provider) => provider with
    {
        Model = provider.Kind is SpeechProviderKind.Fish or SpeechProviderKind.OpenAiCompatible && ModelOverride.Length > 0
            ? ModelOverride : provider.Model,
        Latency = provider.Kind == SpeechProviderKind.Fish && FishLatencyOverride.Length > 0
            ? FishLatencyOverride : provider.Latency
    };

    public string Prefix(int people) => Announcement switch
    {
        AnnouncementMode.Off => "",
        AnnouncementMode.Automatic when people <= 1 => "",
        AnnouncementMode.Custom => CustomAnnouncement.Trim(),
        AnnouncementMode.NicknameSuffix => (string.IsNullOrWhiteSpace(Name) ? "联系人" : Name.Trim()) + "留言",
        _ => (string.IsNullOrWhiteSpace(Name) ? "联系人" : Name.Trim()) + "说"
    };

    /// <param name="renderPrefix">
    /// Optional word-book rendering of the announcement itself. The message body has
    /// already been rendered by the worker, but the prefix is composed here, after that.
    /// </param>
    public string Prepare(string text, int people, Func<string, string>? renderPrefix = null)
    {
        var prefix = Prefix(people);
        if (prefix.Length == 0) return text;
        if (renderPrefix is not null) prefix = renderPrefix(prefix);
        var phrase = prefix.TrimEnd('，', ',', '：', ':', '。', ' ');
        if (phrase.Length == 0) return text;
        return Announcement == AnnouncementMode.NicknameSuffix ||
            Announcement == AnnouncementMode.Custom && CustomPosition == AnnouncementPosition.End
            ? text + "，" + phrase : phrase + "，" + text;
    }
}

public sealed record ContactHistory(string AccountId, string ConversationId, string Name, string Alias,
    string? AvatarUrl, DateTimeOffset UsedAt);
public sealed record SearchHistory(string AccountId, string Query, DateTimeOffset UsedAt);

public static class RecentContacts
{
    public static ContactHistory[] Add(IEnumerable<ContactHistory> history, ContactHistory item) =>
        new[] { item }.Concat(history.Where(old => old.AccountId != item.AccountId ||
            old.ConversationId != item.ConversationId)).Take(12).ToArray();

    public static SearchHistory[] Add(IEnumerable<SearchHistory> history, SearchHistory item) =>
        new[] { item }.Concat(history.Where(old => old.AccountId != item.AccountId ||
            !string.Equals(old.Query, item.Query, StringComparison.OrdinalIgnoreCase))).Take(8).ToArray();
}
