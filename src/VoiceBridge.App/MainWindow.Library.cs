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
    private const string DefaultFishId = "fish-default";
    private readonly SpeechProviderFactory speechFactory = new();
    private readonly AvatarCache avatars = new();
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

    private sealed record ProviderRow(string Id, string Name, string Kind, string State, string Endpoint);
    private sealed record LibraryRow(string Id, string Name, string VoiceId, string ProviderName);
    private sealed record RoutingSnapshot(ContactProfile[] Contacts, VoiceProfile[] Voices,
        SpeechProviderProfile[] Providers, Dictionary<string, string> Credentials);

    private void InitializeLibrary(LocalSettings settings)
    {
        selectionAccount = AccountOf();
        providers.AddRange((settings.Providers ?? new()).Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .DistinctBy(item => item.Id));
        if (!providers.Any(item => item.Id == DefaultFishId))
            providers.Insert(0, new() { Id = DefaultFishId, Name = "Fish Audio", Kind = SpeechProviderKind.Fish,
                Endpoint = SpeechProviderFactory.DefaultEndpoint(SpeechProviderKind.Fish), Model = settings.FishModel,
                Latency = settings.Latency, CredentialId = selectedKeyName });
        library.AddRange((settings.VoiceLibrary ?? new()).Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .DistinctBy(item => item.Id));
        if (library.Count == 0)
        {
            foreach (var voice in VoicePresets.Select(item => new NamedVoice(item.Name, item.Id)).Concat(userVoices)
                .DistinctBy(item => item.Id))
                library.Add(new() { Id = "fish-" + voice.Id, Name = voice.Name, ProviderId = DefaultFishId, VoiceId = voice.Id });
            if (!string.IsNullOrWhiteSpace(settings.Voice) && !library.Any(item => item.VoiceId == settings.Voice))
                library.Add(new() { Id = "fish-" + settings.Voice, Name = "此前使用的音色", ProviderId = DefaultFishId, VoiceId = settings.Voice });
        }
        contactPreferences.AddRange((settings.ContactProfiles ?? new()).Where(item =>
            !string.IsNullOrWhiteSpace(item.AccountId) && !string.IsNullOrWhiteSpace(item.ConversationId))
            .DistinctBy(item => (item.AccountId, item.ConversationId)));
        if (contactPreferences.Count == 0 && !string.IsNullOrWhiteSpace(settings.WeChatConversation) &&
            !string.IsNullOrWhiteSpace(settings.WeChatAccount))
            contactPreferences.Add(new() { AccountId = settings.WeChatAccount, ConversationId = settings.WeChatConversation,
                Name = settings.WeChatLabel, Alias = settings.WeChatAlias, Selected = true,
                VoiceProfileId = library.FirstOrDefault(item => item.ProviderId == DefaultFishId && item.VoiceId == settings.Voice)?.Id
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
        Closed += (_, _) =>
        {
            avatarStop.Cancel();
            avatars.Dispose();
            avatarStop.Dispose();
            speechFactory.Dispose();
        };
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
        card.CustomRequested += requested => Dispatcher.BeginInvoke(new Action(() => EditCustomAnnouncement(requested, newPhrase: true)));
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
        AddContactButton.Content = contactCards.Count == 0 ? "＋ 添加联系人" : "＋ 添加一个人";
        PrefixHint.Text = contactCards.Count > 1
            ? "依次朗读每个人的消息。音色和提示词可分别设置。"
            : "";
        PrefixHint.Visibility = contactCards.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PublishRouting()
    {
        if (!libraryReady) return;
        SyncDefaultFish();
        foreach (var card in contactCards)
        {
            RefreshContactSpeech(card);
            UpsertContact(card.Profile);
        }
        Volatile.Write(ref routing, new(contactCards.Select(card => card.Profile).ToArray(), library.ToArray(),
            providers.ToArray(), userKeys.ToDictionary(key => key.Name, key => key.Value, StringComparer.Ordinal)));
    }

    private void SyncDefaultFish()
    {
        var index = providers.FindIndex(item => item.Id == DefaultFishId);
        if (index >= 0)
            providers[index] = providers[index] with { CredentialId = selectedKeyName, Model = fishModel,
                Latency = LatencyBox.SelectedItem as string ?? "balanced" };
    }

    private bool ChooseContactVoice(ContactCard card, string voiceId)
    {
        var voice = library.FirstOrDefault(item => item.Id == voiceId);
        var provider = providers.FirstOrDefault(item => item.Id == voice?.ProviderId);
        if (voice == null || provider is not { Enabled: true })
        {
            notice = $"这款音色属于{provider?.Name ?? "尚未配置的 API"}。请在“高级 → 语音服务”启用它，或换用已启用服务的音色。";
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
            VoiceProfileId = library.FirstOrDefault(item => item.ProviderId == DefaultFishId && item.VoiceId == selectedVoiceId)?.Id
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
        if (ListeningActive || sender is not Button { Tag: ContactCard card }) return;
        contactCards.Remove(card);
        UpsertContact(card.Profile with { Selected = false });
        if (contactCards.Count == 1) contactCards[0].AdaptDefaultAnnouncement(addingSecond: false);
        SetPrimaryContact(contactCards.FirstOrDefault()?.Profile);
        UpdateContactsCaption();
        PublishRouting();
        SaveNow();
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
            voiceChoices.Clear();
            foreach (var voice in library)
            {
                var provider = providers.FirstOrDefault(item => item.Id == voice.ProviderId);
                voiceChoices.Add(new(voice.Id, voice.Name, (provider?.Name ?? "服务已删除") +
                    (provider is { Enabled: false } ? "（未启用）" : "")));
            }
            var rows = library.Select(voice => new LibraryRow(voice.Id, voice.Name, voice.VoiceId,
                providers.FirstOrDefault(item => item.Id == voice.ProviderId)?.Name ?? "服务不可用")).ToList();
            var grouped = new ListCollectionView(rows);
            grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryRow.ProviderName)));
            VoiceLibraryList.ItemsSource = grouped;
            var providerRows = providers.Select(provider => new ProviderRow(provider.Id,
                provider.Name == SpeechProviderFactory.Label(provider.Kind) ? "默认连接" : provider.Name,
                SpeechProviderFactory.Label(provider.Kind), provider.Enabled ? "已启用" : "未启用", provider.Endpoint)).ToList();
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

    private void AddLibraryVoiceClick(object sender, RoutedEventArgs e) => EditLibraryVoice(null);

    private bool announcementEditorOpen;
    private void EditAnnouncementClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ContactCard card }) EditCustomAnnouncement(card);
    }

    private void EditCustomAnnouncement(ContactCard card, bool newPhrase = false)
    {
        if (announcementEditorOpen || closing || !contactCards.Contains(card)) { card.FinishCustomRequest(); return; }
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
            card.RefreshAnnouncement();
            Dispatcher.BeginInvoke(new Action(() => { card.FinishCustomRequest(); announcementEditorOpen = false; }), System.Windows.Threading.DispatcherPriority.ContextIdle);
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
            if (editor.NewSecret.Length > 0)
            {
                var keys = userKeys.Where(key => key.Name != updated.CredentialId)
                    .Append(new NamedKey(updated.CredentialId, editor.NewSecret)).ToList();
                KeyStore.Save(keys); userKeys.Clear(); userKeys.AddRange(keys); RefreshKeys(selectedKeyName);
            }
            StoreProvider(updated);
            RefreshLibrary(); SaveNow();
            notice = ListeningActive ? "服务配置已保存，下一条开始合成时生效。" : "语音服务已保存。";
        }
        catch (Exception) { notice = "保存服务凭据失败，请检查本机配置目录权限。"; }
        Render();
    }
    private void DeleteProviderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProviderRow row }) return;
        if (row.Id == DefaultFishId || library.Any(voice => voice.ProviderId == row.Id))
        { notice = row.Id == DefaultFishId ? "默认 Fish 连接请通过编辑修改。" : "请先删除该服务的音色。"; Render(); return; }
        providers.RemoveAll(item => item.Id == row.Id); RefreshLibrary(); SaveNow(); Render();
    }
    private void TestVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && !refreshingLibrary) ScheduleSave();
    }

    private ISpeech SpeechFor(string voiceProfileId, RoutingSnapshot snapshot, ContactProfile? contact = null)
    {
        var voice = snapshot.Voices.FirstOrDefault(item => item.Id == voiceProfileId)
            ?? throw new BridgeException("联系人尚未绑定有效音色，请在首页选择音色库中的音色。");
        var provider = snapshot.Providers.FirstOrDefault(item => item.Id == voice.ProviderId)
            ?? throw new BridgeException("这款音色所属的 API 不存在，请在高级设置中添加它。");
        if (contact != null) provider = contact.ApplySpeechPreferences(provider);
        var key = snapshot.Credentials.GetValueOrDefault(provider.CredentialId, "");
        SpeechProviderFactory.ValidateCredential(provider, key);
        return speechFactory.Create(provider, voice, () => key);
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
            var spoken = contact.Prepare(text, snapshot.Contacts.Length);
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
