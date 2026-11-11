using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using VoiceBridge.Core;

namespace VoiceBridge.FishAudio;

/// <summary>One request to the Edge read-aloud helper. The host decides how it runs.</summary>
public sealed record EdgeSpeechRequest(string Text, string Voice, double Speed);

/// <summary>Own one factory per window. Returned speech objects borrow its HTTP client and require no disposal.</summary>
public sealed class SpeechProviderFactory : IDisposable
{
    private readonly HttpClient http;
    private readonly Func<EdgeSpeechRequest, CancellationToken, Task<byte[]>>? edge;
    private readonly ConcurrentDictionary<string, FailureBlock> failures = new(StringComparer.Ordinal);
    private bool disposed;

    /// <param name="edge">Runs the bundled edge-tts helper; null when this build has none.</param>
    public SpeechProviderFactory(HttpMessageHandler? handler = null,
        Func<EdgeSpeechRequest, CancellationToken, Task<byte[]>>? edge = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        this.edge = edge;
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
        SpeechProviderKind.AzureSpeech => "Azure 语音服务",
        SpeechProviderKind.Doubao => "火山引擎 · 豆包语音",
        SpeechProviderKind.GptSoVits => "GPT-SoVITS",
        SpeechProviderKind.ElevenLabs => "ElevenLabs",
        SpeechProviderKind.MiniMax => "MiniMax",
        SpeechProviderKind.DashScope => "阿里云百炼",
        SpeechProviderKind.Gemini => "Google Gemini",
        SpeechProviderKind.EdgeTts => "微软 Edge 朗读",
        SpeechProviderKind.CustomHttp => "自定义 HTTP 接口",
        _ => "未知语音接口"
    };

    public static string DefaultEndpoint(SpeechProviderKind kind) => kind switch
    {
        SpeechProviderKind.Fish => ServiceIdentity.FishOfficialEndpoint,
        SpeechProviderKind.OpenAiCompatible => "https://api.openai.com/v1/audio/speech",
        SpeechProviderKind.Doubao => "https://openspeech.bytedance.com/api/v3/tts/unidirectional",
        SpeechProviderKind.GptSoVits => ServiceIdentity.GptSoVitsDefaultEndpoint,
        SpeechProviderKind.ElevenLabs => "https://api.elevenlabs.io",
        SpeechProviderKind.MiniMax => "https://api.minimax.cn/v1/t2a_v2",
        SpeechProviderKind.DashScope => "https://dashscope.aliyuncs.com",
        SpeechProviderKind.Gemini => "https://generativelanguage.googleapis.com",
        _ => ""
    };

    /// <summary>
    /// The Fish models this key offers. Old settings offer the two they always did;
    /// an explicit list never silently enables another model, and only models the
    /// official API documents may ever reach its model header. A local fish-speech
    /// server picks its model itself, so it offers none.
    /// </summary>
    public static string[] FishModels(SpeechProviderProfile provider)
    {
        if (provider.Kind == SpeechProviderKind.Fish && !IsOfficialFish(provider))
            return [];
        if (provider.Kind != SpeechProviderKind.Fish || provider.AvailableModels is null)
            return [FishSpeech.Model, FishSpeech.PaidModel];
        var models = provider.AvailableModels.Distinct(StringComparer.Ordinal).ToArray();
        if (models.Length == 0 || models.Any(model => !ProviderCatalog.FishModelIds.Contains(model)))
            throw new BridgeException("Fish 可用模型无效，请在语音服务中重新选择。");
        return models;
    }

    /// <summary>
    /// The address a request goes to. OpenAI accepts a complete synthesis URL, an
    /// origin, or a base URL ending in /v1. Services whose path depends on the
    /// voice or model (ElevenLabs, Gemini, Bailian) keep only their origin here.
    /// </summary>
    public static Uri ResolveEndpoint(SpeechProviderProfile provider)
    {
        var value = string.IsNullOrWhiteSpace(provider.Endpoint) ? DefaultEndpoint(provider.Kind) : provider.Endpoint.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback) ||
            value.Contains("{{", StringComparison.Ordinal))
            throw new BridgeException("请填写完整 HTTPS 接口地址；仅本机 localhost / 127.0.0.1 可使用 HTTP，地址中不要包含密钥或查询参数。");
        switch (provider.Kind)
        {
            case SpeechProviderKind.OpenAiCompatible:
                var path = endpoint.AbsolutePath.TrimEnd('/');
                if (path.Length == 0)
                    endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/v1/audio/speech");
                else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                    endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + path + "/audio/speech");
                break;
            case SpeechProviderKind.Fish:
                if (endpoint.AbsoluteUri != ServiceIdentity.FishOfficialEndpoint && !endpoint.IsLoopback)
                    throw new BridgeException("Fish Audio 只连接官方接口，以免把 Fish 密钥发到别的地址；本机 fish-speech 服务请填写 127.0.0.1 地址。");
                break;
            case SpeechProviderKind.ElevenLabs:
            case SpeechProviderKind.Gemini:
            case SpeechProviderKind.DashScope:
                endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority));
                break;
        }
        return endpoint;
    }

    private static bool IsOfficialFish(SpeechProviderProfile provider)
    {
        try { return ResolveEndpoint(provider).AbsoluteUri == ServiceIdentity.FishOfficialEndpoint; }
        catch (BridgeException) { return false; }
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
        if (provider.Kind != SpeechProviderKind.EdgeTts)
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
        switch (provider.Kind)
        {
            case SpeechProviderKind.Fish when IsOfficialFish(provider):
                if (!FishVoice(voice.VoiceId) ||
                    !FishModels(provider).Contains(provider.Model.Length == 0 ? FishSpeech.Model : provider.Model, StringComparer.Ordinal) ||
                    provider.Latency is not ("normal" or "balanced" or "low"))
                    throw new BridgeException("Fish 配置需要选择已勾选的模型、有效的 Voice ID 和延迟模式。");
                break;
            case SpeechProviderKind.Fish:
                if (!Plain(voice.VoiceId, 256) || provider.Latency is not ("normal" or "balanced" or "low"))
                    throw new BridgeException("本机 fish-speech 需要填写参考音色 ID。");
                break;
            case SpeechProviderKind.OpenAiCompatible:
                if (!Plain(provider.Model, 256) || provider.AudioFormat is not ("mp3" or "wav") || voice.Instructions.Length > 4096)
                    throw new BridgeException("OpenAI 兼容接口需要模型名称，音频格式请选择 MP3 或 WAV。");
                break;
            case SpeechProviderKind.Doubao:
                if (!Header(provider.ResourceId) || !string.IsNullOrEmpty(provider.AppId) && !Header(provider.AppId))
                    throw new BridgeException("豆包需要资源 ID；旧版控制台还需 App ID，请核对它们与音色的授权关系。");
                break;
            case SpeechProviderKind.AzureSpeech:
                if (!Plain(voice.Language, 32))
                    throw new BridgeException("Azure 音色需要语言代码，例如 zh-CN。");
                break;
            case SpeechProviderKind.ElevenLabs:
                if (!FishVoice(voice.VoiceId) || !ModelName(provider.Model))
                    throw new BridgeException("ElevenLabs 需要模型名称和由字母数字组成的 Voice ID。");
                break;
            case SpeechProviderKind.Gemini:
                if (!ModelName(provider.Model) || !Plain(voice.VoiceId, 128) || voice.Instructions.Length > 2000)
                    throw new BridgeException("Gemini 需要模型名称（例如 gemini-3.8-flash-tts）和音色名。");
                break;
            case SpeechProviderKind.MiniMax:
            case SpeechProviderKind.DashScope:
                if (!ModelName(provider.Model) || voice.Instructions.Length > 2000)
                    throw new BridgeException("请选择该服务的模型。");
                break;
            case SpeechProviderKind.EdgeTts:
                if (!EdgeVoice(voice.VoiceId))
                    throw new BridgeException("Edge 音色名称格式应为 zh-CN-XiaoxiaoNeural 这样的形式。");
                break;
            case SpeechProviderKind.CustomHttp:
                CustomTemplates.Validate(provider.Template);
                break;
        }
    }

    public static bool RequiresCredential(SpeechProviderProfile provider) => provider.Kind switch
    {
        SpeechProviderKind.GptSoVits or SpeechProviderKind.EdgeTts or SpeechProviderKind.CustomHttp => false,
        SpeechProviderKind.OpenAiCompatible or SpeechProviderKind.Fish => !ResolveEndpoint(provider).IsLoopback,
        _ => true
    };

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
    private static bool ModelName(string value) => Plain(value, 128) && value.All(c => c is >= (char)33 and <= (char)126 && c is not ('?' or '#' or '\\'));
    private static bool EdgeVoice(string value) => Plain(value, 96) && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

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
            if (provider.Kind == SpeechProviderKind.EdgeTts)
                return await SynthesizeEdgeAsync(text, firstAudio, token);
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
                            return await ReadAudioAsync(response, firstAudio, deadline.Token);
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
                        // Only the free Fish model is retried: a retry of any paid
                        // request could be billed twice.
                        var freeFish = provider.Kind == SpeechProviderKind.Fish && provider.Model is "" or FishSpeech.Model;
                        if (!freeFish || status is not 429 && status is not (>= 500 and <= 599) ||
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

        private async Task<AudioData> SynthesizeEdgeAsync(string text, Action firstAudio, CancellationToken token)
        {
            if (owner.edge is null)
                throw new BridgeException("这个版本缺少 Edge 朗读组件，请重新安装软件。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(provider.TimeoutSeconds));
            byte[] bytes;
            try { bytes = await owner.edge(new(text, voice.VoiceId, voice.Speed), deadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new BridgeException("Edge 朗读请求超时，本条已跳过。");
            }
            var audio = Detect(bytes);
            firstAudio();
            return audio;
        }

        private HttpRequestMessage BuildRequest(Uri endpoint, string key, string text)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.UserAgent.ParseAdd("WeChatVoiceBridge");
            switch (provider.Kind)
            {
                case SpeechProviderKind.Fish:
                    if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    // Always name the model: without the header Fish falls back to a paid one.
                    if (IsOfficialFish(provider))
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
                case SpeechProviderKind.ElevenLabs:
                    request.RequestUri = new Uri(endpoint, "/v1/text-to-speech/" + Uri.EscapeDataString(voice.VoiceId) +
                        "?output_format=mp3_44100_128");
                    request.Headers.Add("xi-api-key", key);
                    var eleven = new Dictionary<string, object> { ["text"] = text, ["model_id"] = provider.Model };
                    if (Math.Abs(voice.Speed - 1) > 0.001)
                        eleven["voice_settings"] = new { speed = Math.Clamp(voice.Speed, 0.7, 1.2) };
                    request.Content = JsonContent.Create(eleven);
                    break;
                case SpeechProviderKind.MiniMax:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    request.Content = JsonContent.Create(new
                    {
                        model = provider.Model, text, stream = false,
                        voice_setting = new { voice_id = voice.VoiceId, speed = Math.Clamp(voice.Speed, 0.5, 2), vol = 1, pitch = 0 },
                        audio_setting = new { sample_rate = 32000, bitrate = 128000, format = "mp3", channel = 1 },
                        output_format = "hex"
                    });
                    break;
                case SpeechProviderKind.DashScope:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    if (IsCosyVoice(provider.Model))
                    {
                        request.RequestUri = new Uri(endpoint, "/api/v1/services/audio/tts/SpeechSynthesizer");
                        var input = new Dictionary<string, object>
                        {
                            ["text"] = text, ["voice"] = voice.VoiceId, ["format"] = "mp3", ["sample_rate"] = 24000,
                            ["rate"] = Math.Clamp(voice.Speed, 0.5, 2)
                        };
                        if (!string.IsNullOrWhiteSpace(voice.Instructions)) input["instruction"] = voice.Instructions;
                        request.Content = JsonContent.Create(new { model = provider.Model, input });
                    }
                    else
                    {
                        request.RequestUri = new Uri(endpoint, "/api/v1/services/aigc/multimodal-generation/generation");
                        request.Content = JsonContent.Create(new { model = provider.Model, input = new { text, voice = voice.VoiceId } });
                    }
                    break;
                case SpeechProviderKind.Gemini:
                    request.RequestUri = new Uri(endpoint, "/v1beta/models/" + Uri.EscapeDataString(provider.Model) + ":generateContent");
                    request.Headers.Add("x-goog-api-key", key);
                    var part = new Dictionary<string, object> { ["text"] = text };
                    if (!string.IsNullOrWhiteSpace(voice.Instructions))
                        part["speech_metadata"] = new { style = voice.Instructions };
                    request.Content = JsonContent.Create(new
                    {
                        contents = new[] { new { role = "user", parts = new[] { part } } },
                        generationConfig = new
                        {
                            responseModalities = new[] { "AUDIO" },
                            speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName = voice.VoiceId } } }
                        }
                    });
                    break;
                case SpeechProviderKind.CustomHttp:
                    CustomTemplates.Apply(request, provider.Template!, key, text, voice.VoiceId, provider.Model, voice.Speed);
                    break;
                default:
                    request.Dispose();
                    throw new BridgeException("尚未支持该语音接口类型。");
            }
            return request;
        }

        private async Task<AudioData> ReadAudioAsync(HttpResponseMessage response, Action firstAudio, CancellationToken token)
        {
            AudioData audio;
            switch (provider.Kind)
            {
                case SpeechProviderKind.Doubao:
                    audio = new(DecodeDoubao(await Download(response.Content, MaxWireBytes, null, token)));
                    break;
                case SpeechProviderKind.MiniMax:
                    audio = Detect(DecodeMiniMax(await Download(response.Content, MaxWireBytes, null, token)));
                    break;
                case SpeechProviderKind.DashScope:
                    var link = DashScopeAudioUrl(await Download(response.Content, MaxWireBytes, null, token));
                    audio = Detect(await FetchAsync(link, ".aliyuncs.com", token));
                    break;
                case SpeechProviderKind.Gemini:
                    audio = DecodeGemini(await Download(response.Content, MaxWireBytes, null, token));
                    break;
                case SpeechProviderKind.CustomHttp:
                    var template = provider.Template!;
                    if (template.Response == "json")
                    {
                        var value = CustomTemplates.Read(await Download(response.Content, MaxWireBytes, null, token), template.JsonPath);
                        audio = Detect(template.Encoding switch
                        {
                            "hex" => Convert.FromHexString(value),
                            "url" => await FetchAsync(value, null, token),
                            _ => Convert.FromBase64String(value)
                        });
                    }
                    else audio = Detect(await Download(response.Content, MaxAudioBytes, null, token));
                    break;
                default:
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
                    }, token);
                    audio = wave ? new(Wave(bytes), true) : Mp3(bytes);
                    token.ThrowIfCancellationRequested();
                    if (!announced) firstAudio();
                    return audio;
            }
            token.ThrowIfCancellationRequested();
            firstAudio();
            return audio;
        }

        /// <summary>
        /// Download an audio file the service only linked to. No credential is ever
        /// sent along, plain HTTP is upgraded, and Bailian links must stay on its
        /// own storage domain.
        /// </summary>
        private async Task<byte[]> FetchAsync(string link, string? requiredSuffix, CancellationToken token)
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var target) || target.UserInfo.Length != 0 ||
                target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp)
                throw new BridgeException("语音 API 返回的音频地址无效。");
            if (target.Scheme == Uri.UriSchemeHttp && !target.IsLoopback)
                target = new UriBuilder(target) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            if (requiredSuffix is not null && !target.Host.EndsWith(requiredSuffix, StringComparison.OrdinalIgnoreCase))
                throw new BridgeException("语音 API 返回的音频地址不在该服务自己的域名下，已拒绝下载。");
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.UserAgent.ParseAdd("WeChatVoiceBridge");
            using var response = await owner.http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                throw new BridgeException("下载合成好的音频失败，本条已跳过。");
            return await Download(response.Content, MaxAudioBytes, null, token);
        }

        private static bool IsCosyVoice(string model) => model.StartsWith("cosyvoice", StringComparison.OrdinalIgnoreCase);

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
            var audio = output.ToArray();
            ValidateAudio(audio, false);
            return audio;
        }

        /// <summary>MiniMax answers HTTP 200 even for failures; the verdict is in base_resp.</summary>
        private static byte[] DecodeMiniMax(byte[] wire)
        {
            using var document = JsonDocument.Parse(wire);
            var root = document.RootElement;
            if (!root.TryGetProperty("base_resp", out var status) || !status.TryGetProperty("status_code", out var code) ||
                !code.TryGetInt32(out var value))
                throw new BridgeException("MiniMax 响应缺少状态码。");
            if (value != 0)
                throw new BridgeException(value switch
                {
                    1004 or 2049 => "MiniMax 拒绝了密钥，请检查 API Key 是否属于这个站点（国内站与国际站不通用）。",
                    1008 or 2053 or 2056 => "MiniMax 余额或用量不足，请自行检查账户；程序不会充值。",
                    1002 or 1039 => "MiniMax 正在限流，本条已跳过。",
                    2054 => "MiniMax 找不到这个音色 ID。",
                    2042 => "当前账号无权使用这个 MiniMax 音色。",
                    2043 => "所选 MiniMax 模型不支持这个音色，请换模型或在音色里固定模型。",
                    2069 => "所选 MiniMax 模型已下线，请换一个模型。",
                    _ => "MiniMax 拒绝了请求，请核对模型、音色和参数。"
                });
            if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("audio", out var audio) ||
                audio.GetString() is not { Length: > 0 } hex)
                throw new BridgeException("MiniMax 没有返回音频。");
            return Convert.FromHexString(hex);
        }

        private static string DashScopeAudioUrl(byte[] wire)
        {
            using var document = JsonDocument.Parse(wire);
            var root = document.RootElement;
            if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && code.GetString() is { Length: > 0 } error)
                throw new BridgeException(error switch
                {
                    "InvalidApiKey" => "阿里云百炼拒绝了 API Key，请检查地域与 Key 是否对应。",
                    "Arrearage" => "阿里云百炼账户欠费或额度不足，请自行检查账户。",
                    "Throttling" or "Throttling.RateQuota" => "阿里云百炼正在限流，本条已跳过。",
                    _ => "阿里云百炼拒绝了请求，请核对模型与音色（复刻音色只能用创建它的模型）。"
                });
            if (root.TryGetProperty("output", out var output) && output.TryGetProperty("audio", out var audio) &&
                audio.TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } link)
                return link;
            throw new BridgeException("阿里云百炼没有返回音频地址。");
        }

        private static AudioData DecodeGemini(byte[] wire)
        {
            using var document = JsonDocument.Parse(wire);
            if (document.RootElement.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
                foreach (var candidate in candidates.EnumerateArray())
                    if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                        foreach (var part in parts.EnumerateArray())
                            if (part.TryGetProperty("inlineData", out var inline) && inline.TryGetProperty("data", out var data) &&
                                data.GetString() is { Length: > 0 } encoded)
                            {
                                var bytes = Convert.FromBase64String(encoded);
                                if (bytes.AsSpan().StartsWith("RIFF"u8))
                                    return new(Wave(bytes), true);
                                var mime = inline.TryGetProperty("mimeType", out var type) ? type.GetString() ?? "" : "";
                                var rate = Regex.Match(mime, @"rate=(\d+)") is { Success: true } match &&
                                    int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                                    ? parsed : 24000;
                                return new(Wave(AudioSignature.WaveFromPcm16(bytes, rate)), true);
                            }
            throw new BridgeException("Gemini 没有返回音频，可能是文字被安全策略拦下，或模型不支持语音输出。");
        }

        /// <summary>Recognise MP3 or WAV by content; an unknown format is refused.</summary>
        private static AudioData Detect(byte[] bytes) =>
            bytes.AsSpan().StartsWith("RIFF"u8) ? new(Wave(bytes), true) : Mp3(bytes);

        private static AudioData Mp3(byte[] bytes)
        {
            ValidateAudio(bytes, false);
            return new(bytes);
        }

        private static byte[] Wave(byte[] bytes)
        {
            if (AudioSignature.IsCompleteWave(bytes)) return bytes;
            if (AudioSignature.RepairStreamedWave(bytes) is { } repaired && AudioSignature.IsCompleteWave(repaired))
                return repaired;
            ValidateAudio(bytes, true);
            return bytes;
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

/// <summary>
/// The user-described HTTP API. The key may only appear in a header, every value
/// placed into the body is JSON-escaped, and an answer is read from a dotted path.
/// </summary>
public static class CustomTemplates
{
    private static readonly string[] Reserved = ["host", "content-length", "transfer-encoding", "connection"];
    private static readonly Regex HeaderName = new(@"\A[A-Za-z0-9!#$%&'*+.^_`|~-]{1,64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex PathSyntax = new(@"\A[A-Za-z0-9_\-]+(\[[0-9]+\])?(\.[A-Za-z0-9_\-]+(\[[0-9]+\])?)*\z", RegexOptions.CultureInvariant);

    public static void Validate(CustomHttpTemplate? template)
    {
        if (template is null)
            throw new BridgeException("自定义接口缺少请求模板。");
        if (string.IsNullOrWhiteSpace(template.Body) || template.Body.Length > 16_384 ||
            !template.Body.Contains("{{text}}", StringComparison.Ordinal))
            throw new BridgeException("请求体模板必须包含 {{text}}，长度不超过 16 KB。");
        if (template.Body.Contains("{{key}}", StringComparison.Ordinal))
            throw new BridgeException("出于安全考虑，{{key}} 只能写在请求头里。");
        if (template.Headers.Length > 4096)
            throw new BridgeException("请求头太长。");
        foreach (var (name, _) in ParseHeaders(template.Headers))
            if (!HeaderName.IsMatch(name) || Reserved.Contains(name.ToLowerInvariant()))
                throw new BridgeException($"请求头「{name}」无效或不允许自定义。");
        if (template.Response is not ("audio" or "json"))
            throw new BridgeException("返回方式请选择「直接返回音频」或「JSON 中的字段」。");
        if (template.Response == "json" && (!PathSyntax.IsMatch(template.JsonPath) ||
            template.Encoding is not ("base64" or "hex" or "url")))
            throw new BridgeException("请填写音频所在的 JSON 字段路径（例如 data.audio），并选择 base64 / hex / url。");
    }

    public static void Apply(HttpRequestMessage request, CustomHttpTemplate template, string key, string text,
        string voice, string model, double speed)
    {
        var mediaType = "application/json";
        foreach (var (name, raw) in ParseHeaders(template.Headers))
        {
            var needsKey = raw.Contains("{{key}}", StringComparison.Ordinal);
            // An optional key that was left blank drops the header instead of sending "Bearer ".
            if (needsKey && key.Length == 0) continue;
            var value = raw.Replace("{{key}}", key, StringComparison.Ordinal);
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) { mediaType = value; continue; }
            if (value.Any(c => c < 32 || c > 126) || !request.Headers.TryAddWithoutValidation(name, value))
                throw new BridgeException($"请求头「{name}」的值无效。");
        }
        var body = template.Body
            .Replace("{{text}}", Escape(text), StringComparison.Ordinal)
            .Replace("{{voice}}", Escape(voice), StringComparison.Ordinal)
            .Replace("{{model}}", Escape(model), StringComparison.Ordinal)
            .Replace("{{speed}}", speed.ToString("0.###", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try { using var _ = JsonDocument.Parse(body); }
            catch (JsonException) { throw new BridgeException("请求体模板填入文字后不是有效的 JSON，请检查引号和逗号。"); }
        }
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.TryParse(mediaType, out var parsed)
            ? parsed : throw new BridgeException("Content-Type 请求头无效。");
        request.Content = content;
    }

    /// <summary>Read a string at a dotted path such as "data.audio" or "result[0].audio".</summary>
    public static string Read(byte[] json, string path)
    {
        using var document = JsonDocument.Parse(json);
        var node = document.RootElement;
        foreach (var segment in path.Replace("]", "", StringComparison.Ordinal).Split('.', '['))
        {
            if (int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                if (node.ValueKind != JsonValueKind.Array || index >= node.GetArrayLength())
                    throw new BridgeException("返回的 JSON 里找不到音频字段，请核对字段路径。");
                node = node[index];
            }
            else if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(segment, out node))
                throw new BridgeException("返回的 JSON 里找不到音频字段，请核对字段路径。");
        }
        return node.ValueKind == JsonValueKind.String && node.GetString() is { Length: > 0 } value
            ? value : throw new BridgeException("返回的 JSON 里音频字段为空。");
    }

    private static IEnumerable<(string Name, string Value)> ParseHeaders(string headers)
    {
        foreach (var line in headers.Split('\n'))
        {
            var text = line.Trim();
            if (text.Length == 0) continue;
            var colon = text.IndexOf(':');
            if (colon <= 0) throw new BridgeException($"请求头「{text}」缺少冒号，格式应为 名称: 值。");
            yield return (text[..colon].Trim(), text[(colon + 1)..].Trim());
        }
    }

    private static string Escape(string value) =>
        JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();
}
