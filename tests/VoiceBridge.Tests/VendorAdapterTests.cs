using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;

namespace VoiceBridge.Tests;

/// <summary>
/// Request shape and answer decoding for the vendors added with the catalog. All
/// traffic is simulated: no real key, quota or network is used, so a pass here
/// proves the documented wire format, not that a vendor accepts a given account.
/// </summary>
public sealed class VendorAdapterTests
{
    private static SpeechProviderProfile Service(SpeechProviderKind kind, string model = "", string endpoint = "") => new()
    {
        Id = "svc", Name = "测试服务", Kind = kind, Model = model, Endpoint = endpoint, CredentialId = "cred"
    };

    private static VoiceProfile Voice(string id = "voice-1") => new()
    {
        Id = "v", Name = "测试音色", ProviderId = "svc", VoiceId = id
    };

    [Fact]
    public async Task ElevenLabsPutsTheVoiceInThePathAndTheKeyInItsOwnHeader()
    {
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("https://api.elevenlabs.io/v1/text-to-speech/JBFqnCBsd6RMkjVDRZzb?output_format=mp3_44100_128",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal("eleven-key", Assert.Single(request.Headers.GetValues("xi-api-key")));
            Assert.Null(request.Headers.Authorization);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("你好", json.RootElement.GetProperty("text").GetString());
            Assert.Equal("eleven_multilingual_v2", json.RootElement.GetProperty("model_id").GetString());
            Assert.False(json.RootElement.TryGetProperty("voice_settings", out _));
            return Ok(Mp3());
        }));
        var audio = await factory.Create(Service(SpeechProviderKind.ElevenLabs, "eleven_multilingual_v2",
                "https://api.elevenlabs.io/v1/anything"), Voice("JBFqnCBsd6RMkjVDRZzb"), () => "eleven-key")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.False(audio.IsWave);
    }

    [Fact]
    public async Task MiniMaxDecodesHexAudioAndReadsTheVerdictFromBaseResp()
    {
        var frame = Mp3();
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("https://api.minimax.cn/v1/t2a_v2", request.RequestUri!.AbsoluteUri);
            Assert.Equal("mm-key", request.Headers.Authorization!.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = json.RootElement;
            Assert.Equal("speech-2.8-hd", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.Equal("male-qn-qingse", root.GetProperty("voice_setting").GetProperty("voice_id").GetString());
            Assert.Equal("mp3", root.GetProperty("audio_setting").GetProperty("format").GetString());
            Assert.Equal("hex", root.GetProperty("output_format").GetString());
            return Json(new { data = new { audio = Convert.ToHexString(frame), status = 2 }, base_resp = new { status_code = 0, status_msg = "success" } });
        }));
        var audio = await factory.Create(Service(SpeechProviderKind.MiniMax, "speech-2.8-hd", "https://api.minimax.cn/v1/t2a_v2"),
            Voice("male-qn-qingse"), () => "mm-key").SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.Equal(frame, audio.Bytes);
    }

    [Theory]
    [InlineData(1004, "密钥")]
    [InlineData(1008, "余额")]
    [InlineData(2043, "不支持这个音色")]
    public async Task MiniMaxFailuresInsideHttp200AreExplainedWithoutServerText(int code, string expected)
    {
        using var factory = new SpeechProviderFactory(new Handler((_, _) => Task.FromResult(
            Json(new { base_resp = new { status_code = code, status_msg = "PRIVATE_SERVER_TEXT" } }))));
        var failure = await Assert.ThrowsAsync<BridgeException>(() => factory.Create(
                Service(SpeechProviderKind.MiniMax, "speech-2.8-hd"), Voice(), () => "k")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None));
        Assert.Contains(expected, failure.Message);
        Assert.DoesNotContain("PRIVATE", failure.Message);
    }

    [Fact]
    public async Task BailianCosyVoiceDownloadsTheLinkedFileWithoutTheKey()
    {
        var frame = Mp3();
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                // The storage link is fetched over HTTPS and never carries the API key.
                Assert.Equal("https://dashscope-result-bj.oss-cn-beijing.aliyuncs.com/a.mp3?sig=1", request.RequestUri!.AbsoluteUri);
                Assert.Null(request.Headers.Authorization);
                return Ok(frame);
            }
            Assert.Equal("https://dashscope.aliyuncs.com/api/v1/services/audio/tts/SpeechSynthesizer", request.RequestUri!.AbsoluteUri);
            Assert.Equal("ali-key", request.Headers.Authorization!.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("cosyvoice-v3-flash", json.RootElement.GetProperty("model").GetString());
            var input = json.RootElement.GetProperty("input");
            Assert.Equal("longanyang", input.GetProperty("voice").GetString());
            Assert.Equal("mp3", input.GetProperty("format").GetString());
            return Json(new { output = new { audio = new { url = "http://dashscope-result-bj.oss-cn-beijing.aliyuncs.com/a.mp3?sig=1" } } });
        }));
        var audio = await factory.Create(Service(SpeechProviderKind.DashScope, "cosyvoice-v3-flash"), Voice("longanyang"), () => "ali-key")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.Equal(frame, audio.Bytes);
    }

    [Fact]
    public async Task BailianQwenUsesTheMultimodalEndpointAndAcceptsWave()
    {
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Ok(Wave());
            Assert.Equal("https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation", request.RequestUri!.AbsoluteUri);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("Cherry", json.RootElement.GetProperty("input").GetProperty("voice").GetString());
            return Json(new { output = new { audio = new { url = "https://dashscope-result-bj.oss-cn-beijing.aliyuncs.com/a.wav" } } });
        }));
        var audio = await factory.Create(Service(SpeechProviderKind.DashScope, "qwen3-tts-flash"), Voice("Cherry"), () => "k")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.True(audio.IsWave);
    }

    [Fact]
    public async Task BailianLinksOutsideItsOwnDomainAreNotDownloaded()
    {
        var handler = new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? Ok(Mp3())
            : Json(new { output = new { audio = new { url = "https://evil.example.test/a.mp3" } } })));
        using var factory = new SpeechProviderFactory(handler);
        await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Service(SpeechProviderKind.DashScope, "cosyvoice-v2"), Voice(), () => "k")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task GeminiWrapsHeaderlessPcmAsWave()
    {
        var pcm = new byte[480];
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash-tts:generateContent",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal("g-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var config = json.RootElement.GetProperty("generationConfig");
            Assert.Equal("AUDIO", config.GetProperty("responseModalities")[0].GetString());
            Assert.Equal("Kore", config.GetProperty("speechConfig").GetProperty("voiceConfig")
                .GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString());
            return Json(new
            {
                candidates = new[] { new { content = new { parts = new[] { new { inlineData = new { mimeType = "audio/L16;codec=pcm;rate=24000", data = Convert.ToBase64String(pcm) } } } } } }
            });
        }));
        var audio = await factory.Create(Service(SpeechProviderKind.Gemini, "gemini-3.8-flash-tts"), Voice("Kore"), () => "g-key")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.True(audio.IsWave);
        Assert.Equal(44 + pcm.Length, audio.Bytes.Length);
        Assert.Equal(24000, BinaryPrimitives.ReadInt32LittleEndian(audio.Bytes.AsSpan(24)));
    }

    [Fact]
    public async Task GeminiWithoutAudioExplainsItself()
    {
        using var factory = new SpeechProviderFactory(new Handler((_, _) => Task.FromResult(Json(new { candidates = Array.Empty<object>() }))));
        var failure = await Assert.ThrowsAsync<BridgeException>(() => factory.Create(
                Service(SpeechProviderKind.Gemini, "gemini-3.8-flash-tts"), Voice("Kore"), () => "k")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None));
        Assert.Contains("Gemini", failure.Message);
    }

    [Fact]
    public async Task LocalFishSpeechNeedsNoKeyAndSendsNoModelHeader()
    {
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("http://127.0.0.1:8080/v1/tts", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("model"));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("my-reference", json.RootElement.GetProperty("reference_id").GetString());
            return Ok(Mp3());
        }));
        var local = Service(SpeechProviderKind.Fish, "", "http://127.0.0.1:8080/v1/tts");
        Assert.False(SpeechProviderFactory.RequiresCredential(local));
        Assert.Empty(SpeechProviderFactory.FishModels(local));
        await factory.Create(local, Voice("my-reference"), () => "").SynthesizeAsync("你好", () => { }, CancellationToken.None);
    }

    [Fact]
    public void FishKeysStillOnlyGoToTheOfficialAddressOrThisComputer()
    {
        Assert.Throws<BridgeException>(() =>
            SpeechProviderFactory.ResolveEndpoint(Service(SpeechProviderKind.Fish, "", "https://fish.example.test/v1/tts")));
        Assert.True(SpeechProviderFactory.RequiresCredential(Service(SpeechProviderKind.Fish)));
    }

    [Fact]
    public void FishAcceptsItsCurrentModelsButNothingElse()
    {
        var fish = Service(SpeechProviderKind.Fish, "s2.1-pro") with { AvailableModels = ["s2.1-pro-free", "s2.1-pro"] };
        var voice = Voice("bf8f5cb1501d4e5a950d26738db7f772");
        SpeechProviderFactory.Validate(fish, voice);
        Assert.Throws<BridgeException>(() => SpeechProviderFactory.Validate(fish with { AvailableModels = ["s2.1-pro", "speech-9"] }, voice));
        // A checked list never lets an unchecked model through.
        Assert.Throws<BridgeException>(() => SpeechProviderFactory.Validate(fish with { Model = "drama-3" }, voice));
    }

    [Fact]
    public async Task PaidFishModelsAreNotRetried()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var factory = new SpeechProviderFactory(handler);
        var fish = Service(SpeechProviderKind.Fish, "s2.1-pro") with { AvailableModels = ["s2.1-pro"] };
        await Assert.ThrowsAsync<BridgeException>(() => factory.Create(fish, Voice("abc"), () => "k")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task CustomTemplateEscapesTextAndKeepsTheKeyInHeaders()
    {
        var template = new CustomHttpTemplate
        {
            Headers = "Authorization: Bearer {{key}}\nX-Client: voice-bridge",
            Body = "{\"input\": \"{{text}}\", \"speaker\": \"{{voice}}\", \"rate\": {{speed}}}",
            Response = "json", JsonPath = "result[0].audio", Encoding = "base64"
        };
        var frame = Mp3();
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("https://tts.example.test/speak", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer custom-key", Assert.Single(request.Headers.GetValues("Authorization")));
            Assert.Equal("voice-bridge", Assert.Single(request.Headers.GetValues("X-Client")));
            var body = await request.Content!.ReadAsStringAsync(token);
            Assert.DoesNotContain("custom-key", body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("他说：\"你好\"\n再见", json.RootElement.GetProperty("input").GetString());
            Assert.Equal(1.5, json.RootElement.GetProperty("rate").GetDouble());
            return Json(new { result = new[] { new { audio = Convert.ToBase64String(frame) } } });
        }));
        var audio = await factory.Create(Service(SpeechProviderKind.CustomHttp, "", "https://tts.example.test/speak") with { Template = template },
                Voice("narrator") with { Speed = 1.5 }, () => "custom-key")
            .SynthesizeAsync("他说：\"你好\"\n再见", () => { }, CancellationToken.None);
        Assert.Equal(frame, audio.Bytes);
    }

    [Fact]
    public async Task CustomTemplateDropsTheAuthHeaderWhenTheKeyIsLeftBlank()
    {
        var template = new CustomHttpTemplate { Headers = "Authorization: Bearer {{key}}", Body = "{\"text\":\"{{text}}\"}" };
        using var factory = new SpeechProviderFactory(new Handler((request, _) =>
        {
            Assert.False(request.Headers.Contains("Authorization"));
            return Task.FromResult(Ok(Wave()));
        }));
        var audio = await factory.Create(Service(SpeechProviderKind.CustomHttp, "", "http://127.0.0.1:9000/tts") with { Template = template },
            Voice(), () => "").SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.True(audio.IsWave);
    }

    [Theory]
    [InlineData("{\"text\":\"{{text}}\",\"key\":\"{{key}}\"}", "Authorization: Bearer x", "audio", "")]
    [InlineData("{\"voice\":\"{{voice}}\"}", "", "audio", "")]
    [InlineData("{\"text\":\"{{text}}\"}", "Host: evil.example", "audio", "")]
    [InlineData("{\"text\":\"{{text}}\"}", "", "json", "")]
    [InlineData("{\"text\":\"{{text}}\"}", "no colon here", "audio", "")]
    public void UnsafeOrIncompleteTemplatesAreRefused(string body, string headers, string response, string path) =>
        Assert.Throws<BridgeException>(() => CustomTemplates.Validate(new CustomHttpTemplate
        {
            Body = body, Headers = headers, Response = response, JsonPath = path
        }));

    [Fact]
    public async Task AWaveWithPlaceholderSizesFromAStreamingServerIsRepaired()
    {
        var streamed = Wave();
        BinaryPrimitives.WriteUInt32LittleEndian(streamed.AsSpan(4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(streamed.AsSpan(40), uint.MaxValue);
        using var factory = new SpeechProviderFactory(new Handler((_, _) => Task.FromResult(Ok(streamed))));
        var reference = Voice() with { ReferenceAudioPath = "/ref.wav", PromptLanguage = "zh" };
        var audio = await factory.Create(Service(SpeechProviderKind.GptSoVits), reference, () => "")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.True(audio.IsWave);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(audio.Bytes.AsSpan(40)));
    }

    [Fact]
    public void EveryCatalogKindIsUnderstoodByTheFactory()
    {
        foreach (var preset in ProviderCatalog.All)
            Assert.NotEqual("未知语音接口", SpeechProviderFactory.Label(preset.Kind));
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static byte[] Mp3()
    {
        var bytes = new byte[417];
        bytes[0] = 255; bytes[1] = 251; bytes[2] = 144;
        return bytes;
    }

    private static byte[] Wave()
    {
        var bytes = new byte[48];
        "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 24000); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 48000);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), 4);
        return bytes;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Count);
            return send(request, cancellationToken);
        }
    }
}
