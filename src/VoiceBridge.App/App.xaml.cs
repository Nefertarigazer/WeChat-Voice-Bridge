using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoiceBridge.Core;

namespace VoiceBridge.App;

public partial class App : Application
{
    private IHost? host;
    private SingleInstance? instance;
    private DispatcherTimer? handoffTimer;
    private bool waitingForHandoff;
    private const string AppUserModelId = "WeChatVoiceBridge";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    protected override void OnStartup(StartupEventArgs e)
    {
        // The update helper runs from staging and the restarted build from the install
        // directory, so Windows would otherwise give each process its own taskbar button.
        // One explicit id groups all three windows under the same button.
        try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); }
        catch (Exception) { /* Taskbar grouping is optional; never block startup. */ }
        base.OnStartup(e);
        if (e.Args.Length > 0 && e.Args[0] == "--apply-update")
        {
            var installer = new UpdateInstallationWindow();
            MainWindow = installer;
            installer.Show();
            _ = installer.InstallAsync(e.Args);
            return;
        }
        instance = SingleInstance.TryEnter(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            // While the installation window is still waiting for the user, a
            // second launch must not reveal the new main window either.
            if (waitingForHandoff) return;
            if (MainWindow is not { } window) return;
            window.Show(); window.WindowState = WindowState.Normal; window.Activate();
        })));
        if (instance is null) { Shutdown(); return; }
        var updateNotice = UpdateProcess.ReadRestartNotice(e.Args);
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeChatVoiceBridge");
        // Capture this before the main window initializes settings for a new installation.
        var existingUser = File.Exists(Path.Combine(dataDirectory, "settings.json"));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false
        }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan });
        host = builder.Build();
        host.Start();
        MainWindow = new MainWindow(host.Services.GetRequiredService<HttpClient>(), updateNotice);
        MainWindow.ContentRendered += ShowReleaseNotes;
        var handoff = UpdateHandoff.FromArguments(UpdateProcess.DirectoryPath, e.Args);
        if (handoff is null)
            MainWindow.Show();
        else
            AwaitInstallationConfirmation(handoff, e.Args);

        void ShowReleaseNotes(object? sender, EventArgs args)
        {
            MainWindow.ContentRendered -= ShowReleaseNotes;
            try
            {
                using var state = ReleaseNotesState.TryAcquire(dataDirectory);
                if (state?.ShouldShow(UpdateProcess.CurrentVersion, existingUser) != true)
                    return;
                var dialog = new ReleaseNotesDialog(UpdateProcess.CurrentVersion) { Owner = MainWindow };
                dialog.ShowDialog();
                state.Acknowledge(UpdateProcess.CurrentVersion);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The optional notice must not prevent the main application from opening.
            }
        }
    }

    /// <summary>
    /// The updated build has finished loading but must stay invisible until the user
    /// dismisses the installation window. Signalling readiness first keeps the
    /// installer's "did the new build start?" check (and its rollback on failure).
    /// </summary>
    private void AwaitInstallationConfirmation(UpdateHandoff handoff, string[] args)
    {
        waitingForHandoff = true;
        var previousShutdownMode = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try { handoff.SignalReady(); }
        catch (Exception)
        {
            // A broken handshake must never leave the app invisible.
            RestoreAndShow();
            return;
        }

        handoffTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        handoffTimer.Tick += (_, _) =>
        {
            // "go" is written when the user clicks 完成 or closes the helper window.
            // If the helper vanished without confirming (crash / force kill), showing
            // the new build is still the safe outcome.
            if (!handoff.ShouldShowMainWindow() && UpdateProcess.HelperStillRunning(args)) return;
            handoffTimer!.Stop();
            RestoreAndShow();
        };
        handoffTimer.Start();

        void RestoreAndShow()
        {
            waitingForHandoff = false;
            ShutdownMode = previousShutdownMode;
            try { handoff.Cleanup(); } catch (Exception) { }
            MainWindow?.Show();
            MainWindow?.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
