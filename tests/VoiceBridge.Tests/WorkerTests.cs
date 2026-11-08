using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using VoiceBridge.Core;
using VoiceBridge.WeChat;
using static VoiceBridge.Tests.TestSupport;

namespace VoiceBridge.Tests;

public sealed class WorkerTests
{
    [Fact]
    public async Task NewReadingsApplyToMessagesAlreadyQueuedWithoutChangingTheCurrentMessage()
    {
        await using var controller = new SessionController();
        ReadingRule[] current = [new("demo", "old", true)];
        var source = new PythonFixtureSource(new(WatchMode.PollingOnly, 100, 30, []),
            PythonFixtureSource.ResolvePython(), Path.Combine(AppContext.BaseDirectory, "readings_worker.py"),
            currentReadings: () => Volatile.Read(ref current));
        var release = ProbeSource.NewSignal();
        var speech = new ProbeSpeech(async (text, token) =>
        {
            if (text == "old")
                await release.Task.WaitAsync(token);
            return new AudioData([]);
        });
        var player = new ProbePlayer();
        controller.Start(DemoTarget, true, 30, false, () => source, speech, player, source.RenderSpeechAsync);
        await Until(() => controller.Status.State is BridgeState.Running or BridgeState.Error);
        Assert.True(controller.Status.State == BridgeState.Running, controller.Status.Error);
        source.RequestDemo();
        await Until(() => speech.Calls.Count == 1 && controller.Status.Pending == 2);
        Volatile.Write(ref current, [new("demo", "new", true)]);
        release.SetResult();
        await Until(() => Volatile.Read(ref player.Played) == 3);
        Assert.Equal(new[] { "old", "new", "new" }, speech.Calls.ToArray());
        await controller.EndAsync().WaitAsync(Limit);
        Assert.Equal(BridgeState.Idle, controller.Status.State);
    }

    [Fact]
    public async Task RealFixtureWorkerSkipsHistoryAndReleasesWatcherAndProcess()
    {
        await using var controller = new SessionController();
        var source = Fixture();
        var speech = new ProbeSpeech();
        var warnings = new ConcurrentQueue<string>();
        source.Warning += warnings.Enqueue;
        Assert.Null(source.WorkerId);
        Assert.False(source.Watching);
        controller.Start(DemoTarget, true, 30, false, () => source, speech, new ProbePlayer());
        await Until(() => controller.Status.State is BridgeState.Running or BridgeState.Error);
        Assert.True(controller.Status.State == BridgeState.Running, controller.Status.Error);
        Assert.True(source.Watching);
        Assert.Empty(speech.Calls);
        var pid = source.WorkerId!.Value;
        var path = source.FixtureDirectory!;
        using var child = Process.GetProcessById(pid);
        source.SignalOverflow();
        for (var i = 0; i < 5; i++)
            source.RequestDemo();
        await Until(() => speech.Calls.Count == 5);
        Assert.Equal(new[] { "你在干嘛呀？", "哈哈哈", "哈哈哈", "我听得到。", "等一下，我回来啦。" }, speech.Calls.ToArray());
        Assert.Contains(warnings, warning => warning.Contains("兜底", StringComparison.Ordinal));
        await controller.EndAsync().WaitAsync(Limit);
        Assert.False(source.Watching);
        Assert.True(child.HasExited);
        Assert.False(Directory.Exists(path));
        source.RequestDemo();
        Assert.Equal(5, speech.Calls.Count);
    }

    [Fact]
    public async Task StopsUnresponsiveWorkerAndCleansItsDirectory()
    {
        await using var controller = new SessionController();
        var source = Fixture(script: "hang_worker.py");
        controller.Start(DemoTarget, true, 30, false, () => source, new ProbeSpeech(), new ProbePlayer());
        string[]? pids = null;
        await Until(() =>
        {
            if (source.FixtureDirectory == null)
                return false;
            try
            {
                pids = File.ReadAllText(Path.Combine(source.FixtureDirectory, "message_0.sqlite")).Split(',');
                return pids.Length == 2 && int.TryParse(pids[1], out _);
            }
            catch (IOException) { return false; }
        });
        var path = source.FixtureDirectory!;
        using var child = Process.GetProcessById(source.WorkerId!.Value);
        using var descendant = Process.GetProcessById(int.Parse(pids![1]));
        await controller.EndAsync().WaitAsync(Limit);
        Assert.Equal(BridgeState.Idle, controller.Status.State);
        Assert.True(child.HasExited);
        await Until(() => descendant.HasExited);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task RejectsMalformedWorkerProtocol()
    {
        await using var controller = new SessionController();
        var source = Fixture(script: "bad_worker.py");
        controller.Start(DemoTarget, true, 30, false, () => source, new ProbeSpeech(), new ProbePlayer());
        await Until(() => controller.Status.State == BridgeState.Error);
        Assert.Contains("无效的协议", controller.Status.Error);
        Assert.False(Directory.Exists(source.FixtureDirectory));
    }

    [Fact]
    public async Task RecoversFromBusyThenStopsOnFatalDatabaseError()
    {
        await using var controller = new SessionController();
        var options = new WatchOptions(WatchMode.PollingOnly, 100, 30, []);
        var source = Fixture(options, script: "fault_worker.py");
        var warnings = new ConcurrentQueue<string>();
        source.Warning += warnings.Enqueue;
        controller.Start(DemoTarget, true, 30, false, () => source, new ProbeSpeech(), new ProbePlayer());
        await Until(() => !warnings.IsEmpty);
        Assert.Equal(BridgeState.Running, controller.Status.State);
        source.RequestDemo();
        await Until(() => controller.Status.State == BridgeState.Error);
        Assert.Equal("消息数据库查询失败。", controller.Status.Error);
        Assert.False(Directory.Exists(source.FixtureDirectory));
    }

    [Trait("Category", "Benchmark")]
    [Fact]
    public async Task MeasureFixtureDetection()
    {
        var outputPath = Environment.GetEnvironmentVariable("WVB_BENCHMARK_PATH");
        if (string.IsNullOrEmpty(outputPath))
            return;

        var runs = new[]
        {
            new WatchOptions(WatchMode.EventDriven, 1000, 20, [50, 100, 200]),
            WatchOptions.Default,
            new WatchOptions(WatchMode.EventDriven, 1000, 50, [50, 100, 200]),
            new WatchOptions(WatchMode.PollingOnly, 500, 30, []),
            new WatchOptions(WatchMode.PollingOnly, 300, 30, []),
            new WatchOptions(WatchMode.PollingOnly, 200, 30, [])
        };
        var result = new List<object>();
        foreach (var options in runs)
        {
            await using var controller = new SessionController();
            var trace = new ConcurrentQueue<ProcessingEvent>();
            controller.Processed += trace.Enqueue;
            var source = Fixture(options);
            var player = new ProbePlayer();
            controller.Start(DemoTarget, true, 30, false, () => source, new ProbeSpeech(), player);
            await Until(() => controller.Status.State is BridgeState.Running or BridgeState.Error);
            Assert.True(controller.Status.State == BridgeState.Running, controller.Status.Error);
            using var process = Process.GetProcessById(source.WorkerId!.Value);
            using var parent = Process.GetCurrentProcess();
            var childCpuStart = process.TotalProcessorTime;
            var parentCpuStart = parent.TotalProcessorTime;
            GetProcessIoCounters(process.Handle, out var ioStart);
            var elapsed = Stopwatch.StartNew();
            var random = new Random(214);
            for (var i = 1; i <= 30; i++)
            {
                await Task.Delay(random.Next(20, 180));
                source.RequestDemo();
                await Until(() => Volatile.Read(ref player.Played) >= i);
            }
            // Allow any redundant filesystem signals to be processed.
            await Task.Delay(550);
            Assert.Equal(30, player.Played);
            var samples = trace.Where(item => item.Stage == "已接收").ToArray();
            Assert.Equal(30, samples.Length);
            var sorted = samples.Select(item => item.Milliseconds!.Value).Order().ToArray();
            GetProcessIoCounters(process.Handle, out var ioEnd);
            process.Refresh();
            parent.Refresh();
            var resourceUsage = new
            {
                wallMs = elapsed.Elapsed.TotalMilliseconds,
                workerCpuMs = (process.TotalProcessorTime - childCpuStart).TotalMilliseconds,
                parentCpuMs = (parent.TotalProcessorTime - parentCpuStart).TotalMilliseconds,
                workerWorkingSetMiB = process.WorkingSet64 / 1048576d,
                parentWorkingSetMiB = parent.WorkingSet64 / 1048576d,
                workerIoReadBytes = ioEnd.ReadBytes - ioStart.ReadBytes,
                workerIoWriteBytes = ioEnd.WriteBytes - ioStart.WriteBytes
            };
            var directory = source.FixtureDirectory;
            var stop = Stopwatch.StartNew();
            await controller.EndAsync().WaitAsync(Limit);
            Assert.True(process.HasExited);
            Assert.False(Directory.Exists(directory));
            result.Add(new
            {
                mode = options.Mode.ToString(), options.PollMs, options.CoalesceMs,
                samples = sorted.Length,
                p50 = Percentile(sorted, .50), p95 = Percentile(sorted, .95), p99 = Percentile(sorted, .99),
                queryMedian = Percentile(samples.Select(item => item.QueryMilliseconds!.Value).Order().ToArray(), .50),
                cleanupMs = stop.Elapsed.TotalMilliseconds,
                resourceUsage
            });
        }
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static double Percentile(double[] sorted, double p) => sorted[(int)Math.Ceiling(sorted.Length * p) - 1];

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}
