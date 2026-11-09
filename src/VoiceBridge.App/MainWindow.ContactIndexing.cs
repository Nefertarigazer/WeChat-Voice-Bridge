using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using VoiceBridge.WeChat;

namespace VoiceBridge.App;

public partial class MainWindow
{
    private CancellationTokenSource? indexCancellation;
    private Task indexWork = Task.CompletedTask;
    private FileSystemWatcher? contactIndexWatcher;
    private DispatcherTimer? contactIndexDebounce;

    // Explicitly enabled by the user's index policy: account confirmation can build metadata in IDLE.
    // This never starts a listening session or accesses message bodies.
    private void RestartContactIndexing()
    {
        try { RestartContactIndexingCore(); }
        catch (Exception) { _ = StopContactIndexingAsync(); }
    }

    private void RestartContactIndexingCore()
    {
        if (!ready || closing || ListeningActive) return;
        var previous = StopContactIndexingAsync();
        WeChatSession account;
        try { account = BindWeChat(); }
        catch (Exception)
        {
            // WeChat may be opened after this app. Retry identification only, not database access.
            var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            contactIndexDebounce = retry;
            retry.Tick += (_, _) => { retry.Stop(); RestartContactIndexing(); };
            retry.Start(); return;
        }
        var folder = Path.Combine(account.Root, account.Account, "db_storage", "contact");
        if (!Directory.Exists(folder)) return;
        var cancel = new CancellationTokenSource();
        indexCancellation = cancel;
        // Watch only the main database. WAL/SHM churn is constant while WeChat is
        // writing (and especially during a call) and must not tear down the helper.
        var watcher = new FileSystemWatcher(folder)
        {
            Filter = "contact.db", NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = false
        };
        contactIndexWatcher = watcher;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        contactIndexDebounce = timer;
        // Rebuilding the index starts a browse Worker, and each new Worker re-runs the
        // audited key scan on every WeChat process. Keep that rare and never on a timer
        // tighter than a few minutes.
        const int RefreshMinutes = 5;
        var nextRefresh = DateTimeOffset.UtcNow.AddMinutes(RefreshMinutes);
        void QueueRefresh()
        {
            if (closing || ListeningActive || cancel.IsCancellationRequested || !ReferenceEquals(indexCancellation, cancel)) return;
            timer.Stop();
            var delay = nextRefresh - DateTimeOffset.UtcNow;
            timer.Interval = delay > TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1);
            timer.Start();
        }
        FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(new Action(QueueRefresh));
        watcher.Changed += changed; watcher.Created += changed; watcher.Deleted += changed;
        watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(new Action(QueueRefresh));
        watcher.Error += (_, _) => Dispatcher.BeginInvoke(new Action(QueueRefresh));
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (cancel.IsCancellationRequested || !ReferenceEquals(indexCancellation, cancel)) return;
            if (!indexWork.IsCompleted) { QueueRefresh(); return; }
            nextRefresh = DateTimeOffset.UtcNow.AddMinutes(RefreshMinutes);
            indexWork = BuildContactIndexAsync(account, cancel.Token);
        };
        watcher.EnableRaisingEvents = true;
        indexWork = BuildAfterPreviousAsync();
        async Task BuildAfterPreviousAsync()
        {
            await previous;
            await BuildContactIndexAsync(account, cancel.Token);
        }
    }

    private async Task BuildContactIndexAsync(WeChatSession account, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (closing || ListeningActive) return;
            await WithDirectoryAsync(account, async (reader, cancellation) =>
            {
                // Search uses the complete verified metadata index; an unchanged saved index needs no Worker.
                await reader.SearchAsync(account, "", cancellation);
                return true;
            }, token);
            // Profiles saved by builds before avatar support have no AvatarUrl. The
            // verified metadata index already holds the real URL, so recover it here
            // without a new database Worker or any extra contact search.
            await Dispatcher.InvokeAsync(() => RestoreMissingAvatars(account));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Optional acceleration: the normal search path still reports actual errors. */ }
        finally
        {
            try { await CloseDirectoryAsync(); }
            catch (Exception) { /* A foreground operation can recreate the helper. */ }
        }
    }

    private async Task StopContactIndexingAsync()
    {
        contactIndexDebounce?.Stop(); contactIndexDebounce = null;
        contactIndexWatcher?.Dispose(); contactIndexWatcher = null;
        var cancel = indexCancellation;
        indexCancellation = null;
        cancel?.Cancel();
        var work = indexWork;
        try { await work; }
        catch (Exception) { /* An optional index update must never prevent stopping a session. */ }
        finally { cancel?.Dispose(); }
    }
}
