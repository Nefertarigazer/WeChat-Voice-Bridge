using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoiceBridge.Core;

namespace VoiceBridge.App;

public partial class App : Application
{
    private IHost? host;
    private SingleInstance? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
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
        MainWindow.Show();

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

    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
