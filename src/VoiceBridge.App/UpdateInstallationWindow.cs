using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using VoiceBridge.Core;

namespace VoiceBridge.App;

/// <summary>Runs from staging so progress remains visible after the installed app has exited.</summary>
internal sealed class UpdateInstallationWindow : Window
{
    private readonly TextBlock heading = new() { Text = "正在安装更新", FontSize = 22, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock stage = new() { Text = "正在准备更新…", Margin = new(0,20,0,8) };
    private readonly TextBlock detail = new() { Text = "更新期间请勿关闭电脑。完成后会自动打开新版。", Margin = new(0,12,0,0) };
    private readonly TextBlock elapsed = new() { Margin = new(0,8,0,0), FontSize = 12 };
    private readonly ProgressBar bar = new() { Height = 7, Maximum = 100, IsIndeterminate = true };
    private readonly Button close;
    private readonly Button files;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private bool busy = true;

    public UpdateInstallationWindow()
    {
        Title = "微信语音桥更新"; Width = 500; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = new("Microsoft YaHei UI"); FontSize = 14;
        var panel = new StackPanel { Margin = new(28,8,28,24) };
        panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = $"微信语音桥 v{UpdateProcess.CurrentVersion}", Margin = new(0,6,0,0), Foreground = (Brush)FindResource("Secondary") });
        panel.Children.Add(stage); panel.Children.Add(bar); panel.Children.Add(elapsed); panel.Children.Add(detail);
        bar.Foreground = (Brush)FindResource("Accent"); bar.Background = (Brush)FindResource("FillStrong");
        detail.TextWrapping = TextWrapping.Wrap;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0,22,0,0) };
        files = new Button { Content = "查看更新文件", Style = (Style)FindResource("SecondaryButton"), Visibility = Visibility.Collapsed };
        files.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(UpdateProcess.DirectoryPath) { UseShellExecute = true }); } catch (Exception) { detail.Text += "\n更新目录：" + UpdateProcess.DirectoryPath; } };
        close = new Button { Content = "完成", Style = (Style)FindResource("PrimaryButton"), Visibility = Visibility.Collapsed, Margin = new(10,0,0,0) };
        close.Click += (_, _) => Close(); actions.Children.Add(files); actions.Children.Add(close); panel.Children.Add(actions);
        DialogShell.Apply(this, panel);
        ShowInTaskbar = true;
        Icon = new BitmapImage(new Uri("pack://application:,,,/app-icon.png"));
        TaskbarItemInfo = new() { ProgressState = TaskbarItemProgressState.Indeterminate };
        timer.Tick += (_, _) => elapsed.Text = $"已用时 {clock.Elapsed.Minutes:00}:{clock.Elapsed.Seconds:00}";
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; detail.Text = "更新正在进行，请等待当前操作完成。"; }
        base.OnClosing(e);
    }

    public async Task<int> InstallAsync(string[] args)
    {
        string? error = null;
        var progress = new Progress<InstallationProgress>(value =>
        {
            if (!busy) return;
            stage.Text = value.Stage + (value.Percent > 0 ? $" · {value.Percent}%" : "…");
            bar.IsIndeterminate = value.Percent == 0;
            bar.Value = value.Percent;
            TaskbarItemInfo.ProgressState = value.Percent == 0 ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.Normal;
            TaskbarItemInfo.ProgressValue = value.Percent / 100d;
        });
        int result;
        try { result = await Task.Run(() => UpdateProcess.Apply(args, progress, message => error = message)); }
        catch (Exception exception) { result = 3; error = "更新助手遇到异常：" + exception.Message; }
        busy = false; timer.Stop();
        elapsed.Text = $"已用时 {clock.Elapsed.Minutes:00}:{clock.Elapsed.Seconds:00}";
        bar.IsIndeterminate = false;
        close.Visibility = Visibility.Visible;
        if (result == 0)
        {
            heading.Text = "更新完成";
            stage.Text = $"已更新到 v{UpdateProcess.CurrentVersion}"; bar.Value = 100;
            detail.Text = "新版本已启动，可以关闭此窗口。";
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
        }
        else
        {
            heading.Text = result == 4 ? "启动状态待确认" : "更新未完成";
            stage.Text = result == 4 ? "新版本文件已安装" : "请查看下方原因，处理后重试";
            detail.Text = (error ?? "更新助手未能完成安装。") + "\n更新文件与已有备份不会自动清理，可在更新文件夹中查看。";
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Error;
            files.Visibility = Visibility.Visible; close.Content = "关闭"; Activate();
        }
        return result;
    }
}
