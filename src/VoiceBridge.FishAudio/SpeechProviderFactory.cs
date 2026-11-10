using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using VoiceBridge.Core;

namespace VoiceBridge.FishAudio;

/// <summary>Own one factory per window. Returned speech objects borrow its HTTP client and require no disposal.</summary>
public sealed class SpeechProviderFactory : IDisposable
{
    private readonly HttpClient http;
    private readonly ConcurrentDictionary<string, FailureBlock> failures = new(StringComparer.Ordinal);
    private bool disposed;

    public SpeechProviderFactory(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public ISpeech Create(SpeechProviderProfile provider, VoiceProfile voice, Func<string> readCredential)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Validate(provider, voice);
        ArgumentNullException.ThrowIfNull(readCredential);
        return new ProviderSpeech(this, provider, voice, readCredential);
    }

    public void ResetFailures(string? providerId = null)
    {
        if (providerId is null)
            failures.Clear();
        else
            failures.TryRemove(providerId, out _);
    }

    public static string Label(SpeechProviderKind kind) => kind switch
    {
        SpeechProviderKind.Fish => "Fish Audio",
        SpeechProviderKind.OpenAiCompatible => "OpenAI 兼容接口",
        SpeechProviderKind.AzureSpeech => "Azure Speech",
        SpeechProviderKind.Doubao => "豆包 / 火山语音",
        SpeechProviderKind.GptSoVits => "GPT-SoVITS API v2",
        _ => "未知语音接口"
    };

    public static string DefaultEndpoint(SpeechProviderKind kind) => kind switch
    {
        SpeechProviderKind.Fish => "https://api.fish.audio/v1/tts",
        SpeechProviderKind.OpenAiCompatible => "https://api.openai.com/v1/audio/speech",
        SpeechProviderKind.AzureSpeech => "",
        SpeechProviderKind.Doubao => "https://openspeech.bytedance.com/api/v3/tts/unidirectional",
        SpeechProviderKind.GptSoVits => "http://127.0.0.1:9880/tts",
        _ => ""
    };

    /// <summary>Old settings offer both Fish models; an explicit list never silently enables another one.</summary>
    public static string[] FishModels(SpeechProviderProfile provider)
    {
        if (provider.Kind != SpeechProviderKind.Fish || provider.AvailableModels is null)
            return [FishSpeech.Model, FishSpeech.PaidModel];
        var models = provider.AvailableModels.Distinct(StringComparer.Ordinal).ToArray();
        if (models.Length == 0 || models.Any(model => model is not (FishSpeech.Model or FishSpeech.PaidModel)))
            throw new BridgeException("Fish 可用模型无效，请在语音服务中重新选择。");
        return models;
    }

    /// <summary>OpenAI accepts a complete synthesis URL, an origin, or a base URL ending in /v1.</summary>
    public static Uri ResolveEndpoint(SpeechProviderProfile provider)
    {
        var value = string.IsNullOrWhiteSpace(provider.Endpoint) ? DefaultEndpoint(provider.Kind) : provider.Endpoint.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback))
            throw new BridgeException("请填写完整 HTTPS 接口地址；仅本机 localhost / 127.0.0.1 可使用 HTTP，地址中不要包含密钥或查询参数。");
        if (provider.Kind == SpeechProviderKind.OpenAiCompatible)
        {
            var path = endpoint.AbsolutePath.TrimEnd('/');
            if (path.Length == 0)
                endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/v1/audio/speech");
            else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + path + "/audio/speech");
        }
        if (provider.Kind == SpeechProviderKind.Fish && endpoint.AbsoluteUri != DefaultEndpoint(SpeechProviderKind.Fish))
            throw new BridgeException("Fish Audio 类型只使用官方接口，请勿把 Fish 密钥发送到其他地址。");
        return endpoint;
    }

    public static void Validate(SpeechProviderProfile provider, VoiceProfile voice)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(voice);
        if (!Enum.IsDefined(provider.Kind) || string.IsNullOrWhiteSpace(provider.Id))
            throw new BridgeException("语音 API 配置无效，请重新选择接口。");
        if (!provider.Enabled)
            throw new BridgeException("音色已失效，请重新选择。");
        if (!string.Equals(voice.ProviderId, provider.Id, StringComparison.Ordinal))
            throw new BridgeException("该音色属于另一个 API，请启用音色所属的 API，或选择当前 API 的音色。");
        if (provider.TimeoutSeconds is < 5 or > 300)
            throw new BridgeException("语音请求超时请设为 5 至 300 秒。");
        _ = ResolveEndpoint(provider);
        if (voice.Speed is < 0.25 or > 4 || !double.IsFinite(voice.Speed))
            throw new BridgeException("语速请设为 0.25 至 4。");
        if (provider.Kind == SpeechProviderKind.GptSoVits)
        {
            if (!Plain(voice.ReferenceAudioPath, 2048) ||
                voice.ReferenceText.Length > 4096 || voice.ReferenceText.Contains('\0') ||
                !Plain(voice.PromptLanguage, 32) || !Plain(voice.Language, 32))
                throw new BridgeException("GPT-SoVITS 音色需要有效的参考录音路径和语种；路径必须能被模型服务读取。");
        }
        else if (!Plain(voice.VoiceId, 512))
            throw new BridgeException("请填写该 API 对应的有效音色 ID。");
        if (provider.Kind == SpeechProviderKind.Fish &&
            (!FishVoice(voice.VoiceId) || provider.Model is not ("" or FishSpeech.Model or FishSpeech.PaidModel) ||
             !FishModels(provider).Contains(provider.Model.Length == 0 ? FishSpeech.Model : provider.Model, StringComparer.Ordinal) ||
             provider.Latency is not ("normal" or "balanced" or "low")))
            throw new BridgeException("Fish 配置只支持明确选择的 S2.1 Pro 免费版或 Drama 3，以及有效的音色 ID 和延迟模式。");
        if (provider.Kind == SpeechProviderKind.OpenAiCompatible && (!Plain(provider.Model, 256) ||
            provider.AudioFormat is not ("mp3" or "wav") || voice.Instructions.Length > 4096))
            throw new BridgeException("OpenAI 兼容接口需要模型名称，音频格式请选择 MP3 或 WAV。");
        if (provider.Kind == SpeechProviderKind.Doubao && (!Header(provider.ResourceId) ||
            !string.IsNullOrEmpty(provider.AppId) && !Header(provider.AppId)))
            throw new BridgeException("豆包需要资源 ID；旧版控制台还需 App ID，请核对它们与音色的授权关系。");
        if (provider.Kind == SpeechProviderKind.AzureSpeech && !Plain(voice.Language, 32))
            throw new BridgeException("Azure 音色需要语言代码，例如 zh-CN。");
    }

    public static bool RequiresCredential(SpeechProviderProfile provider) =>
        provider.Kind != SpeechProviderKind.GptSoVits &&
        !(provider.Kind == SpeechProviderKind.OpenAiCompatible && ResolveEndpoint(provider).IsLoopback);

    /// <summary>May also be called before starting the message worker; it does not access the network.</summary>
    public static void ValidateCredential(SpeechProviderProfile provider, string credential)
    {
        if (!string.IsNullOrEmpty(credential) && !Header(credential) ||
            RequiresCredential(provider) && string.IsNullOrEmpty(credential))
            throw new BridgeException("请先为该 API 选择有效的密钥；不同服务商的密钥不能混用。");
    }

    private static bool Plain(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(char.IsControl);
    private static bool Header(string value) => Plain(value, 2048) && value.All(c => c is >= (char)33 and <= (char)126);
    private static bool FishVoice(string value) => Plain(value, 128) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        http.Dispose();
    }

    private sealed record FailureBlock(string Signature, DateTimeOffset Until, string Message);

    private sealed class ProviderSpeech(SpeechProviderFactory owner, SpeechProviderProfile provider,
        VoiceProfile voice, Func<string> readCredential) : ISpeech
    {
        private const int MaxAudioBytes = 16 * 1024 * 1024;
        private const int MaxWireBytes = 24 * 1024 * 1024;

        public async Task<AudioData> SynthesizeAsync(string text, Action firstAudio, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(owner.disposed, owner);
            if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
                throw new BridgeException("待朗读文字为空或过长。");
            var key = readCredential() ?? "";
            var endpoint = ResolveEndpoint(provider);
            ValidateCredential(provider, key);
            var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join('\n', key, endpoint.AbsoluteUri, provider.Model, provider.ResourceId, provider.AppId, voice.VoiceId))));
            if (owner.failures.TryGetValue(provider.Id, out var blocked) && blocked.Signature == signature &&
                blocked.Until > DateTimeOffset.UtcNow)
                throw new BridgeException(blocked.Message);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(provider.TimeoutSeconds));
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    TimeSpan retry;
                    using (var request = BuildRequest(endpoint, key, text))
                    using (var response = await owner.http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            if (provider.Kind == SpeechProviderKind.Doubao)
                            {
                                var payload = await Download(response.Content, MaxWireBytes, null, deadline.Token);
                                var audio = DecodeDoubao(payload);
                                ValidateAudio(audio, false);
                                deadline.Token.ThrowIfCancellationRequested();
                                firstAudio();
                                return new(audio);
                            }
                            var wave = provider.Kind == SpeechProviderKind.GptSoVits ||
                                provider.Kind == SpeechProviderKind.OpenAiCompatible && provider.AudioFormat == "wav";
                            var announced = false;
                            var bytes = await Download(response.Content, MaxAudioBytes, data =>
                            {
                                if (!wave && !announced && AudioSignature.HasMp3Frame(data.Span))
                                {
                                    announced = true;
                                    firstAudio();
                                }
                            }, deadline.Token);
                            ValidateAudio(bytes, wave);
                            deadline.Token.ThrowIfCancellationRequested();
                            if (!announced) firstAudio();
                            return new(bytes, wave);
                        }
                        var status = (int)response.StatusCode;
                        var failure = Failure(status);
                        if (status is 401 or 403)
                            owner.failures[provider.Id] = new(signature, DateTimeOffset.MaxValue,
                                "该 API 的密钥或音色被拒绝，请修改配置或重新测试后再试。");
                        retry = response.Headers.RetryAfter?.Delta ??
                            response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow ?? TimeSpan.FromMilliseconds(350 * (attempt + 1));
                        if (status == 429)
                        {
                            var delay = retry > TimeSpan.Zero ? retry : TimeSpan.FromSeconds(1);
                            var until = delay >= DateTimeOffset.MaxValue - DateTimeOffset.UtcNow ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow + delay;
                            owner.failures[provider.Id] = new(signature, until, "该 API 正在限流，请等待服务端允许后再试。");
                        }
                        if (provider.Kind != SpeechProviderKind.Fish || provider.Model == FishSpeech.PaidModel ||
                            status is not 429 && status is not (>= 500 and <= 599) ||
                            attempt >= 2 || retry > TimeSpan.FromSeconds(5))
                            throw failure;
                    }
                    await Task.Delay(retry < TimeSpan.Zero ? TimeSpan.Zero : retry, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new BridgeException("语音 API 请求超时，本条已跳过。");
            }
            catch (HttpRequestException) { throw new BridgeException("语音 API 网络连接失败，请检查服务地址和网络。"); }
            catch (IOException) { throw new BridgeException("语音响应传输中断，本条已跳过。"); }
            catch (JsonException) { throw new BridgeException("语音 API 返回的数据格式不受支持。"); }
            catch (FormatException) { throw new BridgeException("语音 API 返回了无效的音频编码。"); }
            catch (XmlException) { throw new BridgeException("本条文字包含 Azure SSML 不支持的字符，已跳过。"); }
        }

        private HttpRequestMessage BuildRequest(Uri endpoint, string key, string text)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.UserAgent.ParseAdd("WeChatVoiceBridge");
            switch (provider.Kind)
            {
                case SpeechProviderKind.Fish:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    request.Headers.Add("model", string.IsNullOrEmpty(provider.Model) ? FishSpeech.Model : provider.Model);
                    request.Content = JsonContent.Create(new { text, reference_id = voice.VoiceId, format = "mp3", latency = provider.Latency });
                    break;
                case SpeechProviderKind.OpenAiCompatible:
                    if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    var json = new Dictionary<string, object>
                    {
                        ["input"] = text, ["model"] = provider.Model,
                        ["voice"] = voice.IsCustomVoice ? new { id = voice.VoiceId } : voice.VoiceId,
                        ["response_format"] = provider.AudioFormat, ["speed"] = voice.Speed
                    };
                    if (!string.IsNullOrWhiteSpace(voice.Instructions)) json["instructions"] = voice.Instructions;
                    request.Content = JsonContent.Create(json);
                    break;
                case SpeechProviderKind.AzureSpeech:
                    request.Headers.Add("Ocp-Apim-Subscription-Key", key);
                    request.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-96kbitrate-mono-mp3");
                    XNamespace ns = "http://www.w3.org/2001/10/synthesis";
                    var ssml = new XElement(ns + "speak", new XAttribute("version", "1.0"), new XAttribute(XNamespace.Xml + "lang", voice.Language),
                        new XElement(ns + "voice", new XAttribute("name", voice.VoiceId), text));
                    request.Content = new StringContent(ssml.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/ssml+xml");
                    break;
                case SpeechProviderKind.Doubao:
                    if (string.IsNullOrEmpty(provider.AppId)) request.Headers.Add("X-Api-Key", key);
                    else
                    {
                        request.Headers.Add("X-Api-App-Id", provider.AppId);
                        request.Headers.Add("X-Api-Access-Key", key);
                    }
                    request.Headers.Add("X-Api-Resource-Id", provider.ResourceId);
                    request.Headers.Add("X-Api-Request-Id", Guid.NewGuid().ToString());
                    var parameters = new Dictionary<string, object>
                    {
                        ["text"] = text, ["speaker"] = voice.VoiceId,
                        ["audio_params"] = new { format = "mp3", sample_rate = 24000 }
                    };
                    if (!string.IsNullOrWhiteSpace(provider.Model)) parameters["model"] = provider.Model;
                    request.Content = JsonContent.Create(new { user = new { uid = "voice-bridge" }, @namespace = "BidirectionalTTS", req_params = parameters });
                    break;
                case SpeechProviderKind.GptSoVits:
                    if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    request.Content = JsonContent.Create(new
                    {
                        text, text_lang = GptLanguage(voice.Language), ref_audio_path = voice.ReferenceAudioPath,
                        prompt_text = voice.ReferenceText, prompt_lang = voice.PromptLanguage,
                        text_split_method = "cut5", batch_size = 1, media_type = "wav", streaming_mode = false,
                        speed_factor = voice.Speed
                    });
                    break;
                default:
                    request.Dispose();
                    throw new BridgeException("尚未支持该语音接口类型。");
            }
            return request;
        }

        private static async Task<byte[]> Download(HttpContent content, int limit, Action<ReadOnlyMemory<byte>>? received,
            CancellationToken token)
        {
            if (content.Headers.ContentLength > limit) throw new BridgeException("语音 API 返回的内容过大。");
            await using var input = await content.ReadAsStreamAsync(token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                token.ThrowIfCancellationRequested();
                if (output.Length + count > limit) throw new BridgeException("语音 API 返回的内容过大。");
                output.Write(buffer, 0, count);
                received?.Invoke(output.GetBuffer().AsMemory(0, (int)output.Length));
            }
            if (content.Headers.ContentLength is { } expected && expected != output.Length)
                throw new BridgeException("语音 API 音频不完整，本条已跳过。");
            return output.ToArray();
        }

        private static string GptLanguage(string language) => language.ToLowerInvariant() switch
        {
            "zh-cn" or "zh-tw" => "zh",
            "en-us" or "en-gb" => "en",
            "ja-jp" => "ja",
            "ko-kr" => "ko",
            "yue-hk" => "yue",
            _ => language
        };

        private static byte[] DecodeDoubao(byte[] wire)
        {
            using var output = new MemoryStream();
            var offset = 0;
            var finished = false;
            while (offset < wire.Length)
            {
                while (offset < wire.Length && wire[offset] is 9 or 10 or 13 or 32) offset++;
                if (offset == wire.Length) break;
                if (finished) throw new BridgeException("豆包结束标记后还有数据，已拒绝本条音频。");
                var reader = new Utf8JsonReader(wire.AsSpan(offset));
                using var packet = JsonDocument.ParseValue(ref reader);
                offset += checked((int)reader.BytesConsumed);
                var root = packet.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("code", out var code) ||
                    code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var value))
                    throw new BridgeException("豆包语音响应缺少状态码。");
                if (value is not (0 or 20000000))
                    throw new BridgeException(value == 45000000 ? "豆包拒绝请求，请检查音色权限、资源和配额。" : "豆包语音合成失败，请检查配额、音色和资源配置。");
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String && data.GetString() is { Length: > 0 } encoded)
                {
                    var part = Convert.FromBase64String(encoded);
                    if (output.Length + part.Length > MaxAudioBytes) throw new BridgeException("豆包返回的音频过大。");
                    output.Write(part);
                }
                finished = value == 20000000;
            }
            if (!finished) throw new BridgeException("豆包语音响应缺少完成标记，已丢弃不完整音频。");
            return output.ToArray();
        }

        private static void ValidateAudio(byte[] bytes, bool wave)
        {
            if (wave ? !AudioSignature.IsCompleteWave(bytes) : !AudioSignature.HasMp3Frame(bytes))
                throw new BridgeException("语音 API 未返回完整的受支持音频，请核对 MP3 / WAV 输出设置。");
        }

        private static BridgeException Failure(int status) => status switch
        {
            401 or 403 => new("语音 API 拒绝密钥或音色权限，请检查对应服务配置。"),
            402 => new("语音 API 余额或额度不足，请自行检查计费；程序不会充值或切换付费服务。"),
            429 => new("语音 API 已限流，本条已跳过。"),
            >= 300 and < 400 => new("语音接口发生重定向，出于密钥安全已拒绝跟随，请填写最终接口地址。"),
            400 or 404 or 422 => new("语音 API 拒绝请求，请核对接口协议、模型、音色和资源。"),
            _ => new($"语音 API 返回 HTTP {status}，本条已跳过。")
        };
    }
}
