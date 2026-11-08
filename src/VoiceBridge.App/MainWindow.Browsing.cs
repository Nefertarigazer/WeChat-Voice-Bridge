using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceBridge.Core;
using VoiceBridge.WeChat;

namespace VoiceBridge.App;

public partial class MainWindow
{
    private readonly SemaphoreSlim browseGate = new(1);
    private string browseBinding = "";

    private async Task<T> WithDirectoryAsync<T>(WeChatSession wechat,
        Func<WeChatDirectory, CancellationToken, Task<T>> action, CancellationToken token)
    {
        await browseGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var binding = wechat.Root + "\0" + wechat.Account + "\0" + string.Join(',', wechat.Pids.Order());
            if (directory != null && browseBinding != binding)
            {
                await directory.DisposeAsync();
                directory = null;
            }
            directory ??= new WeChatDirectory(PythonFixtureSource.ResolvePython(), WorkerScript, () => CompleteReadings());
            browseBinding = binding;
            try { return await action(directory, token); }
            catch
            {
                await directory.DisposeAsync();
                directory = null;
                browseBinding = "";
                throw;
            }
        }
        finally { browseGate.Release(); }
    }

    private async Task CloseDirectoryAsync(CancellationToken token = default)
    {
        await browseGate.WaitAsync(token);
        try
        {
            if (directory == null) return;
            await directory.DisposeAsync();
            directory = null;
            browseBinding = "";
        }
        finally { browseGate.Release(); }
    }

    private sealed class SessionSource(MainWindow owner, PythonFixtureSource inner) : IMessageSource
    {
        public async Task PrepareAsync(SessionStamp stamp, Target target, CancellationToken token)
        {
            await owner.StopContactIndexingAsync();
            await owner.CloseDirectoryAsync(token);
            token.ThrowIfCancellationRequested();
            await inner.PrepareAsync(stamp, target, token);
        }
        public Task RunAsync(Func<ChatMessage, Admission> admit, CancellationToken token) => inner.RunAsync(admit, token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
