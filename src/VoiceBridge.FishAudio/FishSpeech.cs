using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using VoiceBridge.Core;

namespace VoiceBridge.FishAudio;

public sealed class FishSpeech : ISpeech
{
    public const string Model = "s2.1-pro-free";
    public const string PaidModel = "drama-3";
    private const int MaxBytes = 16 * 1024 * 1024;
    private static readonly Uri Endpoint = new("https://api.fish.audio/v1/tts");
    private readonly HttpClient client;
    private readonly string latency;
    private readonly TimeSpan timeout;
    private readonly bool streamPcm;
    private readonly Func<string> selectModel;
    private readonly Func<string> selectVoice;
    private readonly Func<string> selectApiKey;
    private readonly ConcurrentDictionary<string, long> retryUntil = new(StringComparer.Ordinal);

    public FishSpeech(HttpClient client, string apiKey, string voiceId, string latency, TimeSpan? timeout = null,
        bool streamPcm = false, Func<string>? selectModel = null, Func<string>? selectVoice = null,
        Func<string>? selectApiKey = null)
    {
        if (!ValidKey(apiKey) || !ValidVoice(voiceId) ||
            latency is not ("normal" or "balanced" or "low"))
            throw new BridgeException("请填写有效的 Fish API Key、私有 Voice ID 和延迟模式。");

        this.client = client;
        this.selectApiKey = selectApiKey ?? (() => apiKey);
        this.latency = latency;
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        this.streamPcm = streamPcm;
        this.selectModel = selectModel ?? (() => Model);
        // The voice may change while the application runs, the next message
        // then speaks with the voice that is chosen at that moment.
        this.selectVoice = selectVoice ?? (() => voiceId);
    }

    private static bool ValidVoice(string voice) => !string.IsNullOrWhiteSpace(voice) && voice.Length <= 128 &&
        !voice.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));

    private static bool ValidKey(string key) => !string.IsNullOrWhiteSpace(key) && !key.Any(c => c < 33 || c > 126);

    public Task<AudioData> SynthesizeAsync(string text, Action firstAudio, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var key = selectApiKey();
        if (!ValidKey(key))
            throw new BridgeException("请先选择一个有效的 Fish API Key。");
        var model = selectModel();
        if (model is not (Model or PaidModel))
            throw new BridgeException("Fish Audio 模型无效。");
        var voice = selectVoice();
        if (!ValidVoice(voice))
            throw new BridgeException("请先选择一个有效的音色。");
        if (!streamPcm || model == PaidModel)
            return RequestAsync(key, model, voice, text, firstAudio, null, token);
        return Task.FromResult(new AudioData([], Pcm: async (consume, playbackToken) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, playbackToken);
            await RequestAsync(key, model, voice, text, firstAudio, consume, linked.Token);
        }));
    }

    private async Task<AudioData> RequestAsync(string apiKey, string model, string voice, string text, Action firstAudio,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? consume, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (retryUntil.TryGetValue(apiKey, out var retryAt) && Stopwatch.GetTimestamp() < retryAt)
            throw new BridgeException("Fish Audio 要求延后请求，本条合成已跳过。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                TimeSpan delay;
                using (var request = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Headers.Add("model", model);
                    request.Content = JsonContent.Create(new { text, reference_id = voice,
                        format = consume == null ? "mp3" : "pcm", sample_rate = consume == null ? 44100 : AudioData.PcmSampleRate, latency });
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        if (consume == null)
                            return await DownloadAsync(response.Content, firstAudio, deadline.Token);
                        await PumpPcmAsync(response.Content, firstAudio, consume, deadline.Token);
                        return new([]);
                    }

                    var code = (int)response.StatusCode;
                    if (model == PaidModel && code == 402)
                        throw new BridgeException("Drama 3 的余额或计费条件不满足。可切回免费模型。");
                    if (model == PaidModel)
                        throw Failure(response.StatusCode);
                    if (code is not 429 && code is not (>= 500 and <= 599))
                        throw Failure(response.StatusCode);
                    delay = response.Headers.RetryAfter?.Delta ??
                        (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
                        TimeSpan.FromMilliseconds(350 * (attempt + 1));
                    if (attempt >= 2 || delay > TimeSpan.FromSeconds(5))
                    {
                        if (delay > TimeSpan.Zero)
                            retryUntil[apiKey] = Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency);
                        throw Failure(response.StatusCode);
                    }
                }
                await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, deadline.Token);
            }
        }
        catch (BridgeException error) when (error.Fatal && !string.Equals(selectApiKey(), apiKey, StringComparison.Ordinal))
        {
            // A failure from a key the user already switched away from applies to this message.
            throw new BridgeException(error.Message);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new BridgeException("Fish Audio 请求超时。");
        }
        catch (HttpRequestException)
        {
            throw new BridgeException("Fish Audio 网络连接失败。");
        }
        catch (IOException)
        {
            throw new BridgeException("Fish Audio 音频传输中断。");
        }
    }

    private static async Task PumpPcmAsync(HttpContent content, Action firstAudio,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consume, CancellationToken token)
    {
        var media = content.Headers.ContentType?.MediaType;
        if (media is not ("application/octet-stream" or "audio/pcm" or "audio/raw" or "audio/x-pcm") ||
            content.Headers.ContentLength > MaxBytes)
            throw new BridgeException("Fish Audio 未返回支持的 PCM 流，请切换到完整 MP3。");
        // API PCM is signed 16-bit mono. Keep a split sample across network reads.
        await using var stream = await content.ReadAsStreamAsync(token);
        var buffer = new byte[8192];
        var first = true;
        var carry = 0;
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(carry), token)) > 0)
        {
            total += read;
            if (total > MaxBytes)
                throw new BridgeException("Fish Audio 返回的音频过大。");
            var count = read + carry;
            var aligned = count & ~1;
            if (aligned > 0)
            {
                if (first)
                {
                    first = false;
                    firstAudio();
                }
                await consume(buffer.AsMemory(0, aligned), token);
            }
            carry = count - aligned;
            if (carry != 0)
                buffer[0] = buffer[aligned];
        }
        if (first || carry != 0 || (content.Headers.ContentLength is { } expected && total != expected))
            throw new BridgeException("Fish Audio PCM 流不完整，请切换到完整 MP3。");
    }

    private static async Task<AudioData> DownloadAsync(HttpContent content, Action firstAudio, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaxBytes)
            throw new BridgeException("Fish Audio 返回的音频过大。");

        await using var stream = await content.ReadAsStreamAsync(token);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        var first = false;
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (bytes.Length + read > MaxBytes)
                throw new BridgeException("Fish Audio 返回的音频过大。");
            bytes.Write(buffer, 0, read);
            if (!first && HasFrame(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)))
            {
                first = true;
                firstAudio();
            }
        }
        token.ThrowIfCancellationRequested();
        if (!first)
            throw new BridgeException("Fish Audio 未返回有效的 MP3 音频。");
        return new(bytes.ToArray());
    }

    private static bool HasFrame(ReadOnlySpan<byte> bytes)
    {
        // Documented MP3 output is MPEG-1 Layer III at 32 or 44.1 kHz.
        ReadOnlySpan<int> bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
        var offset = 0;
        if (bytes.StartsWith("ID3"u8))
        {
            if (bytes.Length < 10)
                return false;
            offset = 10 + ((bytes[6] & 127) << 21) + ((bytes[7] & 127) << 14) +
                ((bytes[8] & 127) << 7) + (bytes[9] & 127);
            if (bytes[3] == 4 && (bytes[5] & 16) != 0)
                offset += 10;
        }
        if (offset + 4 > bytes.Length || bytes[offset] != 255 || (bytes[offset + 1] & 254) != 250)
            return false;
        var flags = bytes[offset + 2];
        var rate = ((flags >> 2) & 3) switch { 0 => 44100, 2 => 32000, _ => 0 };
        var bitrate = bitrates[flags >> 4];
        return rate > 0 && bitrate > 0 && offset + 144000 * bitrate / rate + ((flags >> 1) & 1) <= bytes.Length;
    }

    private static BridgeException Failure(HttpStatusCode status) => (int)status switch
    {
        401 or 403 => new("Fish Audio 拒绝访问，请检查密钥和私有音色权限。", true),
        402 => new("Fish Audio 返回 402，免费额度或计费条件不满足。", true),
        400 or 404 or 422 => new("Fish Audio 拒绝请求，请核对私有音色和所选模型是否可用。", true),
        429 => new("Fish Audio 触发限流，本条合成已停止。"),
        _ => new($"Fish Audio 返回 HTTP {(int)status}。")
    };
}
