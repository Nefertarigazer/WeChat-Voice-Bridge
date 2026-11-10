using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceBridge.WeChat;

namespace VoiceBridge.App;

/// <summary>
/// User-facing gate for WeChat builds outside the audited Config.Cipher band.
/// The worker also refuses such versions (code "unsupported_version") before it
/// opens any process handle; this dialog makes the refusal visible instead of
/// leaving it as a line of status text.
/// </summary>
internal static class VersionGate
{
    private static bool shown;

    public static bool IsUnsupported(string message) =>
        message.Contains("不支持此版本", StringComparison.Ordinal);

    /// <summary>Startup check: only when a Weixin client is running and its file version is readable.</summary>
    public static void TryShow(Window owner)
    {
        if (shown) return;
        var (raw, parsed) = WeChatSession.DetectClientVersion();
        if (raw is null) return;                       // WeChat not running yet.
        if (parsed is not null && WeChatSession.IsSupportedVersion(parsed)) return;
        ShowFor(owner, $"检测到微信版本：{raw}。");
    }

    /// <summary>Explicit refusal from WeChatSession.Bind, which already carries the full Chinese reason.</summary>
    public static void ShowFor(Window owner, string detail)
    {
        if (shown) return;
        shown = true;
        var dialog = new VersionGateDialog(detail) { Owner = owner };
        dialog.ShowDialog();
        if (dialog.WantsExit)
            Application.Current.Shutdown();
    }
}

internal sealed class VersionGateDialog : Window
{
    public bool WantsExit { get; private set; }

    public VersionGateDialog(string detail)
    {
        Title = "微信版本不受支持";
        Width = 470; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14;

        var panel = new StackPanel { Margin = new(28, 8, 28, 24) };
        panel.Children.Add(new TextBlock
        {
            Text = "微信版本不受支持", FontSize = 22, FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"当前仅支持微信 Windows {WeChatSession.MinSupportedVersion} 至 {WeChatSession.MaxSupportedVersion}。",
            Margin = new(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("Label")
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail, Margin = new(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap,
            FontSize = 13, Foreground = (Brush)FindResource("Secondary")
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 24, 0, 0)
        };
        var quit = new Button
        {
            Content = "退出应用", Style = (Style)FindResource("SecondaryButton"), Margin = new(0, 0, 10, 0)
        };
        quit.Click += (_, _) => { WantsExit = true; Close(); };
        var update = new Button { Content = "更新微信", Style = (Style)FindResource("PrimaryButton") };
        update.Click += (_, _) =>
        {
            // Opening the official download page is enough guidance; the user can
            // restart the bridge after updating WeChat.
            try { Process.Start(new ProcessStartInfo("https://pc.weixin.qq.com/") { UseShellExecute = true }); }
            catch (Exception) { /* No browser association: the text above still explains the range. */ }
            Close();
        };
        actions.Children.Add(quit);
        actions.Children.Add(update);
        panel.Children.Add(actions);
        DialogShell.Apply(this, panel);
    }
}
