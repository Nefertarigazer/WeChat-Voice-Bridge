using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VoiceBridge.Audio;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;
using VoiceBridge.WeChat;
using Forms = System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace VoiceBridge.App;

/// <summary>
/// The user-facing window. It only offers the real WeChat path: pick one
/// contact, optionally preview the last message, then start reading new
/// incoming text with the cloned voice.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Brush IdleDot = Frozen(0xAE, 0xAE, 0xB2);
    private static readonly Brush LiveDot = Frozen(0x1D, 0x9A, 0x5B);
    private static readonly Brush WarnDot = Frozen(0xB2, 0x76, 0x0A);
    private static readonly Brush ErrorDot = Frozen(0xD3, 0x3B, 0x3B);
    private static readonly Brush IdleFill = Frozen(0xF0, 0xF0, 0xF3);
    private static readonly Brush LiveFill = Frozen(0xE6, 0xF5, 0xED);
    private static readonly Brush ErrorFill = Frozen(0xFC, 0xEC, 0xEC);
    private static readonly Brush WarnFill = Frozen(0xFD, 0xF3, 0xE2);
    private static readonly Brush LabelBrush = Frozen(0x1D, 0x1D, 0x1F);
    private static readonly Brush SecondaryBrush = Frozen(0x6E, 0x6E, 0x73);

    private readonly SessionController controller = new();
    private readonly HttpClient http;
    private readonly Forms.NotifyIcon tray;
    private Stream? trayIconStream;
    private readonly Forms.ToolStripMenuItem trayStop;
    private readonly DispatcherTimer searchTimer;
    private readonly DispatcherTimer saveTimer;
    private Storyboard? updateBreath;
    private Storyboard? pillBreath;
    private readonly List<NamedVoice> userVoices = new();
    private readonly List<NamedKey> userKeys = new();
    /// <summary>
    /// Entries created during this run and not yet attached to anything. The
    /// credential sweep skips them so a key the user just added cannot vanish
    /// mid-flow; the next start judges them again without this exemption.
    /// </summary>
    private readonly HashSet<string> sessionNewKeys = new(StringComparer.Ordinal);
    private readonly ObservableCollection<ReadingItem> readingItems = new();
    private ReadingRule[] activeReadings = [];

    private PythonFixtureSource? source;
    private WeChatDirectory? directory;
    private WindowsPlayer? manualPlayer;
    private CancellationTokenSource? manualCancel;
    private string? manualPreviewContact;
    private Task? manualTask;
    private CancellationTokenSource? searchCancel;

    private bool ready;
    private bool closing;
    private bool allowClose;
    private bool pickingContact;
    private bool lexiconSeeded;
    private string fishModel = FishSpeech.Model;
    private string? noticeField;
    private DispatcherTimer? noticeTimer;

    /// <summary>
    /// The one-line hint, shown as a subtitle at the bottom of the window: one at a time,
    /// never over the control you are using, and gone again after a few seconds. It is
    /// part of the window, so it follows the window and disappears when it is minimised.
    /// </summary>
    private string? notice
    {
        get => noticeField;
        set
        {
            noticeField = string.IsNullOrWhiteSpace(value) ? null : value;
            noticeTimer?.Stop();
            if (noticeField is null)
            {
                HideHint();
                return;
            }
            HintText.Text = noticeField;
            ShowHint();
            noticeTimer?.Start();
        }
    }

    private void ShowHint()
    {
        HintToast.Visibility = Visibility.Visible;
        HintToast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void HideHint()
    {
        if (HintToast.Visibility != Visibility.Visible)
            return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => HintToast.Visibility = Visibility.Collapsed;
        HintToast.BeginAnimation(OpacityProperty, fade);
    }
    private string? selectedId;
    private string? selectedName;
    private string? selectedWechat;
    private string selectedVoiceId = "";
    private string selectedKeyName = "";
    private string activeApiKey = "";

    private static string WorkerScript => Path.Combine(AppContext.BaseDirectory, "worker", "main.py");

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The picture the tray shows. It has to be the app icon, because the
    /// generic Windows placeholder makes the bridge look like an uninstalled
    /// program in the overflow list.
    /// </summary>
    private Icon LoadTrayIcon()
    {
        try
        {
            // The icon is embedded in the program, so it is there in every build.
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
            if (resource is not null)
            {
                // The icon reads from the stream while it lives, so the stream
                // has to outlive the tray icon.
                var buffer = new MemoryStream();
                resource.Stream.CopyTo(buffer);
                resource.Stream.Dispose();
                buffer.Position = 0;
                trayIconStream = buffer;
                return new Icon(buffer);
            }
        }
        catch (Exception) { /* The next source has the same picture. */ }
        foreach (var name in new[] { "WeChatVoiceBridge.ico", "app.ico" })
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, name);
                if (File.Exists(path))
                    return new Icon(path);
            }
            catch (Exception) { }
        }
        return SystemIcons.Application;
    }

    public MainWindow(HttpClient http, string? updateNotice = null)
    {
        this.http = http;
        InitializeComponent();
        InitializeUpdates(updateNotice);
        // The update dot breathes for the whole session; only the pill's breath is
        // state-dependent (see SetPillBreathing).
        updateBreath = BreathingLight.Create(UpdateStateDot, UpdateDotScale, UpdateStateHalo, UpdateHaloScale);
        updateBreath.Begin();
        searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        searchTimer.Tick += SearchTimerTick;
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        saveTimer.Tick += SaveTimerTick;
        noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        noticeTimer.Tick += (_, _) => notice = null;
        InitializeInteraction();

        tray = new() { Icon = LoadTrayIcon(), Text = "微信语音桥 · 未开始", Visible = true };
        trayStop = new("结束监听", null, (_, _) => Dispatcher.InvokeAsync(async () => await StopAsync()));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => Dispatcher.InvokeAsync(RestoreWindow));
        menu.Items.Add(trayStop);
        menu.Items.Add("退出程序", null, (_, _) => Dispatcher.InvokeAsync(Close));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.InvokeAsync(RestoreWindow);

        controller.Changed += _ => Dispatcher.InvokeAsync(Render);
        controller.Processed += item =>
        {
            var stamp = controller.ActiveStamp;
            Dispatcher.InvokeAsync(() =>
            {
                if (stamp != controller.ActiveStamp || controller.Status.State != BridgeState.Running)
                    return;
                var line = Friendly(item);
                if (line != null)
                    AddResult(line);
            });
        };

        LatencyBox.ItemsSource = new[] { "balanced", "normal", "low" };
        LatencyBox.SelectionChanged += (_, _) => ScheduleSave();
        PreviewBox.Checked += PreviewSettingChanged;
        PreviewBox.Unchecked += PreviewSettingChanged;
        var settings = ReadStoredSettings();
        fishModel = settings.FishModel == FishSpeech.PaidModel ? FishSpeech.PaidModel : FishSpeech.Model;
        selectedVoiceId = settings.Voice;
        selectedKeyName = settings.FishKeyName;
        userVoices.AddRange(settings.Voices);
        foreach (var rule in settings.Rules.Where(rule => !string.IsNullOrWhiteSpace(rule.Match) &&
            !string.IsNullOrWhiteSpace(rule.Say)))
            readingItems.Add(new(rule));
        // The word book starts with one example. It is offered once, so a user
        // who removes it does not get it back on the next start.
        lexiconSeeded = settings.LexiconSeeded;
        var seeded = false;
        if (!lexiconSeeded)
        {
            if (readingItems.Count == 0)
            {
                readingItems.Add(new(new ReadingRule("1", "一", true) { Scope = "both" }));
                seeded = true;
            }
            lexiconSeeded = true;
        }
        userKeys.AddRange(KeyStore.Load(out var createdThisLoad));
        foreach (var created in createdThisLoad)
            sessionNewKeys.Add(created);
        // The sweep runs strictly after every settings and store migration has
        // finished — never before, or it could delete a migration's target. It
        // only removes entries that nothing points at any more.
        SweepCredentials("启动", settings.Providers, settings.FishKeyName, settings.ContactProfiles);
        LatencyBox.SelectedItem = settings.Latency is "normal" or "low" ? settings.Latency : "balanced";
        VolumeBox.Value = Math.Clamp(settings.Volume, 0, 1);
        PreviewBox.IsChecked = settings.Preview;
        RootBox.Text = settings.WeChatRoot;
        AccountBox.Text = settings.WeChatAccount;
        selectedId = settings.WeChatConversation;
        selectedName = settings.WeChatLabel;
        selectedWechat = settings.WeChatAlias;

        // Most installs keep WeChat in a well-known place. Find it instead of asking.
        var root = settings.WeChatRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = WeChatSession.DiscoverRoot() ?? "";
            if (root.Length == 0)
                // The bounded name scan can still miss a deeply custom folder. Point
                // the user at the existing 浏览… picker instead of failing silently.
                notice ??= "未找到微信数据目录，请在高级设置里用「浏览…」手动指定。";
        }
        RootBox.Text = root;
        // The size of the scratch cache is chosen by the user, and it never
        // grows past that size.
        CacheLimitBox.SelectedValue = settings.CacheLimitBytes;
        SessionCache.Trim(settings.CacheLimitBytes, settings.WeChatRoot);
        // The account folders under the root are listed, so the user only
        // has to pick the one that is logged in.
        RefreshAccounts(settings.WeChatAccount);

        PaidModel.IsChecked = settings.FishModel == FishSpeech.PaidModel;
        FreeModel.IsChecked = settings.FishModel != FishSpeech.PaidModel;
        RefreshDevices(settings.Device);
        RefreshVoices();
        RefreshKeys();
        ReadingList.ItemsSource = readingItems;
        PublishReadings();
        InitializeLibrary(settings);
        ready = true;
        Loaded += (_, _) => RestartContactIndexing();
        ApplySelection();
        Render();
        if (seeded)
            SaveNow();
    }

    private static LocalSettings ReadStoredSettings()
    {
        try { return LocalSettings.Load(); }
        catch (Exception) { return new LocalSettings(); }
    }

    // ---------------------------------------------------------------- status

    /// <summary>Runs the pill's breathing light only while the bridge is listening.</summary>
    private void SetPillBreathing(bool live)
    {
        if (live == (pillBreath != null))
            return;
        if (!live)
        {
            pillBreath?.Stop();
            pillBreath = null;
            // Dropping the animations restores the painted values: dot fully lit, halo off.
            StatusPillDot.BeginAnimation(OpacityProperty, null);
            StatusPillHalo.BeginAnimation(OpacityProperty, null);
            PillDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PillDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PillHaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PillHaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            return;
        }
        pillBreath = BreathingLight.Create(StatusPillDot, PillDotScale, StatusPillHalo, PillHaloScale);
        pillBreath.Begin();
    }

    private void Render()
    {
        var state = controller.Status;
        var active = state.State is BridgeState.Starting or BridgeState.Running or BridgeState.Stopping;
        if (!active && source != null)
        {
            source = null;
            livePlayer = null;
            Results.Items.Clear();
        }

        StatusTitle.Text = state.State switch
        {
            BridgeState.Starting => "正在准备",
            BridgeState.Running => "正在监听",
            BridgeState.Stopping => "正在结束",
            BridgeState.Error => "出了点问题",
            _ => "未开始"
        };
        StatusDetail.Text = state.State switch
        {
            BridgeState.Error => state.Error ?? "请重试，或查看高级设置。",
            BridgeState.Starting => "正在连接微信…",
            BridgeState.Stopping => "正在停止…",
            BridgeState.Running => state.Activity == "等待新消息" ? "对方发来新消息时会自动朗读。" : state.Activity,
            _ => ""
        };

        StatusDetail.Visibility = string.IsNullOrWhiteSpace(StatusDetail.Text) ? Visibility.Collapsed : Visibility.Visible;

        (StatusPillText.Text, StatusPillText.Foreground, StatusPillDot.Fill, StatusPill.Background) = state.State switch
        {
            BridgeState.Running => ("正在监听", LiveDot, LiveDot, LiveFill),
            BridgeState.Starting or BridgeState.Stopping => ("正在准备", WarnDot, WarnDot, WarnFill),
            BridgeState.Error => ("已停止", ErrorDot, ErrorDot, ErrorFill),
            _ => ("未开始", SecondaryBrush, IdleDot, IdleFill)
        };
        // Only a live connection breathes; every other state holds the dot still.
        SetPillBreathing(state.State == BridgeState.Running);

        StartButton.Content = state.State switch
        {
            BridgeState.Starting or BridgeState.Running => "结束",
            BridgeState.Stopping => "正在结束",
            BridgeState.Error => "重新开始",
            _ => "开始"
        };

        if (state.State == BridgeState.Running && !setupCompleted)
        {
            setupCompleted = true;
            SaveNow();
        }
        ApplySelection();
        UpdateInteractionRestrictions(active);
        StartButton.IsEnabled = !closing && state.State != BridgeState.Stopping;
        PreviewButton.Content = manualCancel != null ? "取消试听" : "试听最后一条消息";
        PreviewButton.IsEnabled = !active && !closing && !string.IsNullOrWhiteSpace(selectedId);
        var noResults = Results.Items.Count == 0;
        ResultsBox.Visibility = noResults ? Visibility.Collapsed : Visibility.Visible;
        ResultsEmpty.Visibility = noResults ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.IsEnabled = state.State == BridgeState.Running;
        ClearButton.IsEnabled = state.State == BridgeState.Running;
        DeleteVoiceButton.IsEnabled = VoicePresetBox.SelectedItem is string voice &&
            userVoices.Any(item => item.Name == voice);
        DeleteKeyButton.IsEnabled = userKeys.Count > 0 && (!active || userKeys.Count > 1);

        trayStop.Enabled = active || manualCancel != null;
        tray.Text = "微信语音桥 · " + StatusTitle.Text;
    }

    private void AddResult(string line)
    {
        Results.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}   {line}");
        while (Results.Items.Count > 40)
            Results.Items.RemoveAt(Results.Items.Count - 1);
        ResultsBox.Visibility = Visibility.Visible;
        ResultsEmpty.Visibility = Visibility.Collapsed;
    }

    private string? Friendly(ProcessingEvent item) => item.Stage switch
    {
        "文件通知 → 接收" => null,
        "查询请求 → 接收" => null,
        "已接收" => (contactCards.FirstOrDefault(card => card.Profile.ConversationId == item.ConversationId)?.Name ?? "对方")
            + (item.Preview == null || PreviewBox.IsChecked != true ? "发来一条新消息" : "：" + item.Preview),
        "收到首个有效音频帧" => "正在合成…",
        "已向音频设备提交播放" => "正在播放…",
        "播放完成" => "已朗读",
        "已清空待播队列" => "已清空待播",
        "跳过超长消息" => "这条消息太长，已跳过。",
        "队列已满，跳过新消息" => "待播的太多了，已跳过一条。",
        _ => item.Stage
    };

    // -------------------------------------------------------------- contact

    private void ApplySelection()
    {
        SelectedCard.Visibility = Visibility.Collapsed;
        if (!libraryReady) return;
        ContactPickerGroup.Visibility = pickingContact ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = pickingContact ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.Visibility = Visibility.Collapsed;
        UpdateHistoryVisibility();
    }

    private void SelectContact(ContactEntry contact) => AddSelectedContact(contact);

    private void ChangeContactClick(object sender, RoutedEventArgs e)
    {
        if (ListeningActive)
            return;
        pickingContact = true;
        ApplySelection();
        ContactSearch.Focus();
        ContactSearch.SelectAll();
    }

    private void ContactSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ContactSearch.Text))
        {
            searchTimer.Stop();
            searchCancel?.Cancel();
            ContactResults.ItemsSource = null;
            ResultsPanel.Visibility = Visibility.Collapsed;
            ContactHint.Text = "";
            return;
        }
        searchTimer.Stop();
        searchTimer.Start();
    }

    private async void SearchTimerTick(object? sender, EventArgs e)
    {
        searchTimer.Stop();
        await SearchContactsAsync();
    }

    private bool contactSearchRunning;
    private async Task SearchContactsAsync()
    {
        if (ListeningActive || closing)
            return;
        if (contactSearchRunning) { searchTimer.Start(); return; }
        var keyword = ContactSearch.Text.Trim();
        if (keyword.Length == 0)
            return;
        contactSearchRunning = true;
        searchCancel?.Cancel();
        searchCancel?.Dispose();
        searchCancel = new CancellationTokenSource();
        var token = searchCancel.Token;
        ContactHint.Text = "正在查找…";
        try
        {
            var session = BindWeChat();
            if (indexCancellation == null) RestartContactIndexing();
            var contacts = await WithDirectoryAsync(session, (reader, cancellation) =>
                reader.SearchAsync(session, keyword, cancellation), token);
            if (token.IsCancellationRequested || ContactSearch.Text.Trim() != keyword)
                return;
            ContactResults.ItemsSource = contacts.Select(contact => CardFor(new ContactProfile
            {
                AccountId = AccountOf(), ConversationId = contact.Id, Name = contact.Name,
                Alias = contact.Wechat, AvatarUrl = contact.AvatarUrl
            })).ToArray();
            if (ContactResults.ItemsSource is ContactCard[] cards)
                foreach (var card in cards) _ = FetchAvatarAsync(card);
            foreach (var contact in contacts)
            {
                var selected = contactCards.FirstOrDefault(card => card.Profile.ConversationId == contact.Id);
                if (selected == null) continue;
                selected.UpdateIdentity(contact.Name, contact.Wechat, contact.AvatarUrl);
                _ = FetchAvatarAsync(selected);
            }
            ResultsPanel.Visibility = contacts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ContactHint.Text = contacts.Count > 0
                ? $"{contacts.Count} 个匹配项"
                : "没有找到匹配的人。";
        }
        catch (OperationCanceledException) { }
        catch (BridgeException error)
        {
            if (token.IsCancellationRequested || ContactSearch.Text.Trim() != keyword) return;
            ResultsPanel.Visibility = Visibility.Collapsed;
            ContactHint.Text = error.Message;
        }
        catch (Exception)
        {
            if (token.IsCancellationRequested || ContactSearch.Text.Trim() != keyword) return;
            ResultsPanel.Visibility = Visibility.Collapsed;
            ContactHint.Text = "暂时无法读取微信联系人。";
        }
        finally
        {
            contactSearchRunning = false;
            if (!closing && !ListeningActive && pickingContact && ContactSearch.Text.Trim() != keyword && ContactSearch.Text.Trim().Length > 0)
            { searchTimer.Stop(); searchTimer.Start(); }
        }
    }

    private void ContactResultsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ListeningActive && ContactResults.SelectedItem is ContactCard card)
            SelectContact(new(card.Profile.ConversationId, card.Profile.Name, card.Profile.Alias, card.Profile.AvatarUrl));
    }

    private void ContactSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ListeningActive) return;
        e.Handled = true;
        var text = ContactSearch.Text.Trim();
        if (text.Length > 0) RememberSearch(text);
        var contacts = ContactResults.Items.Cast<object>().OfType<ContactCard>().ToArray();
        var match = contacts.Length == 1 ? contacts[0] : contacts.FirstOrDefault(card =>
            string.Equals(card.Profile.ConversationId, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(card.Profile.Alias, text, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            SelectContact(new(match.Profile.ConversationId, match.Profile.Name, match.Profile.Alias, match.Profile.AvatarUrl));
        else
            ContactHint.Text = "请从匹配列表或最近使用中选择联系人，身份未确认的对象不会加入监听。";
    }

    private WeChatSession BindWeChat()
    {
        var root = RootBox.Text.Trim();
        var account = AccountOf();
        if (root.Length == 0 || account.Length == 0)
            throw new BridgeException("请先在高级设置里填写微信数据目录和账号目录。");
        try { return WeChatSession.Bind(root, account); }
        catch (BridgeException error) when (VersionGate.IsUnsupported(error.Message))
        {
            // A client started after the app (or upgraded meanwhile) must also get
            // the visible gate, not just a line of status text.
            VersionGate.ShowFor(this, error.Message);
            throw;
        }
    }

    // ------------------------------------------------------------ directory

    /// <summary>The account folder that is chosen right now.</summary>
    private string AccountOf() => (AccountBox.SelectedValue as string ?? "").Trim();

    /// <summary>
    /// List the account folders under the data root. A folder counts as an
    /// account when it holds the message database. One account needs no
    /// choice, so the row stays out of the way; several accounts show a drop
    /// down with the name that was given to each of them.
    /// </summary>
    private void RefreshAccounts(string? select = null)
    {
        var root = RootBox.Text.Trim();
        var found = new List<string>();
        if (Directory.Exists(root))
        {
            try
            {
                found = Directory.EnumerateDirectories(root)
                    .Where(directory => Directory.Exists(Path.Combine(directory, "db_storage")))
                    .Select(directory => Path.GetFileName(directory) ?? "")
                    .Where(name => name.Length > 0)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception) { found = new List<string>(); }
        }
        var target = select ?? AccountOf();
        if (!found.Contains(target) && found.Count > 0)
            target = found[0];
        if (found.Count == 0 && !string.IsNullOrWhiteSpace(target))
            found.Add(target);
        AccountBox.ItemsSource = found
            .Select(name => new AccountChoice(name, AccountLabelOf(name)))
            .ToArray();
        AccountBox.SelectedValue = found.Contains(target) ? target : found.FirstOrDefault();
        // A single account is filled in on its own, so the row is hidden.
        var several = found.Count > 1;
        AccountRow.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
        AccountRowContent.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The name that an account folder is shown with. WeChat keeps the own
    /// nickname in the client and on the server, so the folder name is all
    /// that can be read here.
    /// </summary>
    private static string AccountLabelOf(string folder)
    {
        // The folder carries the wechat id and a number that marks the
        // instance, so only the id is shown.
        var trimmed = folder;
        var cut = trimmed.LastIndexOf('_');
        if (cut > 0 && cut + 1 < trimmed.Length && trimmed[(cut + 1)..].All(char.IsDigit))
            trimmed = trimmed[..cut];
        return trimmed;
    }

    private void BrowseRootClick(object sender, RoutedEventArgs e)
    {
        var current = RootBox.Text.Trim();
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择微信数据目录", Multiselect = false };
        if (Directory.Exists(current))
            dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) != true)
            return;
        RootBox.Text = dialog.FolderName;
        RefreshAccounts(AccountOf());
        RestartContactIndexing();
        ScheduleSave();
    }

    private void AccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (selectionAccount != AccountOf())
        {
            searchTimer.Stop();
            searchCancel?.Cancel();
            pickingContact = false;
            selectionAccount = AccountOf();
            LoadContactCards();
            ContactSearch.Text = "";
            ContactResults.ItemsSource = null;
            PublishRouting();
            RefreshHistory();
            Render();
        }
        RestartContactIndexing();
        ScheduleSave();
    }

    // ---------------------------------------------------------------- start

    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (controller.Status.State is BridgeState.Starting or BridgeState.Running or BridgeState.Stopping)
        {
            await StopAsync();
            return;
        }
        if (controller.Status.State == BridgeState.Error)
        {
            notice = null;
            controller.Reset();
            Render();
            return;
        }
        if (controller.Status.State != BridgeState.Idle || closing)
            return;
        try
        {
            if (manualCancel != null)
            {
                await StopAsync();
                if (closing || controller.Status.State != BridgeState.Idle) return;
            }
            if (contactCards.Count == 0)
                throw new BridgeException("请先添加要朗读的联系人。");
            PublishRouting();
            var snapshot = Volatile.Read(ref routing);
            foreach (var contact in snapshot.Contacts) _ = SpeechFor(contact.VoiceProfileId, snapshot, contact);
            var settings = ReadSettings();
            var targets = snapshot.Contacts.Select(contact => new Target(contact.AccountId, contact.ConversationId)).ToArray();
            if (targets.Any(target => target.AccountId != settings.WeChatAccount))
                throw new BridgeException("联系人属于另一个微信账号，请重新选择。");
            var wechat = WeChatSession.Bind(settings.WeChatRoot, settings.WeChatAccount);
            var player = new WindowsPlayer(settings.Device, (float)settings.Volume);
            player.Validate();
            var speech = new RoutedSpeech(this);
            var options = new WatchOptions(settings.Watch, settings.Poll, settings.Coalesce, ParseRetries(settings.Retry));
            // The cache never grows past the size that the user chose.
            SessionCache.Trim(settings.CacheLimitBytes, settings.WeChatRoot);
            var fixture = new PythonFixtureSource(options, PythonFixtureSource.ResolvePython(), WorkerScript, wechat,
                currentReadings: () => Volatile.Read(ref activeReadings),
                conversations: targets.Select(target => target.ConversationId).ToArray());
            fixture.Warning += text => Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(source, fixture) && controller.Status.State == BridgeState.Running)
                    AddResult(text);
            });
            fixture.ContactsReady += contacts => Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(source, fixture) || controller.Status.State is not (BridgeState.Starting or BridgeState.Running)) return;
                foreach (var contact in contacts)
                {
                    var card = contactCards.FirstOrDefault(item => item.Profile.ConversationId == contact.Id);
                    if (card == null) continue;
                    card.UpdateIdentity(contact.Name, contact.Wechat, contact.AvatarUrl);
                    _ = FetchAvatarAsync(card);
                }
                ScheduleSave();
            });
            Results.Items.Clear();
            searchTimer.Stop();
            searchCancel?.Cancel();
            notice = null;
            source = fixture;
            livePlayer = player;
            foreach (var contact in snapshot.Contacts) RememberContact(contact);
            SaveNow();
            controller.Start(targets, true, settings.Capacity, settings.Preview, () => new SessionSource(this, fixture), speech, player,
                fixture.RenderSpeechAsync,
                // Removing a contact while listening only narrows the routing snapshot.
                // Keep the worker baseline, but stop admitting messages from peers no longer on the list.
                message => Volatile.Read(ref routing).Contacts.Any(contact =>
                    contact.AccountId == message.AccountId && contact.ConversationId == message.ConversationId));
        }
        catch (BridgeException error)
        {
            notice = error.Message;
            if (VersionGate.IsUnsupported(error.Message)) VersionGate.ShowFor(this, error.Message);
        }
        catch (Exception) { notice = "无法开始，请检查高级设置。"; }
        Render();
    }

    private async Task StopAsync()
    {
        manualCancel?.Cancel();
        manualPlayer?.Stop();
        var stop = controller.EndAsync();
        var indexStop = StopContactIndexingAsync();
        Render();
        await Task.WhenAll(stop, indexStop);
        if (manualTask != null)
            await manualTask;
        searchCancel?.Cancel();
        await CloseDirectoryAsync();
        Render();
    }

    private void SkipClick(object sender, RoutedEventArgs e) => controller.SkipCurrent();

    private void ClearClick(object sender, RoutedEventArgs e) => controller.ClearQueue();

    // -------------------------------------------------------------- preview

    private void PreviewClick(object sender, RoutedEventArgs e)
    {
        if (ListeningActive || closing) return;
        if (manualCancel != null)
        {
            manualCancel.Cancel();
            manualPlayer?.Stop();
            return;
        }
        try
        {
            var settings = ReadSettings();
            PublishRouting();
            speechFactory.ResetFailures();
            var contact = contactCards.FirstOrDefault(card => card.Profile.ConversationId == selectedId)
                ?? throw new BridgeException("请选择要试听的联系人。");
            var speech = SpeechFor(contact.Profile.VoiceProfileId, Volatile.Read(ref routing), contact.Profile);
            var player = new WindowsPlayer(settings.Device, (float)settings.Volume);
            player.Validate();
            var wechat = BindWeChat();
            searchTimer.Stop();
            searchCancel?.Cancel();
            manualCancel = new();
            manualPreviewContact = contact.Profile.ConversationId;
            manualPlayer = player;
            notice = "正在读取所选联系人的最后一条消息，点击试听可取消。";
            manualTask = PreviewOperationAsync(wechat, contact.Profile, speech, player, manualCancel.Token);
        }
        catch (BridgeException error) { notice = error.Message; }
        catch (Exception) { notice = "无法开始试听，请检查联系人和语音服务配置。"; }
        Render();
    }

    private async Task PreviewOperationAsync(WeChatSession wechat, ContactProfile contact,
        ISpeech speech, WindowsPlayer player, CancellationToken token)
    {
        // Assign manualTask before a synchronously failing operation can clear it.
        await Task.Yield();
        try
        {
            var text = await WithDirectoryAsync(wechat, (reader, cancellation) =>
                reader.LastMessageAsync(wechat, contact.ConversationId, cancellation), token);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
            {
                notice = "这个联系人暂时没有可试听的消息。";
                return;
            }
            // The preview reads the raw last message, so render both the body and the
            // announcement here; listening does the same through the worker.
            var rules = Volatile.Read(ref activeReadings);
            var body = rules.Length == 0 ? text : ReadingText.Apply(text, rules);
            await SpeakAsync(speech, player, contact.Prepare(body, contactCards.Count,
                rules.Length == 0 ? null : prefix => ReadingText.Apply(prefix, rules, ignoreScope: true)),
                token, completeManual: false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { notice = "试听已取消。"; }
        catch (BridgeException error) { notice = error.Message; }
        catch (Exception) { notice = "读取试听消息失败。"; }
        finally
        {
            manualCancel?.Dispose();
            manualCancel = null;
            manualPreviewContact = null;
            manualPlayer = null;
            manualTask = null;
            Render();
        }
    }

    private void TestFishClick(object sender, RoutedEventArgs e)
    {
        if (manualCancel != null || ListeningActive || closing)
            return;
        var text = TestText.Text.Trim();
        if (text.Length == 0)
        {
            notice = "请输入要试听的文字。";
            Render();
            return;
        }
        try
        {
            var settings = ReadSettings();
            var speech = CreateFish(settings);
            var player = new WindowsPlayer(settings.Device, (float)settings.Volume);
            player.Validate();
            manualPlayer = player;
            manualCancel = new();
            notice = "正在合成…";
            Render();
            manualTask = SpeakAsync(speech, player, text, manualCancel.Token);
        }
        catch (BridgeException error) { notice = error.Message; }
        catch (Exception) { notice = "试听配置有误。"; }
        Render();
    }

    private async Task SpeakAsync(ISpeech speech, WindowsPlayer player, string text, CancellationToken token,
        bool completeManual = true)
    {
        await Task.Yield();
        try
        {
            var audio = await speech.SynthesizeAsync(text, () => { }, token);
            token.ThrowIfCancellationRequested();
            await player.PlayAsync(audio, play =>
            {
                if (token.IsCancellationRequested)
                    return false;
                play();
                return true;
            }, token);
            notice = "试听完成。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { notice = "试听已取消。"; }
        catch (BridgeException error) { notice = error.Message; }
        catch (Exception) { notice = "试听遇到异常。"; }
        finally
        {
            if (completeManual)
            {
                manualCancel?.Dispose();
                manualCancel = null;
                manualPlayer = null;
                manualTask = null;
                Render();
            }
        }
    }

    // ------------------------------------------------------------- settings

    private ISpeech CreateFish(LocalSettings settings, bool followKey = false)
    {
        _ = settings;
        _ = followKey;
        PublishRouting();
        speechFactory.ResetFailures();
        return SpeechFor(TestVoiceBox.SelectedValue as string ?? "", Volatile.Read(ref routing));
    }

    private LocalSettings ReadSettings()
    {
        var stored = ReadStoredSettings();
        return stored with
        {
            Voice = selectedVoiceId,
            VoiceLibrary = library.ToList(),
            Providers = providers.ToList(),
            ContactProfiles = contactPreferences.ToList(),
            SelectedContacts = contactCards.Select(card => card.Profile.ConversationId).ToList(),
            ContactHistory = contactHistory.ToList(),
            SearchHistory = searchHistory.ToList(),
            SetupCompleted = setupCompleted,
            TestVoiceProfileId = TestVoiceBox.SelectedValue as string ?? "",
            FishKeyName = selectedKeyName,
            Latency = LatencyBox.SelectedItem as string ?? "balanced",
            Device = DeviceBox.SelectedValue as string ?? "",
            Volume = VolumeBox.Value,
            Preview = PreviewBox.IsChecked == true,
            RealWeChat = true,
            FishModel = fishModel,
            WeChatRoot = RootBox.Text.Trim(),
            WeChatAccount = AccountOf(),
            WeChatConversation = selectedId ?? "",
            WeChatLabel = selectedName ?? "",
            WeChatAlias = selectedWechat ?? "",
            Voices = userVoices.ToList(),
            Rules = CompleteReadings(),
            Lexicon = new List<WordEntry>(),
            LexiconSeeded = lexiconSeeded,
            CacheLimitBytes = CacheLimitBox.SelectedValue is long limit && limit > 0
                ? limit : CacheLimits.DefaultBytes
        };
    }

    private static int[] ParseRetries(string value)
    {
        var tokens = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 5 || tokens.Any(part => !int.TryParse(part, out var ms) || ms is < 10 or > 1000))
            throw new BridgeException("短重试支持最多五个 10–1000 毫秒的间隔。");
        return tokens.Select(int.Parse).ToArray();
    }

    private void ScheduleSave()
    {
        if (!ready)
            return;
        PublishRouting();
        saveTimer.Stop();
        saveTimer.Start();
    }

    /// <summary>Writes the settings right away, so a choice is never lost.</summary>
    private void SaveNow()
    {
        if (!ready)
            return;
        saveTimer.Stop();
        PublishRouting();
        try { ReadSettings().Save(); }
        catch (Exception) { /* The explicit save reports the failure. */ }
    }

    private void SaveTimerTick(object? sender, EventArgs e)
    {
        saveTimer.Stop();
        try { ReadSettings().Save(); }
        catch (Exception) { /* The explicit save reports the failure. */ }
    }

    private void ModelChanged(object sender, RoutedEventArgs e)
    {
        if (!ready)
            return;
        Volatile.Write(ref fishModel, PaidModel.IsChecked == true ? FishSpeech.PaidModel : FishSpeech.Model);
        ModelHint.Text = fishModel == FishSpeech.PaidModel
            ? "Drama 3 按量付费，需要账户有余额。"
            : "免费模型适合日常使用。";
        ScheduleSave();
    }

    private void DeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready)
            return;
        livePlayer?.SelectDevice(DeviceBox.SelectedValue as string ?? "");
        manualPlayer?.SelectDevice(DeviceBox.SelectedValue as string ?? "");
        ScheduleSave();
    }

    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready) return;
        livePlayer?.SetVolume((float)VolumeBox.Value);
        manualPlayer?.SetVolume((float)VolumeBox.Value);
        ScheduleSave();
    }

    private void RefreshVoices(string? select = null)
    {
        // Only voices the user named themselves are listed; the old factory presets
        // are gone from both the voice library and this legacy dropdown.
        var names = userVoices.Select(item => item.Name).ToArray();
        VoicePresetBox.ItemsSource = names;
        // A name picks the entry that carries it, an id finds the entry of a voice.
        var index = select == null
            ? Array.FindIndex(names, name => VoiceIdOf(name) == selectedVoiceId)
            : Array.FindIndex(names, name => name == select);
        if (index < 0)
        {
            index = names.Length > 0 ? 0 : -1;
            if (names.Length > 0)
                selectedVoiceId = VoiceIdOf(names[0]);
        }
        VoicePresetBox.SelectedIndex = index;
    }

    private string VoiceIdOf(string name) =>
        userVoices.FirstOrDefault(item => item.Name == name)?.Id ?? "";

    private void VoicePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || VoicePresetBox.SelectedItem is not string name)
            return;
        var id = VoiceIdOf(name);
        if (id.Length == 0)
            return;
        selectedVoiceId = id;
        DeleteVoiceButton.IsEnabled = userVoices.Any(item => item.Name == name);
        // The chosen voice is kept at once, the next start speaks with it.
        SaveNow();
    }

    private void AddVoiceClick(object sender, RoutedEventArgs e) => AddLibraryVoiceClick(sender, e);
    private void DeleteVoiceClick(object sender, RoutedEventArgs e) => DeleteLibraryVoiceClick(sender, e);

    private void RefreshKeys(string? select = null)
    {
        var target = select ?? selectedKeyName;
        var item = userKeys.FirstOrDefault(item => item.Name == target);
        SelectKey(item?.Name ?? "");
        KeyChoiceBox.ItemsSource = userKeys.Select(key => key.Name).ToArray();
        KeyChoiceBox.SelectedItem = item?.Name;
    }

    private void SelectKey(string name)
    {
        selectedKeyName = name;
        Volatile.Write(ref activeApiKey, KeyStore.Find(userKeys, name) ?? "");
    }

    private void KeyChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || KeyChoiceBox.SelectedItem is not string name || name == selectedKeyName)
            return;
        SelectKey(name);
        KeyNotice.Text = ListeningActive ? "已切换 API Key，从下一条开始生效。" : "已切换 API Key。";
        ScheduleSave();
    }

    private void AddKeyClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddItemDialog("添加 API Key", "名称", "API Key", true) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (!userKeys.Any(item => item.Name == dialog.ItemName))
            sessionNewKeys.Add(dialog.ItemName);
        var updated = userKeys.Where(item => item.Name != dialog.ItemName)
            .Append(new NamedKey(dialog.ItemName, dialog.ItemValue)).ToList();
        SaveKeys(updated, dialog.ItemName);
    }

    private void DeleteKeyClick(object sender, RoutedEventArgs e)
    {
        if (userKeys.Count == 0 || (userKeys.Count == 1 && ListeningActive))
            return;
        var key = userKeys.FirstOrDefault(item => item.Name == selectedKeyName);
        if (key == null)
            return;
        if (providers.Any(provider => provider.CredentialId == key.Name))
        {
            KeyNotice.Text = "这份凭据仍被某个服务引用，请先编辑或删除对应 API。";
            return;
        }
        sessionNewKeys.Remove(key.Name);
        var updated = userKeys.Where(item => item != key).ToList();
        SaveKeys(updated, updated.FirstOrDefault()?.Name);
    }

    private void SaveKeys(List<NamedKey> updated, string? selected)
    {
        try { KeyStore.Save(updated); }
        catch (Exception)
        {
            KeyNotice.Text = "保存 API Key 失败，请检查本地配置目录权限。";
            return;
        }
        userKeys.Clear();
        userKeys.AddRange(updated);
        speechFactory.ResetFailures();
        RefreshKeys(selected);
        KeyNotice.Text = ListeningActive ? "API Key 已更新，从下一条开始生效。" : "API Key 已更新。";
        ScheduleSave();
        Render();
    }

    /// <summary>
    /// Credential names held in per-contact or test-voice override settings.
    /// Today's overrides (model, latency) never name a credential and the test
    /// voice picks one through the FishKeyName parameter, so this collects
    /// nothing; it stays as the single wiring point if either settings family
    /// ever gains a credential override of its own.
    /// </summary>
    private static IEnumerable<string> ContactOrTestCredentialOverrides(IEnumerable<ContactProfile>? contacts)
    {
        _ = contacts;
        return Array.Empty<string>();
    }

    /// <summary>
    /// Removes secure-store entries that nothing references any more. Called
    /// once after startup migration and again when a service is deleted; the
    /// caller's lists are the post-migration truth at startup and the live
    /// ones afterwards. Only unreferenced entries go — a name that any service
    /// pointer, the FishKeyName or a contact/test override still holds is kept
    /// whatever else happens — and entries created during this run are exempt.
    /// </summary>
    private void SweepCredentials(string when,
        IEnumerable<SpeechProviderProfile?>? currentProviders, string? keyName,
        IEnumerable<ContactProfile>? contacts = null)
    {
        var referenced = CredentialSweep.ReferencedNames(currentProviders, keyName,
            ContactOrTestCredentialOverrides(contacts));
        var stale = CredentialSweep.UnreferencedEntries(userKeys.Select(key => key.Name),
            referenced, sessionNewKeys);
        if (stale.Count == 0)
            return;
        var kept = userKeys.Where(key => !stale.Contains(key.Name)).ToList();
        try { KeyStore.Save(kept); }
        catch (Exception) { return; }
        userKeys.Clear();
        userKeys.AddRange(kept);
        foreach (var name in stale)
            sessionNewKeys.Remove(name);
        AppLog.Write($"凭据清扫（{when}）：删除 {stale.Count} 条无引用条目。");
        if (ready)
            RefreshKeys();
    }

    /// <summary>
    /// The plus at the bottom of the word book. It adds an empty row and lets
    /// the user fill it in.
    /// </summary>
    private void AddReadingClick(object sender, RoutedEventArgs e)
    {
        var item = new ReadingItem(new ReadingRule("", "", true) { Scope = "both" }, isDraft: true);
        readingItems.Add(item);
        ShowReadingNotice(null);
        FocusReading(item);
    }

    /// <summary>
    /// The edit button of a row. The row stays plain text until the button is
    /// pressed, and it is checked over again when the row is done.
    /// </summary>
    private void EditReadingClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ReadingItem item)
            return;
        if (item.IsEditing)
        {
            CommitReading(item);
            return;
        }
        item.IsEditing = true;
        ShowReadingNotice(null);
        FocusReading(item, editPronunciation: true);
    }

    /// <summary>
    /// A row that is done. A row that was never filled in is taken away
    /// again, and a row that is only half filled stays open.
    /// </summary>
    private void CommitReading(ReadingItem item)
    {
        var match = item.Match.Trim();
        var say = item.Say.Trim();
        if (match.Length == 0 && say.Length == 0)
        {
            if (item.Committed == null)
                RemoveReading(item);
            else
                ShowReadingNotice("原文和读作都要填上。");
            return;
        }
        if (match.Length == 0 || say.Length == 0)
        {
            ShowReadingNotice("原文和读作都要填上。");
            return;
        }
        if (readingItems.Any(row => row != item && row.Committed?.Match == match))
        {
            ShowReadingNotice("这条原文已经加过了。");
            return;
        }
        var rule = item.Rule with { Match = match, Say = say };
        if (!PythonFixtureSource.CanSendReadings(readingItems.Select(row => row == item ? rule : row.Committed)
            .OfType<ReadingRule>()))
        {
            ShowReadingNotice("词库太大，请删减词条后再保存。");
            return;
        }
        item.Commit(rule);
        PublishReadings();
        ShowReadingNotice(ListeningActive ? "词库已更新，从下一条开始生效。" : null);
        ScheduleSave();
    }

    private void RemoveReading(ReadingItem item)
    {
        if (!readingItems.Remove(item))
            return;
        PublishReadings();
        ShowReadingNotice(ListeningActive && item.Committed != null ? "词库已更新，从下一条开始生效。" : null);
        ScheduleSave();
    }

    private void DeleteReadingClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ReadingItem item)
            return;
        RemoveReading(item);
    }

    /// <summary>Word-book messages use the same single chip as everything else.</summary>
    private void ShowReadingNotice(string? text) => notice = text;

    /// <summary>New rows start with the source; editing starts at the end of the pronunciation.</summary>
    private void FocusReading(ReadingItem item, bool editPronunciation = false)
    {
        ReadingList.ScrollIntoView(item);
        var name = editPronunciation ? "SayBox" : "MatchBox";
        var attempts = 0;
        void TryFocus()
        {
            if (!item.IsEditing || ReadingList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
                return;
            // Find the box in the visual tree, not through ContentTemplate.FindName:
            // ListBox supplies the template through ItemTemplate, so the presenter's
            // own ContentTemplate is often null and the old lookup always failed.
            var box = Descendants(container).OfType<TextBox>().FirstOrDefault(candidate => candidate.Name == name);
            if (box is null)
            {
                if (attempts++ < 3)
                    ReadingList.Dispatcher.BeginInvoke(new Action(TryFocus), System.Windows.Threading.DispatcherPriority.Input);
                return;
            }
            if (!box.IsVisible)
            {
                // IsEditing flips the boxes from Collapsed to Visible in a later layout pass.
                void OnVisible(object? sender, DependencyPropertyChangedEventArgs args)
                {
                    // A recycled ListBox container can become visible for another row.
                    if (!item.IsEditing) { box.IsVisibleChanged -= OnVisible; return; }
                    if (!box.IsVisible) return;
                    box.IsVisibleChanged -= OnVisible;
                    ReadingList.Dispatcher.BeginInvoke(new Action(TryFocus), System.Windows.Threading.DispatcherPriority.Input);
                }
                box.IsVisibleChanged += OnVisible;
                return;
            }
            Keyboard.Focus(box);
            box.Select(box.Text.Length, 0);
        }
        ReadingList.Dispatcher.BeginInvoke(new Action(TryFocus), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The rows that are ready to be used. A row that is still being filled in
    /// is left out, so it never reaches the worker or the settings file.
    /// </summary>
    private void PublishReadings() => Volatile.Write(ref activeReadings,
        readingItems.Select(item => item.Committed).OfType<ReadingRule>().ToArray());

    private List<ReadingRule> CompleteReadings() => Volatile.Read(ref activeReadings).ToList();

    private void SaveClick(object sender, RoutedEventArgs e) => ApplySettings(() =>
    {
        ReadSettings().Save();
        RestartContactIndexing();
        return "设置已保存。";
    });

    private void CleanClick(object sender, RoutedEventArgs e) => ApplySettings(() =>
    {
        var settings = ReadSettings();
        var removed = SessionCache.CleanResiduals(settings.WeChatRoot);
        avatars.Clear();
        foreach (var card in contactCards) card.Avatar = null;
        RefreshHistory();
        SessionCache.Trim(settings.CacheLimitBytes, settings.WeChatRoot);
        UpdateCacheInfo();
        return removed > 0
            ? $"已清理 {removed} 个残留缓存目录，当前占用 {CacheSizeText()}。"
            : $"缓存是干净的，当前占用 {CacheSizeText()}。";
    });

    /// <summary>Shows how much room the copies of the databases take.</summary>
    private void UpdateCacheInfo()
    {
        CacheInfo.Text = "当前缓存占用 " + CacheSizeText() + "，超过上限会自动清理，退出时也会清空。";
    }

    private string CacheSizeText()
    {
        var bytes = SessionCache.Measure(ReadStoredSettings().WeChatRoot);
        return bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024L / 1024 / 1024} GB"
            : $"{Math.Max(1, bytes / 1024 / 1024)} MB";
    }

    private void ApplySettings(Func<string> operation)
    {
        try { SettingsNotice.Text = operation(); }
        catch (BridgeException error) { SettingsNotice.Text = error.Message; }
        catch (Exception) { SettingsNotice.Text = "操作失败，请检查本地配置目录权限。"; }
        Render();
    }

    private void RefreshDevices(string selected)
    {
        try { DeviceBox.ItemsSource = WindowsPlayer.Devices(); }
        catch (Exception) { DeviceBox.ItemsSource = new[] { new OutputDevice("", "Windows 默认输出") }; }
        DeviceBox.SelectedValue = selected;
        if (DeviceBox.SelectedIndex < 0)
        {
            var devices = DeviceBox.Items.Cast<OutputDevice>()
                .Append(new(selected, "已保存的输出设备（当前不可用）"));
            DeviceBox.ItemsSource = devices.ToArray();
            DeviceBox.SelectedValue = selected;
        }
    }

    // ---------------------------------------------------------------- window

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void WindowStateChanged(object? sender, EventArgs e)
    {
        // Minimizing keeps the button in the task bar. The window is not
        // hidden and does not slip away into the overflow of the tray.
        bool maximized = WindowState == WindowState.Maximized;
        UpdateCaptionGlyphs(maximized);
    }

    // The two caption glyphs live in the button template, so they are looked up
    // by name instead of being generated fields.
    private void UpdateCaptionGlyphs(bool maximized)
    {
        if (MaximizeButton.Template is not ControlTemplate template)
            return;
        if (template.FindName("MaxGlyph", MaximizeButton) is FrameworkElement max)
            max.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        if (template.FindName("RestoreGlyph", MaximizeButton) is FrameworkElement restore)
            restore.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
    }

    // --------------------------------------------------------- caption bar

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// The caption bar has no system frame, so it moves the window itself.
    /// A double click toggles the maximized state like a normal title bar.
    /// </summary>
    private void CaptionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || InsideButton(e.OriginalSource as DependencyObject))
            return;
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (WindowState == WindowState.Maximized)
            return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse was released before the move started.
        }
    }

    private static bool InsideButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button)
                return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : null;
        }
        return false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // A self-drawn caption bar lets the window fill the monitor work area
        // when it is maximized instead of sliding under the taskbar.
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WindowMessage);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                limits.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                limits.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                limits.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                limits.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                Marshal.StructureToPtr(limits, lParam, true);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
            return;
        e.Cancel = true;
        if (closing)
            return;
        closing = true;
        try
        {
            var updatesClosed = CloseUpdatesAsync();
            if (!updateHandoff)
            {
                // Keep the last choice, even when the window is closed at once.
                SaveNow();
                await StopAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }
            await updatesClosed;
            await controller.DisposeAsync();
            await CloseDirectoryAsync();
            // The copies of the databases are not needed any more, so the
            // cache is emptied when the app goes away.
            if (!updateHandoff)
                SessionCache.CleanResiduals(ReadStoredSettings().WeChatRoot);
        }
        catch (Exception)
        {
            // Closing the process releases the Worker Job handle even after a hung audio driver.
            if (!updateHandoff)
                Environment.Exit(1);
        }
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        trayIconStream?.Dispose();
        trayIconStream = null;
        allowClose = true;
        await Dispatcher.InvokeAsync(Close);
    }
}
