using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceBridge.Core;
using VoiceBridge.WeChat;

namespace VoiceBridge.App;

/// <summary>
/// User-facing gate for WeChat builds outside the audited Config.Cipher band.
/// The worker also refuses such versions (code "unsupported_version") before it
/// opens any process handle; this dialog makes the refusal visible instead of
/// leaving it as a line of status text.
/// </summary>
/// <remarks>
/// The gate never blocks startup: it runs after the main window has rendered and
/// asks the ordinary update channel whether a newer bridge exists before it
/// decides what to promise. A newer bridge is the fix the user actually needs, so
/// it is the primary action and runs the same in-app update flow as the settings
/// page. Closing the box quits the application — that is the owner's decision —
/// except when the user already chose a remediation.
/// </remarks>
internal static class VersionGate
{
    /// <summary>
    /// Test-only override for acceptance runs on a real machine: any non-empty value
    /// is used as the detected WeChat version <em>for this gate only</em>. The worker
    /// still binds against the real client, so this can never make an unsupported
    /// client actually work. Not part of the user documentation.
    /// </summary>
    internal const string TestVersionVariable = "WVB_TEST_WECHAT_VERSION";

    /// <summary>One gate session per run: a dismissed gate must not return on every 开始.</summary>
    private static bool started;

    public static bool IsUnsupported(string message) =>
        message.Contains("不支持此版本", StringComparison.Ordinal);

    /// <summary>
    /// Startup check, run once the main window is visible: detect the client, then
    /// let the update channel answer before anything is shown. Returns true when a
    /// gate was displayed, so the caller can hold back the release notes.
    /// </summary>
    public static async Task<bool> TryShowOnStartupAsync(Window owner, Func<Task<UpdateManifest?>>? updateProbe)
    {
        if (started) return false;
        started = true;
        try
        {
            var (raw, parsed) = await Task.Run(DetectVersion);
            if (raw is null) { started = false; return false; }      // WeChat not running yet.
            if (WeChatSession.IsSupportedVersion(parsed)) { started = false; return false; }
            var manifest = await ProbeAsync(updateProbe);
            return Show(owner, raw, parsed, manifest, detail: null);
        }
        catch (Exception)
        {
            // The gate is advisory; a failure here must never break startup.
            started = false;
            return false;
        }
    }

    /// <summary>
    /// Explicit refusal from WeChatSession.Bind, which already carries the full
    /// Chinese reason. The same decision applies, so the box can still offer the
    /// bridge update it needs instead of only telling the user to update WeChat.
    /// </summary>
    public static async Task ShowForAsync(Window owner, string detail,
        Func<Task<UpdateManifest?>>? updateProbe = null)
    {
        if (started) return;
        started = true;
        try
        {
            var (raw, parsed) = await Task.Run(DetectVersion);
            var manifest = await ProbeAsync(updateProbe);
            // The worker refused the client, so an unreadable version must still be shown.
            Show(owner, raw ?? "未知", parsed, manifest, detail);
        }
        catch (Exception) { /* Never let the gate break the caller's error path. */ }
    }

    private static (string? Raw, Version? Parsed) DetectVersion()
    {
        var overridden = Environment.GetEnvironmentVariable(TestVersionVariable)?.Trim();
        if (!string.IsNullOrEmpty(overridden))
            return (overridden, Version.TryParse(overridden, out var test) ? test : null);
        return WeChatSession.DetectClientVersion();
    }

    private static async Task<UpdateManifest?> ProbeAsync(Func<Task<UpdateManifest?>>? probe)
    {
        if (probe is null) return null;
        try { return await probe(); }
        catch (Exception) { return null; }   // Offline is fine: the gate still explains the refusal.
    }

    private static bool Show(Window owner, string raw, Version? parsed, UpdateManifest? manifest, string? detail)
    {
        var decision = VersionGatePolicy.Decide(raw, parsed,
            WeChatSession.MinSupportedVersion, WeChatSession.MaxSupportedVersion,
            manifest?.WeChatSupport, UpdateProcess.CurrentVersion, manifest?.Version);
        if (!decision.Show) return false;
        var dialog = new VersionGateDialog(decision, detail) { Owner = owner };
        dialog.ShowDialog();
        if (dialog.Exit == VersionGateExit.ExitApplication)
            Application.Current.Shutdown();
        return true;
    }
}

internal sealed class VersionGateDialog : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly HttpClient http = UpdateProcess.CreateUpdateHttp();
    private readonly TextBlock status;
    private readonly StackPanel progressRow;
    private readonly ProgressBar progress;
    private readonly TextBlock percent;
    private readonly Button bridgeButton;

    /// <summary>True once updating the bridge or opening the WeChat page was chosen.</summary>
    private bool remediationChosen;
    private bool installStarted;
    private bool busy;

    /// <summary>What the caller must do after the box closes.</summary>
    public VersionGateExit Exit { get; private set; } = VersionGateExit.ExitApplication;

    public VersionGateDialog(VersionGateDecision decision, string? detail)
    {
        Title = "微信版本不受支持";
        Width = 480; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14;

        var client = decision.ClientVersion ?? "未知";
        var panel = new StackPanel { Margin = new(28, 8, 28, 24) };
        panel.Children.Add(new TextBlock
        {
            Text = "微信版本不受支持", FontSize = 22, FontWeight = FontWeights.SemiBold
        });
        // The first paragraph carries the conclusion, so it differs by case: whether
        // updating the bridge is the fix, or the bridge itself is already the latest.
        panel.Children.Add(new TextBlock
        {
            Text = Advice(decision, client), Margin = new(0, 14, 0, 0),
            FontSize = 15, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("Label")
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"当前支持微信 Windows {WeChatSession.MinSupportedVersion} 至 {WeChatSession.MaxSupportedVersion}，" +
                $"检测到微信版本：{client}。",
            Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("Label")
        });
        panel.Children.Add(new TextBlock
        {
            Text = RangeAdvice(decision),
            Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            FontSize = 13, Foreground = (Brush)FindResource("Secondary")
        });
        if (detail is not null)
            panel.Children.Add(new TextBlock
            {
                Text = detail, Margin = new(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
                FontSize = 12, Foreground = (Brush)FindResource("Secondary")
            });

        status = new TextBlock
        {
            Margin = new(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 13,
            Foreground = (Brush)FindResource("Secondary"), Visibility = Visibility.Collapsed
        };
        panel.Children.Add(status);
        progressRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };
        progress = new ProgressBar { Width = 300, Height = 6, Minimum = 0, Maximum = 100 };
        percent = new TextBlock
        {
            Text = "0%", FontSize = 12, Margin = new(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("Secondary")
        };
        progressRow.Children.Add(progress); progressRow.Children.Add(percent);
        panel.Children.Add(progressRow);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 22, 0, 0)
        };
        var quit = new Button
        {
            Content = "退出应用", Style = (Style)FindResource("SecondaryButton"), Margin = new(0, 0, 10, 0)
        };
        quit.Click += (_, _) => { Exit = VersionGateExit.ExitApplication; Close(); };
        AutomationProperties.SetHelpText(quit, "关闭这个提示就退出应用");
        actions.Children.Add(quit);
        // Only a client below the supported band can be fixed by updating WeChat;
        // above it a newer WeChat is further out of range, so the button would mislead.
        if (decision.OffersWeChatUpdate)
        {
            var wechat = new Button
            {
                Content = "更新微信", Style = (Style)FindResource("SecondaryButton"), Margin = new(0, 0, 10, 0)
            };
            wechat.Click += (_, _) => UpdateWeChat();
            actions.Children.Add(wechat);
        }
        bridgeButton = new Button
        {
            Content = decision.BridgeVersion is { } version ? $"更新语音桥到 v{version}" : "更新语音桥",
            Style = (Style)FindResource("PrimaryButton"),
            IsDefault = true
        };
        bridgeButton.Click += (_, _) => UpdateBridge();
        actions.Children.Add(bridgeButton);
        panel.Children.Add(actions);
        DialogShell.Apply(this, panel);
    }

    private static string Advice(VersionGateDecision decision, string client)
    {
        if (decision.Reason == VersionGateReason.VersionUnreadable)
            return decision.BridgeVersion is { } known
                ? $"有可用的更新 v{known}，但读不到微信版本，更新前无法确认是否支持。"
                : $"语音桥已是最新（v{UpdateProcess.CurrentVersion}），但读不到微信版本，无法确认是否支持。";
        if (decision.BridgeVersion is { } version)
            return decision.BridgeUpdateCoversWeChat
                ? $"更新语音桥即可解决：v{version} 已支持微信 Windows {client}。"
                : $"已发布 v{version}，但目前仍不支持微信 Windows {client}。";
        return decision.Reason == VersionGateReason.WeChatTooOld
            ? $"微信 Windows {client} 低于支持范围，需要更新微信。语音桥已是最新（v{UpdateProcess.CurrentVersion}）。"
            : $"语音桥已是最新（v{UpdateProcess.CurrentVersion}），暂时还不支持微信 Windows {client}。";
    }

    private static string RangeAdvice(VersionGateDecision decision)
    {
        if (decision.Reason == VersionGateReason.VersionUnreadable)
            return "请在微信「设置 → 关于微信」里确认版本号；读不到版本时不会读取任何消息。";
        if (decision.BridgeVersion is not null && decision.BridgeUpdateCoversWeChat)
            return "点「更新语音桥」会在这里下载并重启安装，更新后重新开始监听即可。";
        if (decision.BridgeVersion is not null)
            return "新版微信正在适配中；适配完成后会再发布新版本，届时在这里点「更新语音桥」即可。";
        return decision.Reason == VersionGateReason.WeChatTooOld
            ? "更新微信到支持范围内的版本后重新开始即可；也可以顺手把语音桥更新到最新。"
            : "新版微信正在适配中；适配完成后会发布新版本，届时在这里点「更新语音桥」即可。";
    }

    /// <summary>
    /// Same flow as 设置 → 软件更新: check the manifest, download with verification,
    /// prepare the staging copy and hand it to the update helper. Nothing is
    /// re-implemented here beyond the dialog's own progress lines, and every
    /// outcome — no update, failure, success — is written into the box.
    /// </summary>
    private async void UpdateBridge()
    {
        // The user already chose the remediation, so closing later must not quit.
        remediationChosen = true;
        if (busy) return;
        busy = true;
        bridgeButton.IsEnabled = false;
        try
        {
            var client = new UpdateClient(http, UpdateProcess.CurrentVersion);
            SetStatus("正在检查更新…");
            var manifest = await client.CheckAsync(cancellation.Token);
            if (manifest is null)
            {
                SetStatus($"语音桥已是最新（v{UpdateProcess.CurrentVersion}），暂时没有可安装的更新。" +
                    "新版微信适配完成后会在这里提供更新。");
                return;
            }
            SetStatus($"发现 v{manifest.Version}，正在下载…");
            progressRow.Visibility = Visibility.Visible;
            progress.Value = 0; percent.Text = "0%";
            var download = new Progress<int>(value =>
            {
                if (cancellation.IsCancellationRequested) return;
                progress.Value = value;
                percent.Text = $"{value}%";
            });
            var archive = await client.DownloadAsync(manifest, UpdateProcess.DirectoryPath, download, cancellation.Token);
            SetStatus("下载完成，正在准备安装文件…");
            progress.IsIndeterminate = true;
            percent.Text = "准备中";
            var install = new Progress<InstallationProgress>(value =>
            {
                if (cancellation.IsCancellationRequested) return;
                progress.IsIndeterminate = false;
                progress.Value = value.Percent;
                percent.Text = $"{value.Percent}%";
                SetStatus(value.Stage + "…");
            });
            var stage = await Task.Run(() => UpdateInstaller.Prepare(archive, manifest,
                AppContext.BaseDirectory, UpdateProcess.DirectoryPath, cancellation.Token, install), cancellation.Token);
            UpdateProcess.ValidatePackageVersion(stage, manifest.Version);
            progress.IsIndeterminate = false;
            progress.Value = 100; percent.Text = "100%";
            SetStatus("正在启动更新助手…");
            await UpdateProcess.StartAsync(stage, cancellation.Token);
            installStarted = true;
            SetStatus("正在关闭程序，随后安装并重启…");
            Exit = VersionGateExit.ExitApplication;
            Close();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Closing the box during the download keeps the partial file for next time.
        }
        catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or
            HttpRequestException or UnauthorizedAccessException or Win32Exception)
        {
            SetStatus(Describe(error), failed: true);
        }
        catch (Exception error)
        {
            SetStatus($"更新未完成：{error.Message}", failed: true);
        }
        finally
        {
            busy = false;
            if (IsVisible)
            {
                progressRow.Visibility = Visibility.Collapsed;
                progress.IsIndeterminate = false;
                // The action is repeatable: a failed or interrupted download resumes,
                // and a check that found nothing can be repeated later.
                bridgeButton.IsEnabled = true;
            }
        }
    }

    private static string Describe(Exception error) => error switch
    {
        HttpRequestException { StatusCode: { } code } => $"更新服务返回 HTTP {(int)code}，请稍后重试。",
        HttpRequestException => "暂时连不上更新服务（服务器没有完成握手）。稍后再点一次「更新语音桥」即可。",
        UnauthorizedAccessException or Win32Exception => "无法写入程序或启动更新，请检查安装目录权限。",
        _ => $"更新未完成：{error.Message}"
    };

    private void UpdateWeChat()
    {
        try { Process.Start(new ProcessStartInfo("https://pc.weixin.qq.com/") { UseShellExecute = true }); }
        catch (Exception)
        {
            SetStatus("无法打开浏览器，请手动访问 https://pc.weixin.qq.com/ 更新微信。", failed: true);
            return;
        }
        remediationChosen = true;
        Exit = VersionGateExit.KeepRunning;
        Close();
    }

    private void SetStatus(string text, bool failed = false)
    {
        status.Text = text;
        status.Visibility = Visibility.Visible;
        status.Foreground = (Brush)FindResource(failed ? "Danger" : "Secondary");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!installStarted)
            Exit = VersionGatePolicy.OnDialogClosed(remediationChosen, installStarted);
        // Stops a download that is still running; its partial file stays resumable.
        cancellation.Cancel();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // The token source is deliberately not disposed: the cancelled download still
        // unwinds in the background and would hit an ObjectDisposedException instead.
        http.Dispose();
        base.OnClosed(e);
    }
}
