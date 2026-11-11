using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VoiceBridge.Audio;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;
using VoiceBridge.WeChat;

namespace VoiceBridge.App;

public partial class MainWindow
{
    // The free Edge voices run through the bundled edge-tts helper (one child process).
    private static readonly EdgeSpeechHost EdgeHost = new();
    private readonly SpeechProviderFactory speechFactory = new(edge: (request, token) =>
        EdgeHost.SpeakAsync(request.Text, request.Voice, request.Speed, token));
    private readonly AvatarCache avatars = new();
    private readonly ContactSearchCache contactIndexCache = new();
    private readonly CancellationTokenSource avatarStop = new();
    private readonly List<SpeechProviderProfile> providers = new();
    private readonly List<VoiceProfile> library = new();
    private readonly List<ContactProfile> contactPreferences = new();
    private readonly ObservableCollection<ContactCard> contactCards = new();
    private readonly ObservableCollection<VoiceChoice> voiceChoices = new();
    private readonly List<ContactHistory> contactHistory = new();
    private readonly List<SearchHistory> searchHistory = new();
    private WindowsPlayer? livePlayer;
    private bool libraryReady, refreshingLibrary, setupCompleted;
    private string selectionAccount = "";
    private RoutingSnapshot routing = new([], [], [], new(StringComparer.Ordinal));

    private sealed record ProviderRow(string Id, string Name, string Kind, bool IsActive);
    private sealed record LibraryRow(string Id, string Name, string VoiceId, string ProviderName,
        bool ProviderMissing);
    private sealed record RoutingSnapshot(ContactProfile[] Contacts, VoiceProfile[] Voices,
        SpeechProviderProfile[] Providers, Dictionary<string, string> Credentials);

    /// <summary>
    /// Vendor label survives deleting the connection that created a voice: another
    /// key of the same service lends its name, otherwise the cached protocol does.
    /// </summary>
    private static string VendorName(VoiceProfile voice, IEnumerable<SpeechProviderProfile> providers)
    {
        var vendor = VoiceLibraryMigration.VendorOf(voice, providers);
        var sibling = vendor is null ? null : providers.FirstOrDefault(item => ServiceIdentity.Of(item) == vendor);
        if (sibling is not null) return ServiceLabel(sibling);
        if (ProviderCatalog.Find(vendor) is { } preset) return preset.Label;
        return voice.ProviderKind is { } kind ? SpeechProviderFactory.Label(kind) : "未知语音服务";
    }

    /// <summary>
    /// The heading a service is listed under, shared by its keys and its voices.
    /// A mainstream vendor uses its catalog name; a generic or local service adds
    /// its address, so two OpenAI-compatible services never end up under one heading.
    /// </summary>
    private static string ServiceLabel(SpeechProviderProfile provider)
    {
        var preset = ProviderCatalog.For(provider);
        if (preset.Category == PresetCategory.Mainstream) return preset.Label;
        return Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out var address)
            ? preset.Label + " · " + address.Authority : preset.Label;
    }

    /// <summary>
    /// The key that will actually synthesize this voice: any valid key of the
    /// vendor does. Null only when the vendor has no key left, which is also the
    /// only case where the voice counts as unavailable in the UI.
    /// </summary>
    private static SpeechProviderProfile? ActiveProviderFor(
        VoiceProfile voice, IEnumerable<SpeechProviderProfile> providers) =>
        VoiceLibraryMigration.ActiveKeyFor(voice, providers);

    private void InitializeLibrary(LocalSettings settings)
    {
        selectionAccount = AccountOf();
        // LocalSettings.Load() has already stripped the factory connection and the
        // voices that older builds seeded. A fresh install has no services and no
        // voices: nothing is seeded, and every entry that exists belongs to the user.
        providers.AddRange((settings.Providers ?? new())
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Where(item => !ProviderMigration.IsFactoryDefault(item.Id))
            .DistinctBy(item => item.Id));
        library.AddRange((settings.VoiceLibrary ?? new())
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Where(item => !VoiceLibraryMigration.IsFactoryVoice(item))
            .DistinctBy(item => item.Id));
        contactPreferences.AddRange((settings.ContactProfiles ?? new()).Where(item =>
            !string.IsNullOrWhiteSpace(item.AccountId) && !string.IsNullOrWhiteSpace(item.ConversationId))
            .DistinctBy(item => (item.AccountId, item.ConversationId)));
        if (contactPreferences.Count == 0 && !string.IsNullOrWhiteSpace(settings.WeChatConversation) &&
            !string.IsNullOrWhiteSpace(settings.WeChatAccount))
            contactPreferences.Add(new() { AccountId = settings.WeChatAccount, ConversationId = settings.WeChatConversation,
                Name = settings.WeChatLabel, Alias = settings.WeChatAlias, Selected = true,
                VoiceProfileId = library.FirstOrDefault(item => item.VoiceId == settings.Voice)?.Id
                    ?? library.FirstOrDefault()?.Id ?? "" });
        contactHistory.AddRange((settings.ContactHistory ?? new()).Take(12));
        searchHistory.AddRange((settings.SearchHistory ?? new()).Take(8));
        var testVoices = new ListCollectionView(voiceChoices);
        testVoices.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VoiceChoice.ProviderName)));
        TestVoiceBox.ItemsSource = testVoices;
        ContactsList.ItemsSource = contactCards;
        ContactsList.Loaded += (_, _) => Render();
        libraryReady = true;
        RefreshLibrary(settings.TestVoiceProfileId);
        LoadContactCards();
        Loaded += (_, _) => { foreach (var card in contactCards) _ = FetchAvatarAsync(card); };
        setupCompleted = settings.SetupCompleted || HasUsableConfiguration();
        if (!setupCompleted) NavHelp.IsChecked = true;
        PublishRouting();
        RefreshHistory();
        WarmEdgeIfUsed();
        Closed += (_, _) =>
        {
            avatarStop.Cancel();
            avatars.Dispose();
            avatarStop.Dispose();
            speechFactory.Dispose();
            EdgeHost.Dispose();
        };
    }

    /// <summary>Loading edge-tts takes seconds; do it early when an Edge service is configured.</summary>
    private void WarmEdgeIfUsed()
    {
        if (providers.Any(item => item.Kind == SpeechProviderKind.EdgeTts)) EdgeHost.Warm();
    }

    private void LoadContactCards()
    {
        contactCards.Clear();
        foreach (var profile in contactPreferences.Where(item => item.AccountId == AccountOf() && item.Selected).Take(8))
        {
            var recovered = profile with { AvatarUrl = profile.AvatarUrl ?? contactHistory.FirstOrDefault(item =>
                item.AccountId == profile.AccountId && item.ConversationId == profile.ConversationId)?.AvatarUrl };
            var card = CardFor(recovered);
            contactCards.Add(card);
            if (IsLoaded) _ = FetchAvatarAsync(card);
        }
        SetPrimaryContact(contactCards.FirstOrDefault()?.Profile);
        UpdateContactsCaption();
    }

    private ContactCard CardFor(ContactProfile profile)
    {
        var card = new ContactCard(profile, voiceChoices, ChooseContactVoice, ContactPreferenceChanged)
            { Avatar = avatars.Cached(profile.AccountId, profile.ConversationId) };
        RefreshContactSpeech(card);
        return card;
    }

    private void SetPrimaryContact(ContactProfile? profile)
    {
        selectedId = profile?.ConversationId;
        selectedName = profile?.Name;
        selectedWechat = profile?.Alias;
    }

    private void UpdateContactsCaption()
    {
        foreach (var card in contactCards) card.SetPeopleCount(contactCards.Count);
        SingleContactSurface.Content = contactCards.Count == 1 ? contactCards[0] : null;
        SingleContactSurface.Visibility = contactCards.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        ContactsList.Visibility = contactCards.Count == 1 ? Visibility.Collapsed : Visibility.Visible;
        SingleSpeechGap.Width = new GridLength(contactCards.Count == 1 ? 12 : 0);
        SingleSpeechColumn.Width = contactCards.Count == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumnSpan(ContactsHeader, 3);
        Grid.SetColumnSpan(ContactsList, contactCards.Count == 1 ? 1 : 3);
        ContactsCaption.Text = $"朗读对象 · {contactCards.Count}/8";
        AddContactButton.Content = "＋ 添加";
        // The old one-line explainer is gone: the cards speak for themselves.
        PrefixHint.Text = "";
        PrefixHint.Visibility = Visibility.Collapsed;
    }

    private void PublishRouting()
    {
        if (!libraryReady) return;
        foreach (var card in contactCards)
        {
            RefreshContactSpeech(card);
            UpsertContact(card.Profile);
        }
        Volatile.Write(ref routing, new(contactCards.Select(card => card.Profile).ToArray(), library.ToArray(),
            providers.ToArray(), userKeys.ToDictionary(key => key.Name, key => key.Value, StringComparer.Ordinal)));
    }

    private bool ChooseContactVoice(ContactCard card, string voiceId)
    {
        var voice = library.FirstOrDefault(item => item.Id == voiceId);
        if (voice == null || ActiveProviderFor(voice, providers) is null)
        {
            notice = "音色已失效，请重新选择。";
            Render();
            return false;
        }
        return true;
    }

    private void ContactPreferenceChanged(ContactCard card)
    {
        UpsertContact(card.Profile);
        PublishRouting();
        ScheduleSave();
    }

    private void UpsertContact(ContactProfile profile)
    {
        var index = contactPreferences.FindIndex(item => item.AccountId == profile.AccountId &&
            item.ConversationId == profile.ConversationId);
        if (index < 0) contactPreferences.Add(profile);
        else contactPreferences[index] = profile;
    }

    private void AddContactClick(object sender, RoutedEventArgs e)
    {
        if (ListeningActive || closing) { notice = "请先结束监听，再修改监听名单。"; Render(); return; }
        if (contactCards.Count >= 8) { notice = "一次最多监听 8 个联系人。"; Render(); return; }
        pickingContact = true;
        ApplySelection();
        RefreshHistory();
        ContactSearch.Focus();
    }

    private void AddSelectedContact(ContactEntry contact)
    {
        if (ListeningActive || closing) return;
        if (contact.Id.EndsWith("@chatroom", StringComparison.Ordinal))
            throw new BridgeException("这里仅支持一对一联系人，群聊不能加入监听名单。");
        var account = AccountOf();
        if (string.IsNullOrWhiteSpace(account)) { notice = "请先在高级设置中选择微信账号。"; Render(); return; }
        var exists = contactCards.FirstOrDefault(item => item.Profile.ConversationId == contact.Id);
        if (exists == null && contactCards.Count >= 8) { notice = "一次最多监听 8 个联系人。"; Render(); return; }
        var remembered = contactPreferences.FirstOrDefault(item => item.AccountId == account && item.ConversationId == contact.Id);
        var profile = (remembered ?? new ContactProfile { AccountId = account, ConversationId = contact.Id,
            VoiceProfileId = library.FirstOrDefault(item => item.VoiceId == selectedVoiceId)?.Id
                ?? library.FirstOrDefault()?.Id ?? "" }) with
        { Name = contact.Name, Alias = contact.Wechat, AvatarUrl = contact.AvatarUrl, Selected = true };
        if (!string.IsNullOrWhiteSpace(ContactSearch.Text)) RememberSearch(ContactSearch.Text.Trim());
        UpsertContact(profile);
        if (exists == null)
        {
            if (contactCards.Count == 1) contactCards[0].AdaptDefaultAnnouncement(addingSecond: true);
            exists = CardFor(profile); contactCards.Add(exists);
        }
        SetPrimaryContact(exists.Profile);
        _ = FetchAvatarAsync(exists);
        RememberContact(profile);
        pickingContact = false;
        ContactSearch.Text = "";
        ContactResults.ItemsSource = null;
        ResultsPanel.Visibility = Visibility.Collapsed;
        ContactHint.Text = "";
        notice = null;
        UpdateContactsCaption();
        PublishRouting();
        SaveNow();
        Render();
    }

    /// <summary>
    /// Builds before avatar support saved no AvatarUrl, so an upgraded profile can
    /// never reach AvatarCache.GetAsync. The verified contact index already stores
    /// the real URL keyed by conversation id: recover it once, which also persists it.
    /// </summary>
    private void RestoreMissingAvatars(WeChatSession account)
    {
        if (closing) return;
        foreach (var card in contactCards.Where(item => string.IsNullOrWhiteSpace(item.Profile.AvatarUrl)).ToArray())
        {
            if (avatars.Cached(card.Profile.AccountId, card.Profile.ConversationId) is not null) continue;
            if (!contactIndexCache.TryFind(account, card.Profile.ConversationId, out var entry) ||
                string.IsNullOrWhiteSpace(entry?.AvatarUrl)) continue;
            card.UpdateAvatar(entry!.AvatarUrl);
            _ = FetchAvatarAsync(card);
        }
    }

    private async Task FetchAvatarAsync(ContactCard card)
    {
        try
        {
            var profile = card.Profile;
            var avatar = await avatars.GetAsync(profile.AccountId, profile.ConversationId, profile.AvatarUrl, avatarStop.Token);
            if (!closing) card.Avatar = avatar;
        }
        catch (Exception) { /* An optional portrait never prevents reading messages. */ }
    }

    private void RemoveContactClick(object sender, RoutedEventArgs e)
    {
        if (closing || sender is not Button { Tag: ContactCard card }) return;
        if (!contactCards.Remove(card)) return;
        UpsertContact(card.Profile with { Selected = false });
        if (contactCards.Count == 1) contactCards[0].AdaptDefaultAnnouncement(addingSecond: false);
        SetPrimaryContact(contactCards.FirstOrDefault()?.Profile);
        UpdateContactsCaption();
        // PublishRouting replaces the immutable snapshot; the running session reads it
        // on every message, so this person stops being read without restarting the baseline.
        PublishRouting();
        SaveNow();
        notice = contactCards.Count == 0 ? "朗读名单已空，添加联系人后才会继续朗读。" : null;
        Render();
    }

    private void PreviewContactClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContactCard card }) return;
        if (manualCancel != null && manualPreviewContact != card.Profile.ConversationId) return;
        SetPrimaryContact(card.Profile);
        PreviewClick(sender, e);
    }

    private void RememberContact(ContactProfile profile)
    {
        var item = new ContactHistory(profile.AccountId, profile.ConversationId, profile.Name, profile.Alias,
            profile.AvatarUrl, DateTimeOffset.UtcNow);
        var updated = RecentContacts.Add(contactHistory, item);
        contactHistory.Clear(); contactHistory.AddRange(updated);
        RefreshHistory();
    }

    private void RememberSearch(string query)
    {
        var updated = RecentContacts.Add(searchHistory, new SearchHistory(AccountOf(), query, DateTimeOffset.UtcNow));
        searchHistory.Clear(); searchHistory.AddRange(updated);
        RefreshHistory();
        ScheduleSave();
    }

    private void RefreshHistory()
    {
        if (!libraryReady) return;
        RecentContactsList.ItemsSource = contactHistory.Where(item => item.AccountId == AccountOf())
            .Select(item => CardFor(contactPreferences.FirstOrDefault(profile => profile.AccountId == item.AccountId &&
                profile.ConversationId == item.ConversationId) ?? new() { AccountId = item.AccountId, ConversationId = item.ConversationId,
                Name = item.Name, Alias = item.Alias, AvatarUrl = item.AvatarUrl })).ToArray();
        foreach (var card in RecentContactsList.Items.OfType<ContactCard>()) _ = FetchAvatarAsync(card);
        UpdateHistoryVisibility();
    }

    private void UpdateHistoryVisibility()
    {
        var count = RecentContactsList.Items.Count;
        HistoryPanel.Visibility = pickingContact && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearHistoryButton.Visibility = count >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UseRecentContactClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ContactCard card } && !ListeningActive)
            AddSelectedContact(new(card.Profile.ConversationId, card.Profile.Name, card.Profile.Alias, card.Profile.AvatarUrl));
    }

    private void DeleteRecentContactClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContactCard card }) return;
        contactHistory.RemoveAll(item => item.AccountId == card.Profile.AccountId && item.ConversationId == card.Profile.ConversationId);
        RefreshHistory(); SaveNow();
    }

    private void ClearHistoryClick(object sender, RoutedEventArgs e)
    {
        contactHistory.RemoveAll(item => item.AccountId == AccountOf());
        searchHistory.RemoveAll(item => item.AccountId == AccountOf());
        RefreshHistory(); SaveNow();
    }

    private void RefreshLibrary(string? selectedTest = null)
    {
        if (!libraryReady) return;
        refreshingLibrary = true;
        try
        {
            var selected = selectedTest ?? TestVoiceBox.SelectedValue as string;
            var rows = new List<LibraryRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var voice in library)
            {
                var own = providers.FirstOrDefault(item => item.Id == voice.ProviderId);
                // Voices are grouped by service, like the keys in the service list.
                // After deleting a connection the vendor name stays on the group;
                // only the state of the row changes (see the ItemTemplate style).
                var group = own is not null ? ServiceLabel(own) : VendorName(voice, providers);
                var usable = ActiveProviderFor(voice, providers) is not null;
                seen.Add(voice.Id);
                SyncVoiceChoice(voice.Id, voice.Name, group, usable);
                // A voice tied to one model says so next to its id.
                var detail = voice.VoiceId.Length > 0 ? voice.VoiceId : voice.ReferenceAudioPath;
                if (voice.Model.Length > 0)
                    detail += " · 固定 " + (own is null ? voice.Model : ProviderCatalog.For(own).ModelLabelOf(voice.Model));
                rows.Add(new LibraryRow(voice.Id, voice.Name, detail, group, !usable));
            }
            // Drop choices whose voice is gone. Entries are never rebuilt wholesale:
            // clearing the shared list used to reset every ComboBox selection and
            // blank the voice the user had already picked.
            for (var i = voiceChoices.Count - 1; i >= 0; i--)
                if (!seen.Contains(voiceChoices[i].Id)) voiceChoices.RemoveAt(i);
            var grouped = new ListCollectionView(rows);
            grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryRow.ProviderName)));
            VoiceLibraryList.ItemsSource = grouped;
            var providerRows = providers.Select(provider => new ProviderRow(provider.Id,
                string.IsNullOrWhiteSpace(provider.Name) ? SpeechProviderFactory.Label(provider.Kind) : provider.Name,
                ServiceLabel(provider),
                provider.Enabled)).ToList();
            var providerGroups = new ListCollectionView(providerRows);
            providerGroups.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProviderRow.Kind)));
            ProvidersList.ItemsSource = providerGroups;
            TestVoiceBox.SelectedValue = selected;
            if (TestVoiceBox.SelectedIndex < 0) TestVoiceBox.SelectedIndex = 0;
        }
        finally { refreshingLibrary = false; }
        foreach (var card in contactCards) card.RefreshVoice();
        PublishRouting();
    }

    /// <summary>
    /// Update one dropdown entry in place, or append it when it is new. Never
    /// clearing the shared list is what keeps an existing selection intact while
    /// services are added, edited or removed.
    /// </summary>
    private void SyncVoiceChoice(string id, string name, string group, bool usable)
    {
        var existing = voiceChoices.FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            voiceChoices.Add(new(id, name, group, usable));
            return;
        }
        existing.Name = name;
        existing.ProviderName = group;
        existing.Usable = usable;
    }

    private void AddLibraryVoiceClick(object sender, RoutedEventArgs e) => EditLibraryVoice(null);

    private bool announcementEditorOpen;
    private void EditAnnouncementClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ContactCard card }) EditCustomAnnouncement(card);
    }

    /// <summary>
    /// The last menu row is an action, not a choice. Put the closed ComboBox back to
    /// the real selection immediately (SetCurrentValue does not write to the TwoWay
    /// source), then open the editor. Nothing has to be "restored" afterwards when the
    /// user cancels or closes the dialog, because the selection never left.
    /// </summary>
    private void AnnouncementSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        // Identify the action row by the item the user actually picked. SelectedValue is
        // unreliable: the TwoWay binding reverts it to the source value immediately,
        // while SelectedItem (and therefore the closed box) still shows "自定义".
        if (!e.AddedItems.OfType<AnnouncementChoice>().Any(item => item.Id == AnnouncementChoice.NewId)) return;
        if (combo.DataContext is not ContactCard card) return;
        RestoreAnnouncementItem(combo, card);
        // Normal priority: ContextIdle is starved by the window's repeating timers,
        // which used to leave the editor unopened in the real main window.
        Dispatcher.BeginInvoke(new Action(() => EditCustomAnnouncement(card, newPhrase: true)));
    }

    /// <summary>
    /// Put both the value and the visible item back. SetCurrentValue changes the
    /// control without writing into the TwoWay source, so the action row can never
    /// become a persisted or displayed selection.
    /// </summary>
    private static void RestoreAnnouncementItem(ComboBox combo, ContactCard card)
    {
        var id = card.AnnouncementChoiceId;
        var item = card.Announcements.FirstOrDefault(entry => entry.Id == id);
        combo.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, item);
        combo.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, id);
    }

    private void EditCustomAnnouncement(ContactCard card, bool newPhrase = false)
    {
        if (announcementEditorOpen || closing || !contactCards.Contains(card))
        {
            SyncAnnouncementSelection(card);
            return;
        }
        announcementEditorOpen = true;
        try
        {
            var editor = new AnnouncementDialog(card.Profile, newPhrase) { Owner = this };
            if (editor.ShowDialog() != true) return;
            card.AddCustom(editor.Phrase, editor.Position);
            SaveNow();
        }
        finally
        {
            // Cancel/close leaves the profile untouched, so the ComboBox already shows
            // the previous entry. A successful add changes the getter to the new
            // preset id, so point the ComboBox at it.
            announcementEditorOpen = false;
            SyncAnnouncementSelection(card);
            Dispatcher.BeginInvoke(new Action(() => SyncAnnouncementSelection(card)));
        }
    }

    private void SyncAnnouncementSelection(ContactCard card)
    {
        if (closing) return;
        card.RefreshAnnouncement();
        // Match by ItemsSource: x:Name and DataContext are unreliable inside a
        // DataTemplate generated by ContentControl / ListBox.
        foreach (var combo in Descendants(this).OfType<ComboBox>()
            .Where(entry => ReferenceEquals(entry.ItemsSource, card.Announcements)))
        {
            RestoreAnnouncementItem(combo, card);
            BindingOperations.GetBindingExpression(combo,
                System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.UpdateTarget();
        }
    }

    private void DeleteAnnouncementFromMenu(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: AnnouncementChoice item } button || !item.IsCustomPreset) return;
        System.Windows.DependencyObject? node = button;
        while (node != null && node is not ComboBoxItem) node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        if (node is ComboBoxItem container && ItemsControl.ItemsControlFromItemContainer(container) is ComboBox { DataContext: ContactCard card } && card.DeleteAnnouncement(item.Id)) SaveNow();
    }

    private void DeleteAnnouncementFromMenuMouse(object sender, System.Windows.Input.MouseButtonEventArgs e) => DeleteAnnouncementFromMenu(sender, e);

    private void CancelContactClick(object sender, RoutedEventArgs e)
    {
        searchTimer.Stop(); searchCancel?.Cancel();
        pickingContact = false; ContactSearch.Text = "";
        ContactResults.ItemsSource = null; ResultsPanel.Visibility = Visibility.Collapsed;
        ContactHint.Text = ""; ApplySelection(); RefreshHistory();
    }
    private void EditLibraryVoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LibraryRow row }) EditLibraryVoice(library.FirstOrDefault(item => item.Id == row.Id));
    }
    private void EditLibraryVoice(VoiceProfile? voice)
    {
        var editor = new VoiceEditor(voice, providers) { Owner = this };
        if (editor.ShowDialog() != true) return;
        var index = library.FindIndex(item => item.Id == editor.Result.Id);
        if (index < 0) library.Add(editor.Result); else library[index] = editor.Result;
        speechFactory.ResetFailures();
        RefreshLibrary(editor.Result.Id); SaveNow();
        notice = ListeningActive ? "音色已更新，下一条开始合成时生效。" : "音色已保存到音色库。";
        Render();
    }
    private void DeleteLibraryVoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryRow row }) return;
        if (contactCards.Any(card => card.Profile.VoiceProfileId == row.Id))
        { notice = "这款音色正在被首页联系人使用，请先给联系人选择其他音色。"; Render(); return; }
        library.RemoveAll(item => item.Id == row.Id);
        RefreshLibrary(); SaveNow(); Render();
    }

    private void AddProviderClick(object sender, RoutedEventArgs e) => EditProvider(null);
    private void EditProviderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProviderRow row }) EditProvider(providers.FirstOrDefault(item => item.Id == row.Id));
    }
    private void EditProvider(SpeechProviderProfile? provider)
    {
        var editor = new ProviderEditor(provider) { Owner = this };
        if (editor.ShowDialog() != true) return;
        try
        {
            var updated = editor.Result;
            // Every key belongs to one service. The editor assigns it from the catalog
            // entry (an edited key keeps the one it has so its voices stay with it);
            // this only covers anything that still arrives without one.
            if (string.IsNullOrWhiteSpace(updated.Vendor))
                updated = updated with { Vendor = ServiceIdentity.Derive(updated) };
            // Adding a key is not applying it: when this vendor already has an
            // active key, the new one stays on standby and nothing switches over.
            if (provider == null && ProviderActivation.StaysOnStandby(providers, updated))
                updated = updated with { Enabled = false };
            if (provider == null && providers.Any(item => item.Name == updated.Name))
            {
                var baseName = updated.Name;
                var number = 2;
                while (providers.Any(item => item.Name == updated.Name)) updated = updated with { Name = baseName + " " + number++ };
            }
            if (provider != null && updated.Kind != provider.Kind && library.Any(voice => voice.ProviderId == provider.Id))
            {
                notice = "这项服务已有音色。请新建另一种 API，再添加它自己的音色。";
                Render();
                return;
            }
            if (!string.IsNullOrWhiteSpace(editor.NewSecret))
            {
                // Replace the value behind this connection's existing credential name.
                // A name minted right now is session-new: if attaching it to the
                // service somehow fails below, the sweep must still keep it.
                if (!userKeys.Any(item => item.Name == updated.CredentialId))
                    sessionNewKeys.Add(updated.CredentialId);
                var keys = KeyStore.Replace(userKeys, updated.CredentialId, editor.NewSecret);
                KeyStore.Save(keys); userKeys.Clear(); userKeys.AddRange(keys); RefreshKeys(updated.CredentialId);
            }
            StoreProvider(updated);
            // A voice whose service is gone must not keep showing "no API" once the
            // user adds one: point those orphans at the key that is actually active
            // for this vendor. Voices of other vendors are never re-pointed.
            if (provider == null)
            {
                var active = updated.Enabled ? updated
                    : providers.FirstOrDefault(item => ServiceIdentity.Same(item, updated) && item.Enabled);
                if (active != null)
                {
                    var bound = VoiceLibraryMigration.BindOrphanVoices(library, providers, active, out _);
                    library.Clear();
                    library.AddRange(bound);
                }
            }
            RefreshLibrary(); SaveNow();
            WarmEdgeIfUsed();
            notice = "已保存。";
        }
        catch (Exception) { notice = "保存服务凭据失败，请检查本机配置目录权限。"; }
        Render();
    }
    private void DeleteProviderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProviderRow row }) DeleteProviderById(row.Id);
    }

    /// <summary>
    /// No entry is built in or protected: every service the user sees can be
    /// removed. Voices that pointed at it are kept and keep their vendor name;
    /// they only become unavailable once the vendor has no key left at all.
    /// </summary>
    private void DeleteProviderById(string id)
    {
        var deleted = providers.FirstOrDefault(item => item.Id == id);
        if (deleted is null) return;
        var vendor = ServiceIdentity.Of(deleted);
        for (var i = 0; i < library.Count; i++)
            if (library[i].ProviderId == id)
                library[i] = library[i] with
                {
                    ProviderKind = library[i].ProviderKind ?? deleted.Kind,
                    Vendor = string.IsNullOrWhiteSpace(library[i].Vendor) ? vendor : library[i].Vendor
                };
        providers.RemoveAll(item => item.Id == id);
        // If another key of this vendor remains, its voices follow that key
        // immediately: any valid key of the vendor runs the same voice ids.
        // A vendor left with only standby keys has nothing to choose between, so
        // its remaining key becomes active (deleting is not "adding applies it").
        var replacement = providers.FirstOrDefault(item => ServiceIdentity.Of(item) == vendor && item.Enabled)
            ?? providers.FirstOrDefault(item => ServiceIdentity.Of(item) == vendor);
        if (replacement != null)
        {
            if (!replacement.Enabled)
            {
                var index = providers.IndexOf(replacement);
                replacement = replacement with { Enabled = true };
                providers[index] = replacement;
            }
            var rebound = VoiceLibraryMigration.UseActiveKey(library, providers, replacement, out _);
            library.Clear();
            library.AddRange(rebound);
        }
        // This service's own credential becomes garbage the moment nothing else
        // points at it: sweep such entries now. Anything another service, the
        // FishKeyName or a contact/test override still names is kept, and keys
        // created during this run stay exempt until the next start.
        SweepCredentials("删除服务", providers, selectedKeyName, contactPreferences);
        RefreshLibrary(); SaveNow();
        notice = "已删除。";
        Render();
    }

    /// <summary>
    /// Pick which key of a vendor is in use. Other vendors keep their own active
    /// key, and the vendor's voices follow the chosen key — they stay usable the
    /// whole time, so this needs no confirmation.
    /// </summary>
    private void ActivateProviderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProviderRow row }) return;
        var target = providers.FirstOrDefault(item => item.Id == row.Id);
        if (target is null || target.Enabled) return;
        var activated = ProviderActivation.Activate(providers, target.Id);
        providers.Clear();
        providers.AddRange(activated);
        var rebound = VoiceLibraryMigration.UseActiveKey(library, providers, target, out _);
        library.Clear();
        library.AddRange(rebound);
        speechFactory.ResetFailures();
        RefreshLibrary(); SaveNow();
        Render();
    }

    /// <summary>The row's single overflow menu: edit and delete.</summary>
    private void ProviderMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProviderRow row } button) return;
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("RowMenu"),
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            // The panel keeps a transparent ring for its shadow. Sitting flush under the
            // button makes that ring overlap the button and the two look glued together,
            // so the panel is pushed down until a little air shows between them.
            VerticalOffset = -4,
        };
        // The panel hangs below the ⋮ button and opens rightward, but it is parked so
        // that its right edge keeps ~16px from the window border: a wider panel is
        // allowed to reach back to the left, which is what makes it read as square
        // instead of a tall sliver.
        // 79.5 = the gap from the button's left edge to the window's right edge (95.5)
        // minus that 16px margin; subtracting the panel's own width turns it into the
        // left-aligned popup offset WPF expects.
        menu.Opened += (_, _) => menu.HorizontalOffset = 79.5 - menu.ActualWidth;
        var edit = new MenuItem { Header = "编辑", Style = (Style)FindResource("RowMenuItem") };
        edit.Click += (_, _) => EditProvider(providers.FirstOrDefault(item => item.Id == row.Id));
        var delete = new MenuItem { Header = "删除", Style = (Style)FindResource("RowMenuItemDanger") };
        delete.Click += (_, _) => DeleteProviderById(row.Id);
        menu.Items.Add(edit);
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private void TestVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && !refreshingLibrary) ScheduleSave();
    }

    private ISpeech SpeechFor(string voiceProfileId, RoutingSnapshot snapshot, ContactProfile? contact = null)
    {
        var voice = snapshot.Voices.FirstOrDefault(item => item.Id == voiceProfileId)
            ?? throw new BridgeException("联系人尚未绑定有效音色，请在首页选择音色库中的音色。");
        // Any key of the same vendor can synthesize this voice id, so a deleted
        // or standby key does not make the voice unusable.
        var provider = ActiveProviderFor(voice, snapshot.Providers)
            ?? throw new BridgeException("音色已失效，请重新选择。");
        if (contact != null) provider = contact.ApplySpeechPreferences(provider);
        // A voice that only works with one model always uses it, whatever the
        // contact card or the service default says.
        provider = SpeechModels.Use(provider, voice.Model);
        var key = snapshot.Credentials.GetValueOrDefault(provider.CredentialId, "");
        SpeechProviderFactory.ValidateCredential(provider, key);
        var effectiveVoice = voice with { ProviderId = provider.Id };
        return speechFactory.Create(provider, effectiveVoice, () => key);
    }

    private void PreviewSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        controller.SetPreview(PreviewBox.IsChecked == true);
        Results.Items.Clear();
        ScheduleSave();
        Render();
    }

    private bool HasUsableConfiguration()
    {
        if (string.IsNullOrWhiteSpace(AccountOf()) || string.IsNullOrWhiteSpace(RootBox.Text) || contactCards.Count == 0)
            return false;
        try
        {
            PublishRouting();
            var snapshot = Volatile.Read(ref routing);
            foreach (var card in contactCards) _ = SpeechFor(card.Profile.VoiceProfileId, snapshot, card.Profile);
            return true;
        }
        catch (BridgeException) { return false; }
    }

    private sealed class RoutedSpeech(MainWindow owner) : IMessageSpeech, IMessageSpeechResolver
    {
        public Task<AudioData> SynthesizeAsync(string text, Action firstAudio, CancellationToken token) =>
            throw new BridgeException("缺少已验证的消息来源，已拒绝语音请求。");
        public ResolvedSpeech Resolve(ChatMessage message, string text)
        {
            var snapshot = Volatile.Read(ref owner.routing);
            var contact = snapshot.Contacts.FirstOrDefault(item => item.AccountId == message.AccountId &&
                item.ConversationId == message.ConversationId) ?? throw new BridgeException("联系人已不在获准名单中。");
            // The worker applies the word book to the message body. Apply the same
            // rules to the announcement, which is only composed here, before joining.
            var rules = Volatile.Read(ref owner.activeReadings);
            var spoken = contact.Prepare(text, snapshot.Contacts.Length,
                rules.Length == 0 ? null : prefix => ReadingText.Apply(prefix, rules, ignoreScope: true));
            if (spoken.Length > SpeechText.MaximumLength) throw new BridgeException("加上提示词后超过两万字，已跳过这条消息。");
            return new(owner.SpeechFor(contact.VoiceProfileId, snapshot, contact), spoken);
        }
        public Task<AudioData> SynthesizeAsync(ChatMessage message, string text, Action firstAudio, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var resolved = Resolve(message, text);
            return resolved.Speech.SynthesizeAsync(resolved.Text, firstAudio, token);
        }
    }
}
