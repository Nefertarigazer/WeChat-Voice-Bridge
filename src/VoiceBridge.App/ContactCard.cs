using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;

namespace VoiceBridge.App;

internal sealed record VoiceChoice(string Id, string Name, string ProviderName)
{
    public string Label => Name;
}
internal sealed class AnnouncementChoice(string id, string label) : INotifyPropertyChanged
{
    public string Id { get; } = id;
    public bool IsCustomPreset => Id is not ("none" or "say" or "leave" or "custom");
    public string Label { get; private set; } = label;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void UpdateLabel(string value)
    {
        if (Label == value) return;
        Label = value; PropertyChanged?.Invoke(this, new(nameof(Label)));
    }
}

internal sealed class ContactCard : INotifyPropertyChanged
{
    private readonly Func<ContactCard, string, bool> chooseVoice;
    private readonly Action<ContactCard> changed;
    private ImageSource? avatar;
    private int people = 1;
    private bool customRequestPending;
    private string speechSignature = "", speechProviderId = "", defaultModel = "", defaultLatency = "balanced", connectionName = "";
    private bool speechReady, fishOptions, showConnection;
    public ContactProfile Profile { get; private set; }
    public ListCollectionView Voices { get; }
    public ObservableCollection<AnnouncementChoice> Announcements { get; } = new();
    public ObservableCollection<ModelChoice> Models { get; } = new();
    public ModelChoice[] Latencies { get; } = [new("balanced", "均衡"), new("normal", "音质优先"), new("low", "低延迟")];
    public bool HasModels => Models.Count > 0;
    public bool HasSpeechOptions => HasModels || FishOptions;
    public bool InlineSpeechOptions => people > 1 && HasSpeechOptions;
    public bool IsMultiPerson => people > 1;
    public bool CanSwitchModels => Models.Count > 1;
    public string ModelLabel => Models.FirstOrDefault(item => item.Id == Model)?.Label ?? Model;
    public bool FishOptions => fishOptions;
    public bool ShowConnection => showConnection;
    public string ConnectionName => connectionName;
    public string Model
    {
        get => Profile.ModelOverride.Length > 0 ? Profile.ModelOverride : defaultModel;
        set
        {
            if (string.IsNullOrEmpty(value) || value == Model || !Models.Any(item => item.Id == value)) return;
            Profile = Profile with { ModelOverride = value }; Notify(); Notify(nameof(ModelLabel)); changed(this);
        }
    }
    public string FishLatency
    {
        get => Profile.FishLatencyOverride.Length > 0 ? Profile.FishLatencyOverride : defaultLatency;
        set
        {
            if (value == FishLatency || value is not ("normal" or "balanced" or "low")) return;
            Profile = Profile with { FishLatencyOverride = value }; Notify(); changed(this);
        }
    }

    public bool RefreshSpeechOptions(SpeechProviderProfile? provider, bool multipleConnections)
    {
        var before = Profile;
        if (speechReady && speechProviderId != provider?.Id)
            Profile = Profile with { ModelOverride = "", FishLatencyOverride = "" };
        speechReady = true; speechProviderId = provider?.Id ?? "";
        defaultModel = provider?.Model ?? ""; defaultLatency = provider?.Latency ?? "balanced";
        fishOptions = provider?.Kind == SpeechProviderKind.Fish;
        connectionName = provider?.Name ?? ""; showConnection = multipleConnections && provider != null;
        string[] models = [];
        if (provider != null)
        {
            if (fishOptions)
            {
                try { models = SpeechProviderFactory.FishModels(provider); }
                catch (BridgeException) { /* Invalid settings can still be repaired through the editor. */ }
            }
            else if (provider.Kind == SpeechProviderKind.OpenAiCompatible)
                models = new[] { provider.Model }.Concat(provider.AvailableModels ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        }
        if (Profile.ModelOverride.Length > 0 && !models.Contains(Profile.ModelOverride, StringComparer.Ordinal))
            Profile = Profile with { ModelOverride = "" };
        if (Profile.FishLatencyOverride.Length > 0 && (!fishOptions || Profile.FishLatencyOverride is not ("normal" or "balanced" or "low")))
            Profile = Profile with { FishLatencyOverride = "" };
        var signature = System.Text.Json.JsonSerializer.Serialize(models);
        if (signature != speechSignature)
        {
            speechSignature = signature;
            Models.Clear();
            foreach (var model in models) Models.Add(new(model, model switch
            { FishSpeech.Model => "S2.1 Pro · 免费", FishSpeech.PaidModel => "Drama 3 · 付费", _ => model }));
        }
        Notify(nameof(Model)); Notify(nameof(FishLatency)); Notify(nameof(HasModels));
        Notify(nameof(CanSwitchModels)); Notify(nameof(ModelLabel));
        Notify(nameof(FishOptions)); Notify(nameof(ConnectionName)); Notify(nameof(ShowConnection));
        Notify(nameof(HasSpeechOptions)); Notify(nameof(InlineSpeechOptions));
        return before != Profile;
    }
    public string Name => string.IsNullOrWhiteSpace(Profile.Name) ? Profile.ConversationId : Profile.Name;
    public string Alias => string.IsNullOrWhiteSpace(Profile.Alias) ? Profile.ConversationId : Profile.Alias;
    public string Initial => string.Concat(Name.EnumerateRunes().Take(1));
    public string VoiceProfileId
    {
        get => Profile.VoiceProfileId;
        set
        {
            if (value == Profile.VoiceProfileId) return;
            if (string.IsNullOrEmpty(value) || !chooseVoice(this, value)) { Notify(); return; }
            Profile = Profile with { VoiceProfileId = value }; Notify(); changed(this);
        }
    }
    public string AnnouncementChoiceId
    {
        get => Profile.Announcement switch
        {
            AnnouncementMode.Automatic => people > 1 ? "say" : "none",
            AnnouncementMode.Off => "none",
            AnnouncementMode.Nickname => "say",
            AnnouncementMode.NicknameSuffix => "leave",
            _ => Profile.AnnouncementPresetId.Length > 0 ? Profile.AnnouncementPresetId : "custom"
        };
        set
        {
            if (string.IsNullOrEmpty(value)) return; // Collection refresh must not alter saved preferences.
            if (value == AnnouncementChoiceId) return;
            if (value == "custom" && CustomRequested != null)
            {
                if (!customRequestPending) { customRequestPending = true; CustomRequested(this); }
                return;
            }
            var preset = (Profile.AnnouncementPresets ?? new()).FirstOrDefault(item => item.Id == value);
            var mode = value switch { "none" => AnnouncementMode.Off, "say" => AnnouncementMode.Nickname,
                "leave" => AnnouncementMode.NicknameSuffix, _ => AnnouncementMode.Custom };
            Profile = Profile with { Announcement = mode, AnnouncementPresetId = preset?.Id ?? "",
                CustomAnnouncement = preset?.Text ?? Profile.CustomAnnouncement,
                CustomPosition = preset?.Position ?? Profile.CustomPosition };
            Notify(); Notify(nameof(CustomEnabled)); Notify(nameof(CustomAnnouncement)); Notify(nameof(CustomPosition));
            Notify(nameof(CanDeleteAnnouncement));
            changed(this);
        }
    }
    // Retain the internal enum facade for existing simulation fixtures.
    public int Announcement { get => (int)Profile.Announcement; set
        { Profile = Profile with { Announcement = (AnnouncementMode)value, AnnouncementPresetId = "" };
          Notify(nameof(AnnouncementChoiceId)); Notify(nameof(CustomEnabled)); Notify(nameof(CanDeleteAnnouncement)); changed(this); } }
    public bool CustomEnabled => Profile.Announcement == AnnouncementMode.Custom && Profile.AnnouncementPresetId.Length == 0;
    public bool CanDeleteAnnouncement => Profile.Announcement == AnnouncementMode.Custom &&
        (Profile.AnnouncementPresets ?? new()).Any(item => item.Id == Profile.AnnouncementPresetId);
    public string CustomAnnouncement { get => Profile.CustomAnnouncement; set
        { Profile = Profile with { CustomAnnouncement = value }; Notify(); changed(this); } }
    public int CustomPosition { get => (int)Profile.CustomPosition; set
        { if (value is < 0 or > 1) return; Profile = Profile with { CustomPosition = (AnnouncementPosition)value }; Notify(); changed(this); } }
    public bool SaveCustom()
    {
        var text = Profile.CustomAnnouncement.Trim();
        if (text.Length == 0) return false;
        var presets = (Profile.AnnouncementPresets ?? new()).ToList();
        var preset = presets.FirstOrDefault(item => item.Text == text && item.Position == Profile.CustomPosition)
            ?? new AnnouncementPreset(Guid.NewGuid().ToString("N"), text, Profile.CustomPosition);
        presets.RemoveAll(item => item.Id == preset.Id); presets.Insert(0, preset);
        Profile = Profile with { AnnouncementPresets = presets.Take(12).ToList(), AnnouncementPresetId = preset.Id,
            CustomAnnouncement = text, Announcement = AnnouncementMode.Custom };
        RefreshAnnouncements(); Notify(nameof(AnnouncementChoiceId)); Notify(nameof(CustomEnabled)); changed(this);
        Notify(nameof(CanDeleteAnnouncement));
        return true;
    }
    public bool DeleteSelectedAnnouncement()
    {
        if (!CanDeleteAnnouncement) return false;
        var id = Profile.AnnouncementPresetId;
        Profile = Profile with { AnnouncementPresets = (Profile.AnnouncementPresets ?? new()).Where(item => item.Id != id).ToList(),
            AnnouncementPresetId = "", CustomAnnouncement = "", Announcement = AnnouncementMode.Off };
        RefreshAnnouncements(); Notify(nameof(AnnouncementChoiceId)); Notify(nameof(CustomEnabled));
        Notify(nameof(CustomAnnouncement)); Notify(nameof(CanDeleteAnnouncement)); changed(this);
        return true;
    }
    public bool DeleteAnnouncement(string id)
    {
        if (!(Profile.AnnouncementPresets ?? new()).Any(item => item.Id == id)) return false;
        var selected = Profile.Announcement == AnnouncementMode.Custom && Profile.AnnouncementPresetId == id;
        Profile = Profile with { AnnouncementPresets = (Profile.AnnouncementPresets ?? new()).Where(item => item.Id != id).ToList(),
            AnnouncementPresetId = selected ? "" : Profile.AnnouncementPresetId,
            Announcement = selected ? AnnouncementMode.Off : Profile.Announcement,
            CustomAnnouncement = selected ? "" : Profile.CustomAnnouncement };
        RefreshAnnouncements(); Notify(nameof(AnnouncementChoiceId)); Notify(nameof(CustomEnabled));
        Notify(nameof(CanDeleteAnnouncement)); Notify(nameof(CustomAnnouncement)); changed(this);
        return true;
    }
    public void FinishCustomRequest() { customRequestPending = false; Notify(nameof(AnnouncementChoiceId)); }
    public void AddCustom(string text, AnnouncementPosition position)
    {
        Profile = Profile with { CustomAnnouncement = text, CustomPosition = position };
        SaveCustom();
    }
    public ImageSource? Avatar { get => avatar; set { avatar = value; Notify(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<ContactCard>? CustomRequested;
    public void RefreshAnnouncement() => Notify(nameof(AnnouncementChoiceId));
    public void RefreshVoice() => Notify(nameof(VoiceProfileId));
    public void SetPeopleCount(int count)
    {
        if (people == count) return;
        people = count;
        Notify(nameof(AnnouncementChoiceId)); Notify(nameof(InlineSpeechOptions)); Notify(nameof(IsMultiPerson));
    }
    public void AdaptDefaultAnnouncement(bool addingSecond)
    {
        var mode = Profile.Announcement;
        if (addingSecond && mode == AnnouncementMode.Off)
            Profile = Profile with { Announcement = AnnouncementMode.Automatic };
        else if (!addingSecond && mode == AnnouncementMode.Automatic)
            Profile = Profile with { Announcement = AnnouncementMode.Off };
        else return;
        Notify(nameof(AnnouncementChoiceId));
        changed(this);
    }
    public void UpdateIdentity(string name, string alias, string? url)
    {
        Profile = Profile with { Name = name, Alias = alias, AvatarUrl = url ?? Profile.AvatarUrl };
        RefreshAnnouncements(); Notify(nameof(Name)); Notify(nameof(Alias)); Notify(nameof(Initial));
        Notify(nameof(AnnouncementChoiceId)); changed(this);
    }
    public ContactCard(ContactProfile profile, ObservableCollection<VoiceChoice> voices,
        Func<ContactCard, string, bool> chooseVoice, Action<ContactCard> changed)
    {
        Profile = profile; this.chooseVoice = chooseVoice; this.changed = changed;
        Voices = new ListCollectionView(voices);
        Voices.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VoiceChoice.ProviderName)));
        RefreshAnnouncements();
    }
    private void RefreshAnnouncements()
    {
        // Keep unchanged items alive: clearing the list during Start's identity refresh loses ComboBox selection.
        var desired = new System.Collections.Generic.List<AnnouncementChoice>
        {
            new("none", "无提示词"), new("say", $"“{Name}”说+消息"), new("leave", $"消息+“{Name}”留言")
        };
        foreach (var preset in Profile.AnnouncementPresets ?? new())
            desired.Add(new(preset.Id, preset.Text + (preset.Position == AnnouncementPosition.End ? "（句尾）" : "（句首）")));
        desired.Add(new("custom", "自定义"));
        for (var index = 0; index < desired.Count; index++)
        {
            var next = desired[index];
            var existing = Announcements.FirstOrDefault(item => item.Id == next.Id);
            if (existing != null)
            {
                existing.UpdateLabel(next.Label);
                var previous = Announcements.IndexOf(existing);
                if (previous != index) Announcements.Move(previous, index);
            }
            else Announcements.Insert(index, next);
        }
        while (Announcements.Count > desired.Count) Announcements.RemoveAt(Announcements.Count - 1);
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
