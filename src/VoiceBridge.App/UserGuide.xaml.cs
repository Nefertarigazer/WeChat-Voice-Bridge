using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceBridge.Core;
using VoiceBridge.WeChat;

namespace VoiceBridge.App;

/// <summary>
/// The 使用说明 page. 快速开始 comes first: a new user lands here until setup is done.
/// The rest only explains what the screen itself does not.
/// </summary>
public partial class UserGuide : UserControl
{
    // Has a free tier or free model, or costs nothing at all (local models are always free).
    private static readonly HashSet<string> FreeVendors = new(StringComparer.Ordinal) { "edge", "fish", "azure", "gemini" };
    /// <summary>The changelog lists this many versions; the rest wait behind a button.</summary>
    private const int RecentReleases = 4;
    private readonly IReadOnlyList<ReleaseEntry> releases = ReleaseHistory.Through(UpdateProcess.CurrentVersion);
    private readonly ObservableCollection<ReleaseEntry> shownReleases = new();

    public UserGuide()
    {
        InitializeComponent();
        // The version table reads the same bounds the app enforces.
        var low = WeChatSession.MinSupportedVersion;
        var high = WeChatSession.MaxSupportedVersion;
        VerifiedVersion.Text = high.ToString();
        CoveredVersions.Text = $"{low} 至 {high} 之间的其他版本";
        RefusedVersions.Text = $"{low} 之前，或 {high} 之后";

        MainstreamVendors.ItemsSource = Vendors(PresetCategory.Mainstream);
        GenericVendors.ItemsSource = Vendors(PresetCategory.Generic);
        LocalVendors.ItemsSource = Vendors(PresetCategory.Local);
        Features.ItemsSource = GuideContent.Features;
        Questions.ItemsSource = GuideContent.Questions;
        foreach (var release in releases.Take(RecentReleases)) shownReleases.Add(release);
        ReleaseList.ItemsSource = shownReleases;
        var earlier = releases.Count - shownReleases.Count;
        EarlierReleases.Content = $"查看更早的 {earlier} 个版本";
        EarlierReleases.Visibility = earlier > 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            if (ReleaseList.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject first &&
                FindExpander(first) is { } latest)
                latest.IsExpanded = true;
        };
    }

    /// <summary>Adds the older versions below the ones shown; the open row stays open.</summary>
    private void EarlierClick(object sender, RoutedEventArgs e)
    {
        foreach (var release in releases.Skip(shownReleases.Count)) shownReleases.Add(release);
        EarlierReleases.Visibility = Visibility.Collapsed;
    }

    private static GuideVendor[] Vendors(PresetCategory category) => ProviderCatalog.All
        .Where(preset => preset.Category == category)
        .Select(preset => new GuideVendor(preset.Label.Replace("（免费）", "").Trim(), preset.Overseas,
            category == PresetCategory.Local || FreeVendors.Contains(preset.Id)))
        .ToArray();

    private static Expander? FindExpander(DependencyObject parent)
    {
        if (parent is Expander expander)
            return expander;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindExpander(VisualTreeHelper.GetChild(parent, index)) is { } child)
                return child;
        return null;
    }
}

internal sealed record GuideVendor(string Label, bool Overseas, bool Free);
internal sealed record GuideFeature(string Glyph, string Title, string Text);
internal sealed record GuideQuestion(string Question, string Answer, bool First = false);

/// <summary>The short texts of the guide. docs/user-guide.md says the same; keep them together.</summary>
internal static class GuideContent
{
    public static IReadOnlyList<GuideFeature> Features { get; } = new GuideFeature[]
    {
        new("\uE8BD", "提示词", "读消息之前先报名字，比如“小林说：……”。在联系人卡片上选。"),
        new("\uE716", "多人朗读", "最多 8 人，各用各的音色；几个人同时来消息时排队，依次读。"),
        new("\uE72E", "隐私模式", "只说“微信来消息了”或一句暗语，不读内容。在联系人卡片上打开。"),
        new("\uEA8F", "提醒模式", "消息没回，就按设定的间隔再提醒你；回复了或点「✓ 已读」就停。"),
        new("\uE70F", "词库", "改某个词的读法，比如把缩写读成全称、把表情读成文字。"),
        new("\uE790", "外观", "浅色、深色或跟随系统；主题色有默认、浅绿、浅粉、浅蓝。在「设置 → 外观」里换。"),
    };

    public static IReadOnlyList<GuideQuestion> Questions { get; } = new GuideQuestion[]
    {
        new("没有朗读", "先看左下角是不是「正在监听」。软件只读开始以后、所选联系人新发来的消息，你自己发的不读。", First: true),
        new("提示版本不支持", "电脑微信的版本不在上面的表里。换成表里的版本，再重新打开软件。"),
        new("合成失败", "401、403：Key 填错了或没有权限。402 或「余额不足」：去服务商那里充值。429：请求太快，稍等再试。带地球图标的厂商要在海外网络下使用。"),
        new("听不到声音", "检查「音效」里的输出设备和音量，再看看 Windows 的音量和音量合成器有没有把它静音。"),
    };
}
