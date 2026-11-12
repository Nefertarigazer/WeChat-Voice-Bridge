using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;

namespace VoiceBridge.App;

internal abstract class ProfileEditor : Window
{
    protected readonly StackPanel Fields = new();
    protected readonly StackPanel Choice = new();
    protected readonly TextBlock Error = new() { Margin = new(0, 12, 0, 0) };
    private readonly Button save, back;
    private readonly ScrollViewer scroll;
    // The edges melt the content into the card surface (DialogBg, see DialogShell).
    private readonly Border topEdge = Edge(VerticalAlignment.Top, 28,
        (0, 0xFF), (0.55, 0x8C), (1, 0x00));
    // Light at the top so a peeking row stays readable, solid only at the very edge.
    private readonly Border bottomEdge = Edge(VerticalAlignment.Bottom, DefaultFade,
        (0, 0x00), (0.5, 0x48), (1, 0xE6));
    private const double DefaultFade = 32;
    /// <summary>
    /// How much of the next row shows at the bottom edge: its head (a list entry's
    /// name, a card's top half), never its tail.
    /// </summary>
    private const double PeekShare = 0.48;
    private readonly double viewportLimit;
    private bool showTop, showBottom;
    private StackPanel? target;
    private bool choosing;
    protected ProfileEditor(string title, bool isNew)
    {
        Title = title; Width = 520; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 60);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; FontFamily = new("Microsoft YaHei UI"); FontSize = 14;
        var grid = new Grid { Margin = new(28,8,28,24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,8) });
        var pages = new Grid(); pages.Children.Add(Choice); pages.Children.Add(Fields);
        // No scroll bar: the wheel, touchpad and keyboard still scroll, and the edges
        // dissolve into the card wherever there is more to see (see UpdateEdges).
        viewportLimit = Math.Max(120, MaxHeight - 250);
        scroll = new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = viewportLimit };
        var viewport = new Grid();
        viewport.Children.Add(scroll);
        viewport.Children.Add(topEdge);
        viewport.Children.Add(bottomEdge);
        scroll.ScrollChanged += (_, _) => UpdateEdges();
        Grid.SetRow(viewport, 1); grid.Children.Add(viewport);
        var footer = new StackPanel(); Grid.SetRow(footer, 2);
        Error.Foreground = (Brush)FindResource("Danger"); Error.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(Error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0,16,0,0) };
        back = new Button { Content = "上一步", Style = (Style)FindResource("PlainButton"), Margin = new(0,0,10,0) };
        back.Click += (_, _) => ShowPage(true);
        var cancel = new Button { Content = "取消", IsCancel = true, Style = (Style)FindResource("SecondaryButton") };
        cancel.Click += (_, _) => DialogResult = false;
        save = new Button { Content = "下一步", IsDefault = true, Margin = new(10,0,0,0), Style = (Style)FindResource("PrimaryButton") };
        save.Click += (_, _) => Advance();
        buttons.Children.Add(back); buttons.Children.Add(cancel); buttons.Children.Add(save);
        footer.Children.Add(buttons); grid.Children.Add(footer); DialogShell.Apply(this, grid);
        ShowPage(isNew); if (!isNew) back.Visibility = Visibility.Collapsed;
    }
    private static Border Edge(VerticalAlignment side, double height, params (double Offset, byte Alpha)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        var surface = Theme.ColorOf("DialogBg");
        foreach (var (offset, alpha) in stops)
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, surface.R, surface.G, surface.B), offset));
        brush.Freeze();
        return new Border { Height = height, VerticalAlignment = side, Background = brush, IsHitTestVisible = false, Opacity = 0 };
    }

    /// <summary>
    /// The only cue that a page goes on: content that is cut off fades out into the
    /// card at that edge. The bottom edge shows while anything lies below, the top
    /// edge once the page has left its top, so a fully visible page has neither.
    /// </summary>
    private void UpdateEdges()
    {
        var more = scroll.ScrollableHeight - scroll.VerticalOffset > 0.5;
        var scrolled = scroll.VerticalOffset > 0.5;
        if (more != showBottom) { showBottom = more; FadeEdge(bottomEdge, more); }
        if (scrolled != showTop) { showTop = scrolled; FadeEdge(topEdge, scrolled); }
    }

    private static void FadeEdge(UIElement edge, bool show)
    {
        if (!SystemParameters.ClientAreaAnimation) { edge.BeginAnimation(OpacityProperty, null); edge.Opacity = show ? 1 : 0; return; }
        edge.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(show ? 1 : 0,
            TimeSpan.FromMilliseconds(show ? 180 : 140)));
    }

    /// <summary>Same as pressing the primary button: next page, or save on the last one.</summary>
    protected void Advance()
    {
        try { if (choosing) { BuildDetails(); ShowPage(false); } else { Save(); DialogResult = true; } }
        catch (BridgeException error) { Error.Text = error.Message; }
    }
    private void ShowPage(bool first)
    {
        choosing = first; Error.Text = "";
        Choice.Visibility = first ? Visibility.Visible : Visibility.Collapsed;
        Fields.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
        save.Content = first ? "下一步" : "保存";
        back.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
        // Each page starts at its top; the other page's scroll position means nothing here.
        scroll.ScrollToTop();
        scroll.MaxHeight = viewportLimit;
        bottomEdge.Height = DefaultFade;
        var page = first ? Choice : Fields;
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(() => PeekNextRow(page)), System.Windows.Threading.DispatcherPriority.Loaded);
        else
        {
            void Once(object? sender, RoutedEventArgs e) { Loaded -= Once; PeekNextRow(page); }
            Loaded += Once;
        }
    }

    /// <summary>
    /// When a page is taller than the dialog, end the visible area a little way into
    /// a row, so the head of the next entry peeks out from under the edge. A row that
    /// starts below the edge reads as "there is more"; one whose tail is missing only
    /// reads as "cut off". Only rows of a real size count (entries, fields, cards), so
    /// a label or a one-line hint is never what peeks out.
    /// </summary>
    private void PeekNextRow(FrameworkElement page)
    {
        if (page.Visibility != Visibility.Visible || scroll.Content is not FrameworkElement content) return;
        scroll.UpdateLayout();
        if (content.ActualHeight <= viewportLimit + 0.5) return;
        double cut = 0, peek = 0;
        foreach (var row in Rows(page))
        {
            if (!row.IsVisible || row.ActualHeight < 28) continue;
            var top = row.TransformToAncestor(content).Transform(new Point()).Y;
            var at = top + row.ActualHeight * PeekShare;
            if (at <= viewportLimit && at > cut) { cut = at; peek = at - top; }
        }
        // Never shrink the dialog much just to land on a row boundary.
        if (cut < viewportLimit * 0.6) return;
        scroll.MaxHeight = cut;
        // The fade only softens the lower part of the peek, so the head stays legible.
        bottomEdge.Height = Math.Max(12, peek * 0.6);
    }

    private static IEnumerable<FrameworkElement> Rows(DependencyObject node)
    {
        if (node is ListBoxItem item) { yield return item; yield break; }
        if (node is ItemsControl list and not ComboBox)
        {
            foreach (var entry in Descendants<ListBoxItem>(list)) yield return entry;
            yield break;
        }
        if (node is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                foreach (var row in Rows(child)) yield return row;
            yield break;
        }
        if (node is FrameworkElement leaf) yield return leaf;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) { yield return match; continue; }
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }
    protected TextBox Text(string label, string value, int maximum = 512)
    {
        var box = new TextBox { Text = value, MaxLength = maximum, MinWidth = 80 }; Field(label, box); return box;
    }
    protected static TextBox MultiLine(string value, int maximum = 16_384) => new()
    {
        Text = value, MaxLength = maximum, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        MinHeight = 64, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new("Consolas, Microsoft YaHei UI"), FontSize = 13
    };
    protected void Field(string label, UIElement control)
    {
        var panel = target ?? Fields;
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("FieldLabel"), Margin = new(0,12,0,5) });
        panel.Children.Add(control);
    }
    protected TextBlock Hint(string text)
    {
        var line = new TextBlock { Text = text, Style = (Style)FindResource("Footnote"), Margin = new(0,8,0,0), TextWrapping = TextWrapping.Wrap };
        (target ?? Fields).Children.Add(line);
        return line;
    }
    /// <summary>A line with a link that opens in the browser. Only catalog addresses are linked.</summary>
    protected void Link(string text, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps) return;
        var link = new Hyperlink(new Run(text)) { NavigateUri = address, Foreground = (Brush)FindResource("Accent") };
        link.RequestNavigate += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception) { /* No browser: the address is still shown in the guide. */ }
            e.Handled = true;
        };
        (target ?? Fields).Children.Add(new TextBlock(link) { FontSize = 12, Margin = new(0,6,0,0) });
    }
    protected void Advanced(Action build)
    {
        var panel = new StackPanel(); target = panel; build(); target = null;
        if (panel.Children.Count > 0)
            Fields.Children.Add(Disclosure.Create("高级", panel, scroll));
    }

    /// <summary>A soft amber pill with a globe: this vendor needs an overseas network from mainland China.</summary>
    protected const string OverseasBadge = """
        <Border Background="{DynamicResource CautionSoft}" BorderBrush="{DynamicResource CautionBorder}" BorderThickness="1" CornerRadius="7" Padding="6,1,8,2"
                Margin="8,0,0,0" VerticalAlignment="Center">
          <StackPanel Orientation="Horizontal">
            <Path Data="M0.5,6 A5.5,5.5 0 1 1 11.5,6 A5.5,5.5 0 1 1 0.5,6 Z M0.9,4 H11.1 M0.9,8 H11.1 M6,0.5 C3.3,2.7 3.3,9.3 6,11.5 M6,0.5 C8.7,2.7 8.7,9.3 6,11.5"
                  Stroke="{DynamicResource Caution}" StrokeThickness="1" Width="11" Height="11" Stretch="Uniform" VerticalAlignment="Center"/>
            <TextBlock Text="需海外网络" FontSize="11" Foreground="{DynamicResource CautionText}" Margin="4,0,0,0" VerticalAlignment="Center"/>
          </StackPanel>
        </Border>
        """;

    protected static UIElement Markup(string xaml) => (UIElement)XamlReader.Parse(
        xaml.Replace("<Border ", "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" ", StringComparison.Ordinal));
    protected abstract void BuildDetails();
    protected abstract void Save();
    protected static void Detach(UIElement control)
    {
        if (LogicalTreeHelper.GetParent(control) is Panel panel) panel.Children.Remove(control);
    }
    /// <summary>Small templates built from markup; only literal values, so no resource lookup is needed.</summary>
    protected static DataTemplate MarkupTemplate(string body) => (DataTemplate)XamlReader.Parse(
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" + body + "</DataTemplate>");
}

internal sealed record ModelChoice(string Id, string Label);

/// <summary>One entry of the service picker, grouped as the catalog groups them.</summary>
internal sealed record PresetChoice(ProviderPreset Preset)
{
    public string Label => Preset.Label;
    public string Summary => Preset.Summary;
    public Visibility OverseasVisibility => Preset.Overseas ? Visibility.Visible : Visibility.Collapsed;
    public string Group => Preset.Category switch
    {
        PresetCategory.Generic => "通用协议",
        PresetCategory.Mainstream => "主流厂商",
        _ => "自建模型"
    };
    /// <summary>Groups appear in this order: generic protocols, mainstream vendors, self-hosted models.</summary>
    public int GroupOrder => Preset.Category switch
    {
        PresetCategory.Generic => 0,
        PresetCategory.Mainstream => 1,
        _ => 2
    };
    // Screen readers and UI automation read the entry by this name.
    public override string ToString() => Label;
}

internal sealed class ProviderEditor : ProfileEditor
{
    private readonly ListBox presets;
    private readonly SpeechProviderProfile? original;
    private ProviderPreset? built;
    private TextBox name = null!, endpoint = null!, extraModels = null!, modelParameter = null!, appId = null!, timeout = null!;
    private TextBox headers = null!, body = null!, jsonPath = null!;
    private ComboBox endpointChoice = null!, latency = null!, format = null!, responseMode = null!, encoding = null!;
    private readonly List<(CheckBox Box, string Id)> modelBoxes = new();
    private readonly PasswordBox secret = new() { MaxLength = 2048 };
    public SpeechProviderProfile Result { get; private set; }
    public string NewSecret => secret.Password;

    public ProviderEditor(SpeechProviderProfile? original) : base(original == null ? "添加语音服务" : "编辑语音服务", original == null)
    {
        this.original = original;
        Result = original ?? new() { Id = Guid.NewGuid().ToString("N") };
        // Groups follow their first item, so order the entries by group (a stable sort
        // keeps the catalog order inside each group).
        var view = new ListCollectionView(ProviderCatalog.All.Select(item => new PresetChoice(item))
            .OrderBy(item => item.GroupOrder).ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PresetChoice.Group)));
        var badge = OverseasBadge.Replace("<Border ", "<Border Visibility=\"{Binding OverseasVisibility}\" ", StringComparison.Ordinal);
        presets = new ListBox
        {
            ItemsSource = view,
            ItemTemplate = MarkupTemplate("<StackPanel Margin=\"2,1\"><StackPanel Orientation=\"Horizontal\">" +
                "<TextBlock Text=\"{Binding Label}\" FontWeight=\"SemiBold\" VerticalAlignment=\"Center\"/>" + badge + "</StackPanel>" +
                "<TextBlock Text=\"{Binding Summary}\" FontSize=\"12\" Foreground=\"{DynamicResource Secondary}\" TextWrapping=\"Wrap\" Margin=\"0,2,0,0\"/></StackPanel>"),
            SelectedIndex = 0
        };
        ScrollViewer.SetVerticalScrollBarVisibility(presets, ScrollBarVisibility.Disabled);
        presets.GroupStyle.Add(new GroupStyle
        {
            HeaderTemplate = MarkupTemplate("<TextBlock Text=\"{Binding Name}\" FontSize=\"12\" FontWeight=\"SemiBold\" Foreground=\"{DynamicResource Accent}\" Margin=\"6,14,0,4\"/>")
        });
        presets.MouseDoubleClick += (_, _) => { if (presets.SelectedItem is PresetChoice) Advance(); };
        Choice.Children.Add(new TextBlock { Text = "选择要接入的语音服务。没有专属入口的厂商用「通用协议」接入。",
            Style = (Style)FindResource("Footnote"), TextWrapping = TextWrapping.Wrap, Margin = new(0,4,0,4) });
        Choice.Children.Add(presets);
        if (original != null) BuildDetails();
    }

    private ProviderPreset Selected()
    {
        if (original != null) return ProviderCatalog.For(original);
        return presets.SelectedItem is PresetChoice choice ? choice.Preset : throw new BridgeException("请选择语音服务。");
    }

    protected override void BuildDetails()
    {
        var preset = Selected();
        if (built == preset) return;
        if (built != null) secret.Password = "";
        built = preset; Detach(secret); Fields.Children.Clear(); modelBoxes.Clear();
        var same = original != null;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0,4,0,0) };
        header.Children.Add(new TextBlock { Text = preset.Label, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (preset.Overseas) header.Children.Add(Markup(OverseasBadge));
        Fields.Children.Add(header);
        if (preset.Summary.Length > 0)
            Fields.Children.Add(new TextBlock { Text = preset.Summary, Style = (Style)FindResource("Footnote"), TextWrapping = TextWrapping.Wrap, Margin = new(0,2,0,0) });
        if (preset.Overseas)
            Fields.Children.Add(new TextBlock
            {
                Text = "在中国大陆，注册账号、获取 Key 和合成语音都需要海外网络环境；网络断开时这一家的音色会读不出来。",
                FontSize = 12, Foreground = Theme.Brush("CautionText"), TextWrapping = TextWrapping.Wrap,
                Margin = new(0,6,0,0)
            });

        name = new TextBox { Text = same ? Result.Name : "", MaxLength = 64 };
        if (preset.NameRequired) { Field("服务名称（自己起，方便辨认）", name); }

        // ---- address
        var address = same && !string.IsNullOrWhiteSpace(Result.Endpoint) ? Result.Endpoint : preset.DefaultEndpoint;
        endpoint = new TextBox { Text = address, MaxLength = 2048 };
        endpointChoice = new ComboBox { ItemsSource = preset.Endpoints, DisplayMemberPath = nameof(CatalogEndpoint.Label) };
        if (preset.Endpoints.Length > 1)
        {
            var index = Array.FindIndex(preset.Endpoints, item => SameAddress(item.Url, address));
            endpointChoice.SelectedIndex = index < 0 ? (preset.CustomEndpoint ? -1 : 0) : index;
            endpointChoice.SelectionChanged += (_, _) =>
            {
                if (endpointChoice.SelectedItem is CatalogEndpoint picked) endpoint.Text = picked.Url;
            };
            Field(preset.Kind == SpeechProviderKind.AzureSpeech ? "区域" : "接入地址", endpointChoice);
        }
        if (preset.CustomEndpoint)
        {
            Field(preset.Endpoints.Length > 1 ? "或者直接填写地址" : "接口地址", endpoint);
            if (preset.EndpointHint.Length > 0) Hint(preset.EndpointHint);
        }

        // ---- key
        if (preset.Key != KeyNeed.None)
        {
            Field(preset.Key == KeyNeed.Optional ? preset.KeyLabel + "（可选）" : preset.KeyLabel, secret);
            Hint(same && Result.CredentialId.Length > 0 ? "留空保留此连接的密钥。" : "只保存在当前 Windows 用户的加密凭据库，不会写进配置文件。");
            if (preset.KeyUrl.Length > 0) Link("去哪里获取 " + preset.KeyLabel + " ↗", preset.KeyUrl);
        }

        // ---- models
        var listed = same ? CurrentModels(Result) : [];
        if (preset.Models.Length > 0)
        {
            var choices = new StackPanel();
            foreach (var model in preset.Models)
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = model.Label });
                if (model.Note.Length > 0)
                    content.Children.Add(new TextBlock { Text = model.Note, FontSize = 11.5, Foreground = (Brush)FindResource("Secondary"), TextWrapping = TextWrapping.Wrap, Margin = new(0,2,0,0) });
                var box = new CheckBox
                {
                    Content = content, Style = (Style)FindResource("ModelSelectionCard"),
                    Tag = model.Label.Contains("免费", StringComparison.Ordinal) ? "免费" : model.Label.Contains("付费", StringComparison.Ordinal) ? "付费" : "",
                    IsChecked = same ? listed.Contains(model.Id) : model.Checked
                };
                modelBoxes.Add((box, model.Id));
                choices.Children.Add(box);
            }
            Field(preset.ModelLabel + "（可多选，首页可按联系人切换）", choices);
        }
        var custom = listed.Where(item => preset.Models.All(model => model.Id != item)).ToArray();
        extraModels = new TextBox { Text = string.Join(", ", custom), MaxLength = 2048 };
        if (preset.CustomModels)
        {
            Field(preset.Models.Length > 0 ? "其他" + preset.ModelLabel + "（可选，逗号分隔）" : preset.ModelLabel + "名称（多个用逗号分隔，第一个为默认）", extraModels);
            if (preset.Models.Length == 0) Hint("填写这项服务认识的模型名；保存后首页可以在这些模型之间切换。");
        }

        // ---- custom HTTP
        var template = Result.Template ?? new CustomHttpTemplate();
        headers = MultiLine(template.Headers, 4096);
        body = MultiLine(template.Body);
        jsonPath = new TextBox { Text = template.JsonPath, MaxLength = 256 };
        responseMode = new ComboBox { ItemsSource = new[] { new ModelChoice("audio", "直接返回音频（MP3 / WAV）"), new ModelChoice("json", "返回 JSON，音频在某个字段里") },
            DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = template.Response };
        encoding = new ComboBox { ItemsSource = new[] { new ModelChoice("base64", "base64"), new ModelChoice("hex", "十六进制（hex）"), new ModelChoice("url", "音频下载地址（url）") },
            DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = template.Encoding };
        if (preset.Kind == SpeechProviderKind.CustomHttp)
        {
            Field("请求头（每行一个「名称: 值」，Key 写成 {{key}}）", headers);
            Field("请求体模板（可用 {{text}} {{voice}} {{model}} {{speed}}）", body);
            Field("返回方式", responseMode);
            // The JSON details only matter when the answer is wrapped in JSON.
            var wrapped = new StackPanel();
            Fields.Children.Add(wrapped);
            wrapped.Children.Add(new TextBlock { Text = "音频所在字段（例如 data.audio 或 result[0].audio）", Style = (Style)FindResource("FieldLabel"), Margin = new(0,12,0,5) });
            wrapped.Children.Add(jsonPath);
            wrapped.Children.Add(new TextBlock { Text = "字段编码", Style = (Style)FindResource("FieldLabel"), Margin = new(0,12,0,5) });
            wrapped.Children.Add(encoding);
            void Toggle() => wrapped.Visibility = responseMode.SelectedValue as string == "json" ? Visibility.Visible : Visibility.Collapsed;
            responseMode.SelectionChanged += (_, _) => Toggle();
            Toggle();
        }

        foreach (var note in preset.Notes) Hint(note);
        if (preset.DocsUrl.Length > 0)
            Link(preset.DocsUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase) ? "项目主页 ↗" : "官方接入文档 ↗", preset.DocsUrl);

        // ---- advanced
        modelParameter = new TextBox { Text = same && Result.Kind == SpeechProviderKind.Doubao ? Result.Model : "", MaxLength = 128 };
        appId = new TextBox { Text = same ? Result.AppId : "", MaxLength = 128 };
        timeout = new TextBox { Text = Result.TimeoutSeconds.ToString(CultureInfo.InvariantCulture), MaxLength = 3 };
        format = new ComboBox { ItemsSource = new[] { "mp3", "wav" }, SelectedItem = same ? Result.AudioFormat : preset.AudioFormat };
        latency = new ComboBox { ItemsSource = new[] { new ModelChoice("balanced", "均衡"), new ModelChoice("normal", "音质优先"), new ModelChoice("low", "低延迟") },
            DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = Result.Latency };
        Advanced(() =>
        {
            if (!preset.NameRequired) Field("显示名称（可选，同一厂商有多个 Key 时用来区分）", name);
            if (preset.Kind == SpeechProviderKind.Fish) Field("默认合成模式", latency);
            if (preset.Kind == SpeechProviderKind.OpenAiCompatible && preset.Category != PresetCategory.Mainstream) Field("音频格式", format);
            if (preset.Kind == SpeechProviderKind.Doubao) { Field("旧版控制台 App ID（新版留空）", appId); Field("复刻 2.0 模型参数（可选）", modelParameter); }
            Field("超时秒数（5–300）", timeout);
        });
    }

    private static string[] CurrentModels(SpeechProviderProfile provider)
    {
        if (provider.Kind == SpeechProviderKind.Fish)
        {
            try { return SpeechProviderFactory.FishModels(provider); }
            catch (BridgeException) { return []; }
        }
        return SpeechModels.Listed(provider);
    }

    private static bool SameAddress(string a, string b) =>
        string.Equals(ServiceIdentity.NormalizeEndpoint(a), ServiceIdentity.NormalizeEndpoint(b), StringComparison.Ordinal);

    protected override void Save()
    {
        var preset = built ?? throw new BridgeException("请选择服务。");
        var address = preset.CustomEndpoint ? endpoint.Text.Trim()
            : endpointChoice.SelectedItem is CatalogEndpoint picked ? picked.Url : preset.DefaultEndpoint;
        if (preset.Kind == SpeechProviderKind.AzureSpeech && address.Length > 0 && address.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            address = $"https://{address}.tts.speech.microsoft.com/cognitiveservices/v1";
        if (preset.Kind != SpeechProviderKind.EdgeTts && address.Length == 0) throw new BridgeException("请填写接口地址。");
        if (!int.TryParse(timeout.Text, out var seconds) || seconds is < 5 or > 300) throw new BridgeException("超时秒数请填写 5 至 300。");
        if (preset.NameRequired && string.IsNullOrWhiteSpace(name.Text)) throw new BridgeException("请给这项服务起个名字，方便以后辨认。");
        var label = string.IsNullOrWhiteSpace(name.Text) ? preset.Label : name.Text.Trim();

        var models = modelBoxes.Where(item => item.Box.IsChecked == true).Select(item => item.Id)
            .Concat(extraModels.Text.Split(new[] { ',', '，', '\n', '\r' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal).ToArray();
        var needsModel = preset.Kind is SpeechProviderKind.OpenAiCompatible or SpeechProviderKind.ElevenLabs or SpeechProviderKind.MiniMax
            or SpeechProviderKind.DashScope or SpeechProviderKind.Gemini or SpeechProviderKind.Doubao
            || preset.Kind == SpeechProviderKind.Fish && preset.Models.Length > 0;
        if (needsModel && models.Length == 0) throw new BridgeException("请至少选择或填写一个" + preset.ModelLabel + "。");
        var current = original is null ? "" : SpeechModels.Current(original);
        var active = models.Contains(current, StringComparer.Ordinal) ? current : models.FirstOrDefault() ?? "";

        // Keep the existing service -> credential link. Re-entering a key replaces
        // the value of the entry this connection already points at; only a
        // connection that never had a credential gets a generated name.
        var key = original?.CredentialId ?? "";
        if (!string.IsNullOrWhiteSpace(NewSecret))
            key = key.Length > 0 ? key : "speech-" + Result.Id;
        var template = preset.Kind == SpeechProviderKind.CustomHttp ? new CustomHttpTemplate
        {
            Headers = headers.Text.Trim(), Body = body.Text.Trim(),
            Response = responseMode.SelectedValue as string ?? "audio",
            JsonPath = jsonPath.Text.Trim(), Encoding = encoding.SelectedValue as string ?? "base64"
        } : null;
        // Editing a standby key must not silently make it active. Which key is in
        // use is chosen with the "启用" button; adding a key never applies it.
        var candidate = Result with
        {
            Kind = preset.Kind, Preset = preset.Id, Name = label, Endpoint = address,
            Model = preset.Kind == SpeechProviderKind.Doubao ? modelParameter.Text.Trim() : active,
            ResourceId = preset.Kind == SpeechProviderKind.Doubao ? active : Result.ResourceId,
            AvailableModels = models, CredentialId = key, AppId = appId.Text.Trim(), Enabled = Result.Enabled,
            TimeoutSeconds = seconds, Latency = latency.SelectedValue as string ?? "balanced",
            AudioFormat = preset.Kind == SpeechProviderKind.OpenAiCompatible && preset.Category != PresetCategory.Mainstream
                ? format.SelectedItem as string ?? "mp3" : preset.AudioFormat,
            Template = template
        };
        candidate = candidate with
        {
            Vendor = original is not null && !string.IsNullOrWhiteSpace(original.Vendor) ? original.Vendor : preset.VendorFor(candidate)
        };
        if (preset.Kind != SpeechProviderKind.EdgeTts) _ = SpeechProviderFactory.ResolveEndpoint(candidate);
        if (template is not null) CustomTemplates.Validate(template);
        if (SpeechProviderFactory.RequiresCredential(candidate) && key.Length == 0) throw new BridgeException("请填写 " + preset.KeyLabel + "。");
        if (!string.IsNullOrWhiteSpace(NewSecret)) SpeechProviderFactory.ValidateCredential(candidate, NewSecret);
        Result = candidate;
    }
}

internal sealed record VoiceServiceChoice(SpeechProviderProfile Profile, string Label, bool Placeholder = false);

/// <summary>A known voice offered as a shortcut; picking it fills the id box.</summary>
internal sealed record VoiceSuggestion(CatalogVoice Voice)
{
    public string Text => Voice.Label == Voice.Id ? Voice.Id : Voice.Label + "  ·  " + Voice.Id;
    public override string ToString() => Text;
}

internal sealed class VoiceEditor : ProfileEditor
{
    private readonly ComboBox provider;
    private bool offerRebind;
    private string builtId = "";
    private TextBox name = null!, voiceId = null!, language = null!, audioPath = null!, referenceText = null!, promptLanguage = null!, instructions = null!, speed = null!;
    private ComboBox pinned = null!;
    private TextBlock compatibility = null!;
    private ProviderPreset preset = null!;
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
                new SpeechProviderProfile { Id = Result.ProviderId, Kind = kind, Vendor = Result.Vendor, Enabled = false },
                SpeechProviderFactory.Label(kind) + "（原服务已删除）", true));
            index = 0;
            offerRebind = choices.Count > 1;
        }
        provider = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label" };
        provider.SelectedIndex = Math.Max(0, index);
        Choice.Children.Add(new TextBlock { Text = "选择音色所属的服务", Margin = new(0,12,0,10) }); Choice.Children.Add(provider);
        if (providers.Count == 0) Choice.Children.Add(new TextBlock { Text = "请先在设置中添加语音服务。", Margin = new(0,12,0,0) });
        if (original != null) BuildDetails();
    }

    private static string[] ModelsOf(SpeechProviderProfile service)
    {
        if (service.Kind == SpeechProviderKind.Fish)
        {
            try { return SpeechProviderFactory.FishModels(service); }
            catch (BridgeException) { return []; }
        }
        return SpeechModels.Listed(service);
    }

    protected override void BuildDetails()
    {
        if (provider.SelectedItem is not VoiceServiceChoice choice) throw new BridgeException("请先添加并选择语音服务。");
        var selected = choice.Profile;
        var key = choice.Placeholder ? "__deleted-service__" : selected.Id;
        if (builtId == key) return;
        builtId = key; Detach(customVoice); Fields.Children.Clear();
        preset = ProviderCatalog.For(selected);
        // Editing an existing voice skips the first (service choice) page. When
        // its service was deleted, surface the same ComboBox on the details page
        // so the voice can still be re-pointed to a live service.
        if (offerRebind)
        {
            Detach(provider);
            Fields.Children.Add(new TextBlock { Text = "改绑到语音服务（可选）",
                Style = (Style)FindResource("FieldLabel"), Margin = new(0,0,0,5) });
            Fields.Children.Add(provider);
        }
        var same = choice.Placeholder || Result.ProviderId.Length == 0 || Result.ProviderId == selected.Id;
        name = new TextBox { Text = same ? Result.Name : "", MaxLength = 64 };
        voiceId = new TextBox { Text = same ? Result.VoiceId : "", MaxLength = 512 };
        Field("音色名称（自己起，方便辨认）", name);
        language = new TextBox { Text = same ? Result.Language : "zh-CN", MaxLength = 32 };
        audioPath = new TextBox { Text = same ? Result.ReferenceAudioPath : "", MaxLength = 1024 };
        referenceText = new TextBox { Text = same ? Result.ReferenceText : "", MaxLength = 2000 };
        promptLanguage = new TextBox { Text = same ? Result.PromptLanguage : "zh", MaxLength = 32 };
        instructions = new TextBox { Text = same ? Result.Instructions : "", MaxLength = 1000 };
        speed = new TextBox { Text = Result.Speed.ToString(CultureInfo.InvariantCulture), MaxLength = 16 };
        compatibility = new TextBlock { Foreground = (Brush)FindResource("Caution"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0,6,0,0) };

        string[] models = choice.Placeholder ? [] : ModelsOf(selected);
        pinned = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Id" };
        pinned.ItemsSource = new[] { new ModelChoice("", "不固定（跟随联系人与服务的模型）") }
            .Concat(models.Select(model => new ModelChoice(model, preset.ModelLabelOf(model)))).ToArray();
        pinned.SelectedValue = same && models.Contains(Result.Model) ? Result.Model : "";
        if (pinned.SelectedIndex < 0) pinned.SelectedIndex = 0;

        if (preset.Voice == VoiceForm.Reference)
        {
            Field("参考录音路径", audioPath); Hint("填写本地模型服务能读取的文件路径（远程服务就填服务器上的路径）。");
            Field("参考录音原文（可选）", referenceText); Field("参考语言", promptLanguage); Field("朗读语言", language);
        }
        else
        {
            Field(preset.VoiceLabel, voiceId);
            if (preset.Voices.Length > 0)
            {
                var suggestions = new ComboBox
                {
                    ItemsSource = preset.Voices.Select(item => new VoiceSuggestion(item)).ToArray(),
                    DisplayMemberPath = nameof(VoiceSuggestion.Text)
                };
                suggestions.SelectionChanged += (_, _) =>
                {
                    if (suggestions.SelectedItem is not VoiceSuggestion picked) return;
                    voiceId.Text = picked.Voice.Id;
                    if (string.IsNullOrWhiteSpace(name.Text)) name.Text = picked.Voice.Label;
                    // A voice that only exists for one model (SiliconFlow "model:voice") pins it.
                    if (picked.Voice.Models is [var only] && models.Contains(only)) pinned.SelectedValue = only;
                    CheckCompatibility();
                };
                Field("常用音色（选一个自动填入，也可以直接在上面填写）", suggestions);
            }
            if (preset.VoiceHint.Length > 0) Hint(preset.VoiceHint);
            if (preset.VoicesUrl.Length > 0) Link("查看这家的音色列表 ↗", preset.VoicesUrl);
        }
        if (models.Length > 0)
        {
            Field("固定模型（可选）", pinned);
            Hint("复刻音色、豆包 2.0 音色这类只能用某个模型的音色才需要固定；固定后首页不再为它切换模型。");
            Fields.Children.Add(compatibility);
            pinned.SelectionChanged += (_, _) => CheckCompatibility();
            voiceId.TextChanged += (_, _) => CheckCompatibility();
            CheckCompatibility();
        }
        Advanced(() =>
        {
            if (preset.Kind == SpeechProviderKind.AzureSpeech) Field("语言代码", language);
            if (preset.Kind is SpeechProviderKind.OpenAiCompatible or SpeechProviderKind.Gemini or SpeechProviderKind.DashScope)
                Field(preset.Kind switch
                {
                    SpeechProviderKind.Gemini => "语气描述（例如：温柔、放慢一点）",
                    SpeechProviderKind.DashScope => "合成指令（CosyVoice v3 可控制方言、情感）",
                    _ => "语气指令（gpt-4o-mini-tts 等支持时生效）"
                }, instructions);
            if (preset.Id is "openai" or ProviderCatalog.OpenAiCompatibleId)
            { customVoice.IsChecked = same && Result.IsCustomVoice; Field("音色格式", customVoice); }
            if (preset.Kind is SpeechProviderKind.OpenAiCompatible or SpeechProviderKind.GptSoVits or SpeechProviderKind.ElevenLabs
                or SpeechProviderKind.MiniMax or SpeechProviderKind.DashScope or SpeechProviderKind.EdgeTts or SpeechProviderKind.CustomHttp)
                Field("语速（1 为正常）", speed);
        });
        if (choice.Placeholder)
            Hint("原服务已删除：仍可修改名称与音色 ID，保存后保持缺失样式；补上同厂商服务会自动恢复可用。");
    }

    /// <summary>Say so when a known voice is paired with a model it does not work with. It is a warning, not a block.</summary>
    private void CheckCompatibility()
    {
        if (compatibility is null || provider.SelectedItem is not VoiceServiceChoice choice) return;
        var known = preset.Voices.FirstOrDefault(item => item.Id == voiceId.Text.Trim());
        var model = pinned.SelectedValue as string is { Length: > 0 } chosen ? chosen : SpeechModels.Current(choice.Profile);
        compatibility.Text = known is { Models: { } only } && !known.WorksWith(model)
            ? $"提示：这个音色只适用于 {string.Join(" / ", only.Select(preset.ModelLabelOf))}，建议在「固定模型」里选它。"
            : "";
        compatibility.Visibility = compatibility.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

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
        var model = pinned.SelectedValue as string ?? "";
        var candidate = Result with { Name = name.Text.Trim(),
            ProviderId = choice.Placeholder ? Result.ProviderId : selected.Id,
            ProviderKind = choice.Placeholder ? Result.ProviderKind : selected.Kind,
            Vendor = choice.Placeholder ? Result.Vendor : ServiceIdentity.Of(selected),
            Model = choice.Placeholder ? Result.Model : model,
            VoiceId = id, Language = locale, ReferenceAudioPath = audioPath.Text.Trim(),
            ReferenceText = referenceText.Text.Trim(), PromptLanguage = promptLanguage.Text.Trim(),
            Instructions = selected.Kind is SpeechProviderKind.OpenAiCompatible or SpeechProviderKind.Gemini or SpeechProviderKind.DashScope
                ? instructions.Text.Trim() : "",
            Speed = rate, IsCustomVoice = selected.Kind == SpeechProviderKind.OpenAiCompatible && customVoice.IsChecked == true };
        if (choice.Placeholder)
        {
            // No live connection to validate against; keep the old binding as-is.
            // SpeechFor() still refuses to synthesize until a key is available.
            if (string.IsNullOrWhiteSpace(id) && preset.Voice == VoiceForm.VoiceId) throw new BridgeException("请填写音色 ID。");
        }
        else SpeechProviderFactory.Validate(SpeechModels.Use(selected with { Enabled = true }, model), candidate);
        Result = candidate;
    }
}
