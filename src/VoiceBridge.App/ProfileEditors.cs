using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;

namespace VoiceBridge.App;

internal abstract class ProfileEditor : Window
{
    protected readonly StackPanel Fields = new();
    protected readonly StackPanel Choice = new();
    protected readonly TextBlock Error = new() { Margin = new(0, 12, 0, 0) };
    private readonly Button save, back;
    private StackPanel? target;
    private bool choosing;
    protected ProfileEditor(string title, bool isNew)
    {
        Title = title; Width = 500; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 60);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; FontFamily = new("Microsoft YaHei UI"); FontSize = 14;
        var grid = new Grid { Margin = new(28,8,28,24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,8) });
        var pages = new Grid(); pages.Children.Add(Choice); pages.Children.Add(Fields);
        var scroll = new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(120, MaxHeight - 250) };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var footer = new StackPanel(); Grid.SetRow(footer, 2);
        Error.Foreground = (System.Windows.Media.Brush)FindResource("Danger"); Error.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(Error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0,16,0,0) };
        back = new Button { Content = "上一步", Style = (Style)FindResource("PlainButton"), Margin = new(0,0,10,0) };
        back.Click += (_, _) => ShowPage(true);
        var cancel = new Button { Content = "取消", IsCancel = true, Style = (Style)FindResource("SecondaryButton") };
        cancel.Click += (_, _) => DialogResult = false;
        save = new Button { Content = "下一步", IsDefault = true, Margin = new(10,0,0,0), Style = (Style)FindResource("PrimaryButton") };
        save.Click += (_, _) =>
        {
            try { if (choosing) { BuildDetails(); ShowPage(false); } else { Save(); DialogResult = true; } }
            catch (BridgeException error) { Error.Text = error.Message; }
        };
        buttons.Children.Add(back); buttons.Children.Add(cancel); buttons.Children.Add(save);
        footer.Children.Add(buttons); grid.Children.Add(footer); DialogShell.Apply(this, grid);
        ShowPage(isNew); if (!isNew) back.Visibility = Visibility.Collapsed;
    }
    private void ShowPage(bool first)
    {
        choosing = first; Error.Text = "";
        Choice.Visibility = first ? Visibility.Visible : Visibility.Collapsed;
        Fields.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
        save.Content = first ? "下一步" : "保存";
        back.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
    }
    protected TextBox Text(string label, string value, int maximum = 512)
    {
        var box = new TextBox { Text = value, MaxLength = maximum, MinWidth = 80 }; Field(label, box); return box;
    }
    protected void Field(string label, UIElement control)
    {
        var panel = target ?? Fields;
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("FieldLabel"), Margin = new(0,12,0,5) });
        panel.Children.Add(control);
    }
    protected void Hint(string text) => (target ?? Fields).Children.Add(new TextBlock { Text = text,
        Style = (Style)FindResource("Footnote"), Margin = new(0,8,0,0) });
    protected void Advanced(Action build)
    {
        var panel = new StackPanel(); target = panel; build(); target = null;
        if (panel.Children.Count > 0)
            Fields.Children.Add(new Expander { Header = "高级", Content = panel, Margin = new(0,18,0,0) });
    }
    protected abstract void BuildDetails();
    protected abstract void Save();
    protected static void Detach(UIElement control)
    {
        if (LogicalTreeHelper.GetParent(control) is Panel panel) panel.Children.Remove(control);
    }
}

internal sealed record ProviderKindChoice(SpeechProviderKind Kind, string Label);
internal sealed record ModelChoice(string Id, string Label);

internal sealed class ProviderEditor : ProfileEditor
{
    private readonly ComboBox kind;
    private SpeechProviderKind? builtKind;
    private TextBox name = null!, endpoint = null!, model = null!, resource = null!, appId = null!, timeout = null!, availableModels = null!;
    private ComboBox latency = null!, format = null!;
    private CheckBox fishFree = null!, fishPaid = null!;
    private readonly PasswordBox secret = new() { MaxLength = 2048 };
    public SpeechProviderProfile Result { get; private set; }
    public string NewSecret => secret.Password;
    public ProviderEditor(SpeechProviderProfile? original) : base(original == null ? "添加语音服务" : "编辑语音服务", original == null)
    {
        Result = original ?? new() { Id = Guid.NewGuid().ToString("N"), Kind = SpeechProviderKind.Fish, Model = FishSpeech.Model };
        kind = new ComboBox { ItemsSource = new[] { SpeechProviderKind.Fish, SpeechProviderKind.OpenAiCompatible,
                SpeechProviderKind.AzureSpeech, SpeechProviderKind.Doubao, SpeechProviderKind.GptSoVits }
                .Select(item => new ProviderKindChoice(item, SpeechProviderFactory.Label(item))).ToArray(),
            DisplayMemberPath = "Label", SelectedValuePath = "Kind", SelectedValue = Result.Kind };
        Choice.Children.Add(new TextBlock { Text = "选择要连接的语音服务", Margin = new(0,12,0,10) }); Choice.Children.Add(kind);
        if (original != null) BuildDetails();
    }
    protected override void BuildDetails()
    {
        if (kind.SelectedValue is not SpeechProviderKind selected) throw new BridgeException("请选择语音服务。");
        if (builtKind == selected) return;
        if (builtKind != null) secret.Password = "";
        builtKind = selected; Detach(secret); Fields.Children.Clear();
        var same = selected == Result.Kind;
        endpoint = new TextBox { Text = same ? Result.Endpoint : SpeechProviderFactory.DefaultEndpoint(selected), MaxLength = 2048 };
        if (string.IsNullOrWhiteSpace(endpoint.Text)) endpoint.Text = SpeechProviderFactory.DefaultEndpoint(selected);
        model = new TextBox { Text = same ? Result.Model : "", MaxLength = 128 };
        resource = new TextBox { Text = same ? Result.ResourceId : "seed-tts-2.0", MaxLength = 256 };
        appId = new TextBox { Text = same ? Result.AppId : "", MaxLength = 128 };
        name = new TextBox { Text = same ? Result.Name : "", MaxLength = 64 };
        availableModels = new TextBox { Text = same ? string.Join(", ", Result.AvailableModels ?? []) : "", MaxLength = 2048 };
        timeout = new TextBox { Text = Result.TimeoutSeconds.ToString(CultureInfo.InvariantCulture), MaxLength = 3 };
        format = new ComboBox { ItemsSource = new[] { "mp3", "wav" }, SelectedItem = Result.AudioFormat };
        latency = new ComboBox { ItemsSource = new[] { new ModelChoice("balanced", "均衡"), new ModelChoice("normal", "音质优先"), new ModelChoice("low", "低延迟") },
            DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = Result.Latency };
        var fishModels = same ? SpeechProviderFactory.FishModels(Result) : new[] { FishSpeech.Model, FishSpeech.PaidModel };
        fishFree = new CheckBox { Content = "S2.1 Pro", Tag = "免费", IsChecked = fishModels.Contains(FishSpeech.Model), Style = (Style)FindResource("ModelSelectionCard") };
        fishPaid = new CheckBox { Content = "Drama 3", Tag = "付费", IsChecked = fishModels.Contains(FishSpeech.PaidModel), Style = (Style)FindResource("ModelSelectionCard") };
        if (selected != SpeechProviderKind.Fish) Field(selected == SpeechProviderKind.AzureSpeech ? "区域或语音合成地址" : "接口地址", endpoint);
        if (selected == SpeechProviderKind.Fish)
        {
            var choices = new StackPanel(); choices.Children.Add(fishFree); choices.Children.Add(fishPaid);
            Field("可用模型", choices);
            Hint("共用一个 Key；首页切换使用，每次只调用一个模型。");
        }
        if (selected == SpeechProviderKind.OpenAiCompatible) Field("模型名称", model);
        if (selected == SpeechProviderKind.Doubao) { Field("资源 ID", resource); Hint("按控制台音色授权填写，例如 seed-tts-2.0 或 seed-icl-2.0。"); }
        if (selected != SpeechProviderKind.GptSoVits)
        {
            Field("API Key", secret);
            Hint(same && Result.CredentialId.Length > 0 ? "留空保留此连接的密钥。" : "只保存在当前 Windows 用户的加密凭据库。");
        }
        Advanced(() =>
        {
            Field("连接名称（可选）", name);
            if (selected == SpeechProviderKind.GptSoVits) Field("API Key（可选）", secret);
            if (selected == SpeechProviderKind.Fish) Field("默认合成模式", latency);
            if (selected == SpeechProviderKind.OpenAiCompatible)
            {
                Field("音频格式", format); Field("其他可切换模型（可选，逗号分隔）", availableModels);
                Hint("只填写此接口、Key 和音色共同支持的模型；保存后可在首页切换。");
            }
            if (selected == SpeechProviderKind.Doubao) { Field("旧版控制台 App ID（新版留空）", appId); Field("模型参数（可选）", model); }
            Field("超时秒数（5–300）", timeout);
        });
    }
    protected override void Save()
    {
        var selected = builtKind ?? throw new BridgeException("请选择服务。");
        var address = selected == SpeechProviderKind.Fish ? SpeechProviderFactory.DefaultEndpoint(selected) : endpoint.Text.Trim();
        if (selected == SpeechProviderKind.AzureSpeech && address.Length > 0 && address.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            address = $"https://{address}.tts.speech.microsoft.com/cognitiveservices/v1";
        if (!int.TryParse(timeout.Text, out var seconds) || seconds is < 5 or > 300) throw new BridgeException("超时秒数请填写 5 至 300。");
        var label = string.IsNullOrWhiteSpace(name.Text) ? SpeechProviderFactory.Label(selected) : name.Text.Trim();
        // Keep the existing service -> credential link. Re-entering a key replaces
        // the value of the entry this connection already points at; only a
        // connection that never had a credential gets a generated name. Minting a
        // fresh "speech-<id>" name on every secret entry used to strand the user's
        // key behind a placeholder entry the service did not point at.
        var key = selected == Result.Kind ? Result.CredentialId : "";
        if (!string.IsNullOrWhiteSpace(NewSecret))
            key = key.Length > 0 ? key : "speech-" + Result.Id;
        var models = selected == SpeechProviderKind.Fish
            ? new[] { fishFree.IsChecked == true ? FishSpeech.Model : "", fishPaid.IsChecked == true ? FishSpeech.PaidModel : "" }.Where(item => item.Length > 0).ToArray()
            : selected == SpeechProviderKind.OpenAiCompatible
                ? new[] { model.Text.Trim() }.Concat(availableModels.Text.Split(new[] { ',', '，', '\n', '\r' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).ToArray()
                : null;
        if (selected == SpeechProviderKind.Fish && models!.Length == 0) throw new BridgeException("请至少保留一个可用模型。");
        var activeModel = selected == SpeechProviderKind.Fish
            ? models!.Contains(Result.Model) ? Result.Model : models![0]
            : model.Text.Trim();
        // Editing a standby key must not silently make it active. Which key is in
        // use is chosen with the "启用" button; adding a key never applies it.
        var candidate = Result with { Kind = selected, Name = label, Endpoint = address,
            Model = activeModel, AvailableModels = models,
            CredentialId = key, ResourceId = resource.Text.Trim(), AppId = appId.Text.Trim(),
            Enabled = Result.Enabled,
            TimeoutSeconds = seconds, Latency = latency.SelectedValue as string ?? "balanced", AudioFormat = format.SelectedItem as string ?? "mp3" };
        _ = SpeechProviderFactory.ResolveEndpoint(candidate);
        if (selected == SpeechProviderKind.OpenAiCompatible && string.IsNullOrWhiteSpace(candidate.Model)) throw new BridgeException("请填写服务支持的模型名称。");
        if (selected == SpeechProviderKind.Doubao && candidate.ResourceId.Length == 0) throw new BridgeException("请填写音色对应的资源 ID。");
        if (SpeechProviderFactory.RequiresCredential(candidate) && key.Length == 0) throw new BridgeException("请填写 API Key。");
        if (!string.IsNullOrWhiteSpace(NewSecret)) SpeechProviderFactory.ValidateCredential(candidate, NewSecret);
        Result = candidate;
    }
}

internal sealed record VoiceServiceChoice(SpeechProviderProfile Profile, string Label, bool Placeholder = false);
internal sealed class VoiceEditor : ProfileEditor
{
    private readonly ComboBox provider;
    private bool offerRebind;
    private string builtId = "";
    private TextBox name = null!, voiceId = null!, language = null!, audioPath = null!, referenceText = null!, promptLanguage = null!, instructions = null!, speed = null!;
    private readonly CheckBox customVoice = new() { Content = "使用自定义 voice 对象", Margin = new(0,12,0,0) };
    public VoiceProfile Result { get; private set; }
    public VoiceEditor(VoiceProfile? original, IReadOnlyList<SpeechProviderProfile> providers) : base(original == null ? "添加音色" : "编辑音色", original == null)
    {
        Result = original ?? new() { Id = Guid.NewGuid().ToString("N") };
        var choices = providers.Select(p => new VoiceServiceChoice(p, p.Name)).ToList();
        var index = original is null ? 0 : choices.FindIndex(choice => choice.Profile.Id == original.ProviderId);
        if (original != null && index < 0)
        {
            // Its API was deleted. Offer a placeholder built from the cached vendor
            // so the voice stays editable instead of the dialog failing to open.
            var kind = Result.ProviderKind ?? SpeechProviderKind.Fish;
            choices.Insert(0, new VoiceServiceChoice(
                new SpeechProviderProfile { Id = Result.ProviderId, Kind = kind, Enabled = false },
                SpeechProviderFactory.Label(kind) + "（原服务已删除）", true));
            index = 0;
            offerRebind = choices.Count > 1;
        }
        provider = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label" };
        provider.SelectedIndex = Math.Max(0, index);
        Choice.Children.Add(new TextBlock { Text = "选择音色所属的服务", Margin = new(0,12,0,10) }); Choice.Children.Add(provider);
        if (providers.Count == 0) Choice.Children.Add(new TextBlock { Text = "请先在高级设置中添加 API 服务。", Margin = new(0,12,0,0) });
        if (original != null) BuildDetails();
    }
    protected override void BuildDetails()
    {
        if (provider.SelectedItem is not VoiceServiceChoice choice) throw new BridgeException("请先添加并选择语音服务。");
        var selected = choice.Profile;
        var key = choice.Placeholder ? "__deleted-service__" : selected.Id;
        if (builtId == key) return;
        builtId = key; Detach(customVoice); Fields.Children.Clear();
        // Editing an existing voice skips the first (service choice) page. When
        // its service was deleted, surface the same ComboBox on the details page
        // so the voice can still be re-pointed to a live service.
        if (offerRebind)
        {
            Detach(provider);
            Fields.Children.Add(new TextBlock { Text = "改绑到 API 服务（可选）",
                Style = (Style)FindResource("FieldLabel"), Margin = new(0,0,0,5) });
            Fields.Children.Add(provider);
        }
        var same = choice.Placeholder || Result.ProviderId.Length == 0 || Result.ProviderId == selected.Id;
        name = new TextBox { Text = same ? Result.Name : "", MaxLength = 64 };
        voiceId = new TextBox { Text = same ? Result.VoiceId : "", MaxLength = 256 };
        Field("音色名称", name);
        language = new TextBox { Text = same ? Result.Language : "zh-CN", MaxLength = 32 };
        audioPath = new TextBox { Text = same ? Result.ReferenceAudioPath : "", MaxLength = 1024 };
        referenceText = new TextBox { Text = same ? Result.ReferenceText : "", MaxLength = 2000 };
        promptLanguage = new TextBox { Text = same ? Result.PromptLanguage : "zh", MaxLength = 32 };
        instructions = new TextBox { Text = same ? Result.Instructions : "", MaxLength = 1000 };
        speed = new TextBox { Text = Result.Speed.ToString(CultureInfo.InvariantCulture), MaxLength = 16 };
        if (selected.Kind == SpeechProviderKind.GptSoVits)
        {
            Field("参考录音路径", audioPath); Hint("填写本地模型服务能读取的文件路径。");
            Field("参考录音原文（可选）", referenceText); Field("参考语言", promptLanguage); Field("朗读语言", language);
        }
        else Field(selected.Kind == SpeechProviderKind.Fish ? "Voice ID" : selected.Kind == SpeechProviderKind.AzureSpeech ? "Azure 音色标识" : "音色 ID", voiceId);
        Advanced(() =>
        {
            if (selected.Kind == SpeechProviderKind.AzureSpeech) Field("语言代码", language);
            if (selected.Kind == SpeechProviderKind.OpenAiCompatible)
            { Field("语气指令（模型支持时生效）", instructions); customVoice.IsChecked = same && Result.IsCustomVoice; FieldsForAdvanced(customVoice); }
            if (selected.Kind is SpeechProviderKind.OpenAiCompatible or SpeechProviderKind.GptSoVits) Field("语速", speed);
        });
        if (choice.Placeholder)
            Hint("原 API 已删除：仍可修改名称与音色 ID，保存后保持缺失样式；补上同厂商服务会自动恢复可用。");
    }
    // Use Field so advanced controls stay in their collapsed section.
    private void FieldsForAdvanced(UIElement control) => Field("音色格式", control);
    protected override void Save()
    {
        if (provider.SelectedItem is not VoiceServiceChoice choice) throw new BridgeException("请选择服务。");
        if (string.IsNullOrWhiteSpace(name.Text)) throw new BridgeException("请填写音色名称。");
        var selected = choice.Profile;
        if (!double.TryParse(speed.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || !double.IsFinite(rate) || rate is < .25 or > 4)
            throw new BridgeException("语速请填写 0.25 至 4。");
        var locale = language.Text.Trim();
        if (selected.Kind == SpeechProviderKind.AzureSpeech && locale == "zh-CN")
        { var parts = voiceId.Text.Split('-'); if (parts.Length >= 3) locale = parts[0] + "-" + parts[1]; }
        var id = voiceId.Text.Trim();
        var candidate = Result with { Name = name.Text.Trim(),
            ProviderId = choice.Placeholder ? Result.ProviderId : selected.Id,
            ProviderKind = choice.Placeholder ? Result.ProviderKind : selected.Kind,
            Vendor = choice.Placeholder ? Result.Vendor : ServiceIdentity.Of(selected),
            VoiceId = id, Language = locale, ReferenceAudioPath = audioPath.Text.Trim(),
            ReferenceText = referenceText.Text.Trim(), PromptLanguage = promptLanguage.Text.Trim(),
            Instructions = selected.Kind == SpeechProviderKind.OpenAiCompatible ? instructions.Text.Trim() : "",
            Speed = rate, IsCustomVoice = selected.Kind == SpeechProviderKind.OpenAiCompatible && customVoice.IsChecked == true };
        if (choice.Placeholder)
        {
            // No live connection to validate against; keep the old binding as-is.
            // SpeechFor() still refuses to synthesize until a key is available.
            if (string.IsNullOrWhiteSpace(id)) throw new BridgeException("请填写音色 ID。");
        }
        else SpeechProviderFactory.Validate(selected with { Enabled = true }, candidate);
        Result = candidate;
    }
}
