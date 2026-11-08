using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceBridge.App;

/// <summary>One interactive instance per Windows user/session; update helpers bypass this gate.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop = new();
    private readonly Task listener;
    private SingleInstance(Mutex mutex, string name, Action activate)
    {
        this.mutex = mutex;
        listener = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    var command = new byte[1];
                    if (await pipe.ReadAsync(command, deadline.Token) == 1 && command[0] == 1) activate();
                }
                catch (OperationCanceledException) { }
                catch (IOException)
                {
                    try { await Task.Delay(100, stop.Token); } catch (OperationCanceledException) { break; }
                }
            }
        });
    }
    public static SingleInstance? TryEnter(Action activate)
    {
        // Session ID keeps independent Remote Desktop sessions independent, without an open network port.
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var identity = Environment.UserDomainName + "\\" + Environment.UserName + ":" + process.SessionId;
        var name = "WVB-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        var mutex = new Mutex(false, "Local\\" + name);
        bool entered;
        try { entered = mutex.WaitOne(0); } catch (AbandonedMutexException) { entered = true; }
        if (entered) return new(mutex, name, activate);
        mutex.Dispose();
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(1800); client.WriteByte(1); client.Flush();
        }
        catch (Exception error) when (error is IOException or TimeoutException) { }
        return null;
    }
    public void Dispose()
    {
        stop.Cancel();
        try { listener.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        stop.Dispose(); mutex.ReleaseMutex(); mutex.Dispose();
    }
}
