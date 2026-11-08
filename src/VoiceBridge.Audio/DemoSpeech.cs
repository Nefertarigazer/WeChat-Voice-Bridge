using NAudio.Wave;
using VoiceBridge.Core;

namespace VoiceBridge.Audio;

public sealed class DemoSpeech : ISpeech
{
    public Task<AudioData> SynthesizeAsync(string text, Action firstAudio, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        const int sampleRate = 24000;
        var samples = new short[sampleRate / 5];
        var frequency = 440 + text.Length % 10 * 25;
        for (var i = 0; i < samples.Length; i++)
        {
            var fade = Math.Sin(Math.PI * i / samples.Length);
            samples[i] = (short)(5000 * fade * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
        }
        using var memory = new MemoryStream();
        using (var writer = new WaveFileWriter(memory, new WaveFormat(sampleRate, 16, 1)))
            writer.WriteSamples(samples, 0, samples.Length);
        firstAudio();
        return Task.FromResult(new AudioData(memory.ToArray(), IsWave: true));
    }
}
