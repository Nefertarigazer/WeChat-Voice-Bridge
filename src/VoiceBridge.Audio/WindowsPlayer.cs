using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceBridge.Core;

namespace VoiceBridge.Audio;

public sealed record OutputDevice(string Id, string Name);

public sealed class WindowsPlayer : IPlayer
{
    private readonly object outputGate = new();
    private WasapiOut? active;
    private VolumeSampleProvider? activeVolume;
    private string selectedDevice;
    private float selectedVolume;

    public WindowsPlayer(string deviceId, float volume)
    {
        selectedDevice = deviceId;
        selectedVolume = Math.Clamp(volume, 0, 1);
    }

    public void SelectDevice(string deviceId) => Volatile.Write(ref selectedDevice, deviceId);

    public void SetVolume(float volume)
    {
        lock (outputGate)
        {
            selectedVolume = Math.Clamp(volume, 0, 1);
            if (activeVolume != null)
                activeVolume.Volume = selectedVolume;
        }
    }

    public static IReadOnlyList<OutputDevice> Devices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<OutputDevice> { new("", "Windows 默认输出") };
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
                result.Add(new(endpoint.ID, endpoint.FriendlyName.Trim()));
        }
        return result;
    }

    public void Validate()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = Resolve(enumerator);
            if (endpoint.State != DeviceState.Active)
                throw new InvalidOperationException();
        }
        catch (Exception)
        {
            throw new BridgeException("所选音频设备不可用，请连接设备或重新选择。");
        }
    }

    public async Task PlayAsync(AudioData audio, Func<Action, bool> startIfCurrent, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (audio.Pcm != null)
            {
                await PlayPcmAsync(audio.Pcm, startIfCurrent, token);
                return;
            }
            using var bytes = new MemoryStream(audio.Bytes, writable: false);
            using WaveStream decoded = audio.IsWave ? new WaveFileReader(bytes) : new Mp3FileReader(bytes);
            await PlayProviderAsync(decoded, startIfCurrent, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (BridgeException) { throw; }
        catch (Exception)
        {
            throw new BridgeException("播放失败，请检查音频格式或输出设备。后续消息会重新连接设备。");
        }
    }

    private async Task PlayProviderAsync(IWaveProvider source, Func<Action, bool> startIfCurrent, CancellationToken token)
    {
        var samples = new VolumeSampleProvider(source.ToSampleProvider());
        lock (outputGate) samples.Volume = selectedVolume;
        using var enumerator = new MMDeviceEnumerator();
        using var endpoint = Resolve(enumerator);
        using var output = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 60);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, args) => stopped.TrySetResult(args.Exception);
        output.Init(samples.ToWaveProvider());
        using var cancellation = token.Register(Stop);
        try
        {
            if (!startIfCurrent(() =>
            {
                lock (outputGate)
                {
                    token.ThrowIfCancellationRequested();
                    active = output;
                    activeVolume = samples;
                    samples.Volume = selectedVolume;
                    output.Play();
                }
            }))
                return;
            var error = await stopped.Task.WaitAsync(token);
            if (error != null)
                throw error;
        }
        finally
        {
            lock (outputGate)
                if (active == output)
                {
                    active = null;
                    activeVolume = null;
                }
        }
    }

    private async Task PlayPcmAsync(PcmFeed feed, Func<Action, bool> startIfCurrent, CancellationToken token)
    {
        var buffer = new PcmBuffer();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        var primed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pumping = PumpAsync();
        Task playing = Task.CompletedTask;
        try
        {
            await await Task.WhenAny(pumping, primed.Task);
            linked.Token.ThrowIfCancellationRequested();
            playing = PlayProviderAsync(buffer, startIfCurrent, linked.Token);
            await await Task.WhenAny(pumping, playing);
            // Playback can complete first only after EOF, on rejection, or on device failure.
            if (playing.IsCompleted && !pumping.IsCompleted)
                linked.Cancel();
            await pumping;
            await playing;
        }
        finally
        {
            linked.Cancel();
            try { await Task.WhenAll(pumping, playing); }
            catch (Exception) { /* Preserve the initiating exception. */ }
        }

        async Task PumpAsync()
        {
            await feed(async (chunk, cancellation) =>
            {
                await buffer.AppendAsync(chunk, cancellation);
                if (buffer.BufferedBytes >= buffer.WaveFormat.AverageBytesPerSecond * 80 / 1000)
                    primed.TrySetResult();
            }, linked.Token);
            buffer.Complete();
        }
    }

    public void Stop()
    {
        lock (outputGate)
        {
            try { active?.Stop(); }
            catch (Exception) { /* The playback task disposes a failed device. */ }
        }
    }

    private MMDevice Resolve(MMDeviceEnumerator enumerator)
    {
        var device = Volatile.Read(ref selectedDevice);
        return string.IsNullOrEmpty(device)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(device);
    }

    private sealed class PcmBuffer : IWaveProvider
    {
        private readonly BufferedWaveProvider buffer = new(new WaveFormat(AudioData.PcmSampleRate, 16, 1))
            { BufferDuration = TimeSpan.FromSeconds(1) };
        private int completed;
        public WaveFormat WaveFormat => buffer.WaveFormat;
        public int BufferedBytes => buffer.BufferedBytes;
        public void Complete() => Volatile.Write(ref completed, 1);

        public async ValueTask AppendAsync(ReadOnlyMemory<byte> chunk, CancellationToken token)
        {
            var bytes = chunk.ToArray();
            if (bytes.Length > buffer.BufferLength || bytes.Length % 2 != 0)
                throw new BridgeException("PCM 音频块格式无效。");
            while (bytes.Length > buffer.BufferLength - buffer.BufferedBytes)
                await Task.Delay(10, token);
            token.ThrowIfCancellationRequested();
            buffer.AddSamples(bytes, 0, bytes.Length);
        }

        public int Read(byte[] target, int offset, int count)
        {
            buffer.ReadFully = Volatile.Read(ref completed) == 0;
            return buffer.Read(target, offset, count);
        }
    }
}
