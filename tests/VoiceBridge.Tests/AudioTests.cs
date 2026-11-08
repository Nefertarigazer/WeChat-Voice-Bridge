using System.Buffers.Binary;
using VoiceBridge.Audio;
using VoiceBridge.Core;
using static VoiceBridge.Tests.TestSupport;

namespace VoiceBridge.Tests;

public sealed class AudioTests
{
    [Trait("Category", "AudioDevice")]
    [Fact]
    public async Task WasapiPlaysWavAndMp3AndCancelsStreaming()
    {
        var player = new WindowsPlayer("", 0);
        player.Validate();
        var submitted = 0;
        bool Submit(Action play)
        {
            play();
            Interlocked.Increment(ref submitted);
            return true;
        }

        var wave = await new DemoSpeech().SynthesizeAsync("试音", () => { }, CancellationToken.None);
        await player.PlayAsync(wave, Submit, CancellationToken.None).WaitAsync(Limit);
        var mp3 = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "assets", "tone.mp3"));
        await player.PlayAsync(new(mp3), Submit, CancellationToken.None).WaitAsync(Limit);

        var pcm = TonePcm();
        var first = new AudioData([], Pcm: async (write, token) =>
        {
            await write(pcm.AsMemory(0, 4800), token);
            await Task.Delay(80, token);
            await write(pcm.AsMemory(4800), token);
        });
        await player.PlayAsync(first, Submit, CancellationToken.None).WaitAsync(Limit);
        Assert.Equal(3, submitted);

        using var cancellation = new CancellationTokenSource();
        var playStreaming = player.PlayAsync(new([], Pcm: async (write, token) =>
        {
            await write(pcm.AsMemory(0, 4800), token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }), Submit, cancellation.Token);
        await Until(() => Volatile.Read(ref submitted) == 4);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playStreaming.WaitAsync(Limit));
    }

    private static byte[] TonePcm()
    {
        var pcm = new byte[AudioData.PcmSampleRate * 2 / 5];
        for (var i = 0; i < pcm.Length / 2; i++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(1000 * Math.Sin(i * 0.11)));
        return pcm;
    }

    [Fact]
    public async Task MissingExplicitDeviceReportsFailureWithoutFallingBack()
    {
        var player = new WindowsPlayer("missing-test-device", 0);
        var wave = await new DemoSpeech().SynthesizeAsync("试音", () => { }, CancellationToken.None);
        var called = false;
        await Assert.ThrowsAsync<BridgeException>(() => player.PlayAsync(wave, _ =>
        {
            called = true;
            return false;
        }, CancellationToken.None));
        Assert.False(called);
    }
}
