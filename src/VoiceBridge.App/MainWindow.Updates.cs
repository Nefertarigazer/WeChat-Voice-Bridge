using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VoiceBridge.Core;

namespace VoiceBridge.App;

public partial class MainWindow
{
    private readonly HttpClient updateHttp = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    private UpdateManifest? availableUpdate;
    private string? verifiedUpdate;
    private string? stagedUpdate;
    private CancellationTokenSource? updateCancel;
    private Task? updateWork;
    private bool restartConfirmed;
    private bool updateHandoff;

    private void InitializeUpdates(string? restartNotice)
    {
        UpdateVersion.Text = $"当前版本 v{UpdateProcess.CurrentVersion}";
        if (restartNotice is not null)
            UpdateStatus.Text = restartNotice;
        else
            Loaded += FirstUpdateCheck;
    }

    private void FirstUpdateCheck(object sender, RoutedEventArgs e)
    {
        Loaded -= FirstUpdateCheck;
        _ = RunUpdateAsync(CheckUpdateAsync);
    }

    private async void SidebarUpdateClick(object sender, RoutedEventArgs e)
    {
        NavAdvanced.IsChecked = true;
        ContentScroller.ScrollToTop();
        if (verifiedUpdate is null)
            await RunUpdateActionAsync();
    }

    private async void UpdateClick(object sender, RoutedEventArgs e) => await RunUpdateActionAsync();

    private Task RunUpdateActionAsync() => availableUpdate is null ? RunUpdateAsync(CheckUpdateAsync)
        : verifiedUpdate is null ? RunUpdateAsync(DownloadUpdateAsync) : RunUpdateAsync(InstallUpdateAsync);

    private async Task RunUpdateAsync(Func<CancellationToken, Task> action)
    {
        if (closing || updateCancel != null)
            return;

        using var cancellation = new CancellationTokenSource();
        updateCancel = cancellation;
        UpdateAction.IsEnabled = false;
        try
        {
            updateWork = action(cancellation.Token);
            await updateWork;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            UpdateStatus.Text = "已取消，下载进度已保留，下次可继续。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or
            HttpRequestException or UnauthorizedAccessException or Win32Exception)
        {
            UpdateStatus.Text = error switch
            {
                HttpRequestException { StatusCode: { } code } => $"更新服务返回 HTTP {(int)code}，请稍后重试。",
                HttpRequestException => "无法连接更新服务，请检查网络后重试。",
                UnauthorizedAccessException or Win32Exception => "无法写入程序或启动更新，请检查安装目录权限。",
                InvalidDataException or TimeoutException => error.Message,
                _ => $"更新未完成：{error.Message}"
            };
        }
        finally
        {
            updateWork = null;
            updateCancel = null;
            UpdateProgressRow.Visibility = Visibility.Collapsed;
            RenderUpdate();
        }
    }

    private async Task CheckUpdateAsync(CancellationToken cancellationToken)
    {
        UpdateStatus.Text = "正在检查更新…";
        var client = new UpdateClient(updateHttp, UpdateProcess.CurrentVersion);
        // Recover locally first: a complete verified package remains installable even offline.
        availableUpdate ??= await client.ReadPendingAsync(UpdateProcess.DirectoryPath, cancellationToken);
        if (availableUpdate is not null)
        {
            verifiedUpdate = await client.GetCachedAsync(availableUpdate, UpdateProcess.DirectoryPath, cancellationToken);
            UpdateStatus.Text = verifiedUpdate is null ? "发现上次未完成的下载，可继续下载。" : "已恢复下载完成的更新，可直接重启安装。";
            RenderUpdate();
        }
        var newest = await client.CheckAsync(cancellationToken);
        if (newest is not null && (availableUpdate is null ||
            UpdateManifest.CompareVersions(newest.Version, availableUpdate.Version) > 0 ||
            newest.Version == availableUpdate.Version && newest.Sha256 != availableUpdate.Sha256))
        {
            availableUpdate = newest;
            verifiedUpdate = await client.GetCachedAsync(newest, UpdateProcess.DirectoryPath, cancellationToken);
            DeleteUpdateStage();
        }
        if (availableUpdate is not null && verifiedUpdate is not null)
        { UpdateStatus.Text = "更新包已下载并通过校验，可直接重启安装。"; return; }
        UpdateStatus.Text = availableUpdate is null
            ? $"已是最新 v{UpdateProcess.CurrentVersion}" : "发现新版本。";
    }

    private async Task DownloadUpdateAsync(CancellationToken cancellationToken)
    {
        var manifest = availableUpdate!;
        UpdateStatus.Text = $"正在下载 v{manifest.Version}…";
        UpdateProgress.Value = 0;
        UpdateProgress.IsIndeterminate = false;
        UpdatePercent.Text = "0%";
        UpdateCancelButton.IsEnabled = true;
        UpdateCancelButton.Visibility = Visibility.Visible;
        UpdateProgressRow.Visibility = Visibility.Visible;
        var progress = new Progress<int>(percent =>
        {
            if (!cancellationToken.IsCancellationRequested && UpdateProgressRow.Visibility == Visibility.Visible)
            {
                UpdateProgress.Value = percent;
                UpdatePercent.Text = $"{percent}%";
            }
        });
        var client = new UpdateClient(updateHttp, UpdateProcess.CurrentVersion);
        verifiedUpdate = await client.DownloadAsync(manifest, UpdateProcess.DirectoryPath, progress, cancellationToken);
        UpdateStatus.Text = "下载完成，已通过大小与 SHA-256 校验。";
    }

    private void CancelUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateCancelButton.IsEnabled = false;
        updateCancel?.Cancel();
    }

    private bool ListeningOrPlaying => manualCancel != null || manualTask != null ||
        controller.Status.State is BridgeState.Starting or BridgeState.Running or BridgeState.Stopping;

    private bool ConfirmUpdateRestart()
    {
        if (!ListeningOrPlaying || restartConfirmed)
            return true;

        restartConfirmed = true;
        UpdateStatus.Text = "正在监听或播放。确认后将先结束监听和播放，再重启安装。";
        UpdateKeepWorking.Visibility = Visibility.Visible;
        return false;
    }

    private void KeepWorkingClick(object sender, RoutedEventArgs e)
    {
        restartConfirmed = false;
        UpdateKeepWorking.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = "已保留当前会话。可以稍后重启安装。";
        RenderUpdate();
    }

    private async Task InstallUpdateAsync(CancellationToken cancellationToken)
    {
        if (!ConfirmUpdateRestart())
            return;

        UpdateKeepWorking.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = "正在准备安装文件…";
        UpdateProgressRow.Visibility = Visibility.Visible;
        UpdateProgress.IsIndeterminate = true;
        UpdatePercent.Text = "准备中";
        UpdateCancelButton.Visibility = Visibility.Visible;
        var installProgress = new Progress<InstallationProgress>(progress =>
        {
            if (closing || cancellationToken.IsCancellationRequested) return;
            UpdateStatus.Text = progress.Stage + "…";
            UpdateProgress.IsIndeterminate = false;
            UpdateProgress.Value = progress.Percent;
            UpdatePercent.Text = progress.Percent + "%";
        });
        try
        {
            stagedUpdate ??= await Task.Run(() => UpdateInstaller.Prepare(verifiedUpdate!, availableUpdate!,
                AppContext.BaseDirectory, UpdateProcess.DirectoryPath, cancellationToken, installProgress), cancellationToken);
            UpdateProcess.ValidatePackageVersion(stagedUpdate, availableUpdate!.Version);
            UpdateProgress.Value = 100;
            UpdateProgress.IsIndeterminate = false;
            UpdateCancelButton.Visibility = Visibility.Collapsed;
        }
        catch (InvalidDataException)
        {
            DeleteUpdateArchive();
            DeleteUpdateStage();
            restartConfirmed = false;
            throw;
        }
        finally { UpdateProgress.IsIndeterminate = false; }

        if (closing || !ConfirmUpdateRestart())
            return;

        closing = true;
        Render();
        saveTimer.Stop();
        var stopped = false;
        try
        {
            UpdateStatus.Text = "正在结束监听和播放…";
            await StopAsync().WaitAsync(TimeSpan.FromSeconds(8));
            stopped = true;
            SaveNow();
            UpdateStatus.Text = "正在启动更新助手…";
            await UpdateProcess.StartAsync(stagedUpdate, cancellationToken);
            updateHandoff = true;
        }
        catch (TimeoutException error)
        {
            UpdateStatus.Text = stopped ? error.Message : "监听或播放尚未完全结束。请稍后重试安装。";
        }
        finally
        {
            restartConfirmed = false;
            closing = false;
            if (!updateHandoff) saveTimer.Start();
            Render();
        }

        if (updateHandoff)
        {
            UpdateStatus.Text = "正在关闭程序，随后安装并重启…";
            IsEnabled = false;
            Close();
        }
    }

    private void RenderUpdate()
    {
        var manifest = availableUpdate;
        SidebarUpdate.Visibility = manifest is null ? Visibility.Collapsed : Visibility.Visible;
        SidebarUpdate.ToolTip = manifest is null ? null : $"查看 v{manifest.Version} 更新";
        var label = verifiedUpdate is not null ? (restartConfirmed ? "确认结束并重启" : "重启安装更新")
            : manifest is null ? "检查更新" : $"更新到 v{manifest.Version}";
        // Three states, three distinct colours: grey "up to date", mint outline
        // "download available", solid green "ready to install". The button keeps
        // one exact 36 DIP height in every state; the label colour comes from the
        // style, so a plain string is enough and vertical centring stays exact.
        var style = manifest is null ? "SecondaryButton"
            : verifiedUpdate is null ? "UpdateReadyButton" : "UpdateInstallButton";
        var ink = manifest is null ? "Label" : verifiedUpdate is null ? "UpdateReadyText" : "WhiteText";
        UpdateAction.Style = (Style)FindResource(style);
        // A plain string is enough for centring, but the label ink must be set
        // explicitly: a ContentPresenter does not always inherit the template's
        // Foreground, which would leave white text dark on the solid green state.
        UpdateAction.Foreground = (Brush)FindResource(ink);
        UpdateAction.Content = label;
        // The dot reads green "up to date", red "update available", orange "ready to
        // install". The bridge status pill keeps its own grey/amber/green mapping, so
        // the shared brushes are reused in this order instead of being redefined.
        UpdateStateDot.Fill = manifest is null ? LiveDot : verifiedUpdate is not null ? WarnDot : ErrorDot;
        UpdateAction.IsEnabled = !closing && !updateHandoff && updateCancel is null;
        UpdatePackage.Visibility = manifest is null ? Visibility.Collapsed : Visibility.Visible;
        // The title row shares the header grid with the status row, so collapsing it is
        // what keeps the button centred on the card alone when there is no update.
        UpdateNoteHeader.Visibility = manifest is null ? Visibility.Collapsed : Visibility.Visible;
        if (manifest is null)
            return;

        // The version heads its own notes as a title; the size rides beside it as
        // quiet metadata on the same baseline (two leading spaces stand in for the
        // gap that inline Runs cannot express as a margin).
        UpdateNoteTitle.Text = $"v{manifest.Version}";
        UpdateNoteSize.Text = $"  {manifest.Size / 1_000_000d:0.0} MB";
        UpdatePackage.ToolTip = $"v{manifest.Version} · {manifest.Size / 1_000_000d:0.0} MB";
        FillNotes(manifest.Notes);
    }

    /// <summary>
    /// Renders the manifest notes as one accent-dotted line per change. A single
    /// dense paragraph read as a wall of text under the status row; split lines
    /// keep long release notes scannable inside the soft inset panel.
    /// </summary>
    private void FillNotes(string notes)
    {
        UpdateNotesPanel.Children.Clear();
        var bullet = (Brush)FindResource("Accent");
        var ink = (Brush)FindResource("Label");
        var lines = notes.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(line => line.Trim())
                         .Where(line => line.Length > 0)
                         .ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, i == lines.Count - 1 ? 0 : 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Ellipse
            {
                Width = 5, Height = 5, Fill = bullet,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 7, 0, 0)
            });
            var text = new TextBlock
            {
                Text = lines[i], FontSize = 12, Foreground = ink,
                TextWrapping = TextWrapping.Wrap, LineHeight = 19
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            UpdateNotesPanel.Children.Add(row);
        }
        // No notes means no title either: the header row then collapses and the button
        // centres on the status row alone, exactly as it does with no update at all.
        UpdateNotesBox.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateNoteHeader.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DeleteUpdateArchive()
    {
        if (verifiedUpdate is null)
            return;

        try { File.Delete(verifiedUpdate); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        verifiedUpdate = null;
    }

    private async Task CloseUpdatesAsync()
    {
        if (!updateHandoff)
        {
            updateCancel?.Cancel();
            if (updateWork is { } work)
            {
                try { await work.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception) { /* Retain partial bytes and verified archives for the next launch. */ }
            }
            DeleteUpdateStage();
        }
        updateHttp.Dispose();
    }

    private void DeleteUpdateStage()
    {
        if (stagedUpdate is null)
            return;
        try { Directory.Delete(stagedUpdate, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        stagedUpdate = null;
    }
}
