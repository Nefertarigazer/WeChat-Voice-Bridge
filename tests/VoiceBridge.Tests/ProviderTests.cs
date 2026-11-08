using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;

namespace VoiceBridge.Tests;

public sealed class ProviderTests
{
    [Fact]
    public void OlderFishSettingsOfferBothModelsWithoutChangingTheActiveOne()
    {
        var profile = Profile(SpeechProviderKind.Fish);
        Assert.Equal(new[] { FishSpeech.Model, FishSpeech.PaidModel }, SpeechProviderFactory.FishModels(profile));
        Assert.Equal(FishSpeech.Model, profile.Model);
    }

    [Fact]
    public void FishModelAvailabilitySurvivesSettingsAndCannotFallBackToAnUncheckedModel()
    {
        var profile = Profile(SpeechProviderKind.Fish) with { Model = FishSpeech.PaidModel, AvailableModels = [FishSpeech.PaidModel] };
        var restored = JsonSerializer.Deserialize<SpeechProviderProfile>(JsonSerializer.Serialize(profile))!;
        Assert.Equal(new[] { FishSpeech.PaidModel }, SpeechProviderFactory.FishModels(restored));
        SpeechProviderFactory.Validate(restored, Voice);
        Assert.Throws<BridgeException>(() => SpeechProviderFactory.Validate(restored with { Model = FishSpeech.Model }, Voice));
        Assert.Throws<BridgeException>(() => SpeechProviderFactory.FishModels(profile with { AvailableModels = [] }));
    }

    [Fact]
    public async Task FishModelsShareOneKeyAndAnotherConnectionKeepsItsOwnModelAndKey()
    {
        var received = new List<(string Model, string? Key)>();
        using var factory = new SpeechProviderFactory(new Handler((request, _) =>
        {
            received.Add((Assert.Single(request.Headers.GetValues("model")), request.Headers.Authorization?.Parameter));
            return Task.FromResult(Audio(Mp3()));
        }));
        var profile = Profile(SpeechProviderKind.Fish);
        await factory.Create(profile, Voice, () => "same-test-key").SynthesizeAsync("one", () => { }, CancellationToken.None);
        await factory.Create(profile with { Model = FishSpeech.PaidModel }, Voice, () => "same-test-key").SynthesizeAsync("two", () => { }, CancellationToken.None);
        await factory.Create(profile with { Id = "other" }, Voice with { ProviderId = "other" }, () => "other-test-key").SynthesizeAsync("three", () => { }, CancellationToken.None);
        Assert.Equal(new (string, string?)[] { (FishSpeech.Model, "same-test-key"), (FishSpeech.PaidModel, "same-test-key"), (FishSpeech.Model, "other-test-key") }, received);
    }

    [Fact]
    public async Task TwoContactsCanShareOneFishKeyWithIndependentModelsAndLatency()
    {
        var profile = Profile(SpeechProviderKind.Fish) with { CredentialId = "shared", Latency = "balanced" };
        var alice = new ContactProfile { ModelOverride = FishSpeech.PaidModel, FishLatencyOverride = "low" };
        var bob = new ContactProfile { FishLatencyOverride = "normal" };
        var restored = JsonSerializer.Deserialize<ContactProfile>(JsonSerializer.Serialize(alice))!;
        var received = new List<(string Model, string Latency)>();
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("shared-test-key", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            received.Add((Assert.Single(request.Headers.GetValues("model")), body.RootElement.GetProperty("latency").GetString()!));
            return Audio(Mp3());
        }));
        await factory.Create(restored.ApplySpeechPreferences(profile), Voice, () => "shared-test-key").SynthesizeAsync("one", () => { }, CancellationToken.None);
        await factory.Create(bob.ApplySpeechPreferences(profile), Voice, () => "shared-test-key").SynthesizeAsync("two", () => { }, CancellationToken.None);
        Assert.Equal(new[] { (FishSpeech.PaidModel, "low"), (FishSpeech.Model, "normal") }, received);
        Assert.Equal(FishSpeech.Model, profile.Model);
        Assert.Equal("balanced", profile.Latency);
        var azure = Profile(SpeechProviderKind.AzureSpeech);
        Assert.Equal(azure, alice.ApplySpeechPreferences(azure));
    }

    private static SpeechProviderProfile Profile(SpeechProviderKind kind = SpeechProviderKind.OpenAiCompatible) =>
        new() { Id = "connection-one", Name = "测试 API", Kind = kind, Model = kind == SpeechProviderKind.Fish ? FishSpeech.Model : "test-model", ResourceId = "seed-tts-2.0" };
    private static VoiceProfile Voice => new() { Id = "voice-one", Name = "测试音色", ProviderId = "connection-one", VoiceId = "private-voice" };

    [Fact]
    public void VoiceCannotCrossConnectionsEvenWithSameProtocol()
    {
        using var factory = new SpeechProviderFactory();
        var failure = Assert.Throws<BridgeException>(() => factory.Create(Profile() with { Id = "other" }, Voice, () => "test"));
        Assert.Contains("另一个 API", failure.Message);
        Assert.False(failure.Fatal);
    }

    [Fact]
    public void DisabledProviderCannotSynthesize()
    {
        using var factory = new SpeechProviderFactory();
        Assert.Throws<BridgeException>(() => factory.Create(Profile() with { Enabled = false }, Voice, () => "test"));
    }

    [Theory]
    [InlineData("https://api.example.test", "https://api.example.test/v1/audio/speech")]
    [InlineData("https://api.example.test/v1/", "https://api.example.test/v1/audio/speech")]
    [InlineData("https://api.example.test/prefix/v1", "https://api.example.test/prefix/v1/audio/speech")]
    [InlineData("https://api.example.test/custom/speak", "https://api.example.test/custom/speak")]
    [InlineData("http://127.0.0.1:8080/v1", "http://127.0.0.1:8080/v1/audio/speech")]
    [InlineData("http://[::1]:8080/custom", "http://[::1]:8080/custom")]
    public void OpenAiSupportsBaseUrlsAndExactSynthesisPaths(string input, string expected) =>
        Assert.Equal(expected, SpeechProviderFactory.ResolveEndpoint(Profile() with { Endpoint = input }).AbsoluteUri);

    [Theory]
    [InlineData("http://api.example.test/v1")]
    [InlineData("http://127.0.0.1.evil.test/v1")]
    [InlineData("https://key:secret@api.example.test/v1")]
    [InlineData("https://api.example.test/v1?api_key=private")]
    [InlineData("file:///C:/speech.wav")]
    public void UnsafeEndpointsAreRefused(string input) => Assert.Throws<BridgeException>(() =>
        SpeechProviderFactory.ResolveEndpoint(Profile() with { Endpoint = input }));

    [Fact]
    public async Task OpenAiRequestUsesSelectedModelVoiceAndPerRequestKey()
    {
        var keys = new List<string?>();
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("https://api.example.test/tts", request.RequestUri!.AbsoluteUri);
            keys.Add(request.Headers.Authorization?.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("测试文字", json.RootElement.GetProperty("input").GetString());
            Assert.Equal("test-model", json.RootElement.GetProperty("model").GetString());
            Assert.Equal("private-voice", json.RootElement.GetProperty("voice").GetString());
            Assert.Equal("mp3", json.RootElement.GetProperty("response_format").GetString());
            return Audio(Mp3());
        }));
        var selected = "credential-one";
        var speech = factory.Create(Profile() with { Endpoint = "https://api.example.test/tts" }, Voice, () => selected);
        var first = 0;
        await speech.SynthesizeAsync("测试文字", () => first++, CancellationToken.None);
        selected = "credential-two";
        await speech.SynthesizeAsync("测试文字", () => first++, CancellationToken.None);
        Assert.Equal(new[] { "credential-one", "credential-two" }, keys);
        Assert.Equal(2, first);
    }

    [Fact]
    public async Task OpenAiCustomVoiceAndWaveOutputAreExplicit()
    {
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("private-voice", json.RootElement.GetProperty("voice").GetProperty("id").GetString());
            Assert.Equal("自然一点", json.RootElement.GetProperty("instructions").GetString());
            Assert.Equal(0.8, json.RootElement.GetProperty("speed").GetDouble());
            Assert.Equal("wav", json.RootElement.GetProperty("response_format").GetString());
            return Audio(WaveWithExtraChunk());
        }));
        var audio = await factory.Create(Profile() with { AudioFormat = "wav" },
            Voice with { IsCustomVoice = true, Instructions = "自然一点", Speed = 0.8 }, () => "test-key")
            .SynthesizeAsync("hi", () => { }, CancellationToken.None);
        Assert.True(audio.IsWave);
    }

    [Fact]
    public async Task LocalOpenAiCanUseNoKey()
    {
        using var factory = new SpeechProviderFactory(new Handler((request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(Audio(Mp3()));
        }));
        await factory.Create(Profile() with { Endpoint = "http://localhost:8880/v1" }, Voice, () => "")
            .SynthesizeAsync("hello", () => { }, CancellationToken.None);
    }

    [Fact]
    public async Task RemoteOpenAiRequiresCredentialWithoutMakingRequest()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("Must not send"));
        using var factory = new SpeechProviderFactory(handler);
        await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile(), Voice, () => "")
            .SynthesizeAsync("hi", () => { }, CancellationToken.None));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task AzureEscapesSsmlAndDoesNotInheritBearerFromOtherProvider()
    {
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            if (request.Headers.Contains("Ocp-Apim-Subscription-Key"))
            {
                Assert.Null(request.Headers.Authorization);
                Assert.Equal("azure-secret", Assert.Single(request.Headers.GetValues("Ocp-Apim-Subscription-Key")));
                Assert.Equal("audio-24khz-96kbitrate-mono-mp3", Assert.Single(request.Headers.GetValues("X-Microsoft-OutputFormat")));
                var xml = XDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                XNamespace ns = "http://www.w3.org/2001/10/synthesis";
                var voice = xml.Root!.Element(ns + "voice")!;
                Assert.Equal("<voice> & hello", voice.Value);
                Assert.Equal("zh-CN-XiaoxiaoNeural", voice.Attribute("name")!.Value);
            }
            else Assert.Equal("openai-secret", request.Headers.Authorization?.Parameter);
            return Audio(Mp3());
        }));
        await factory.Create(Profile(), Voice, () => "openai-secret").SynthesizeAsync("hi", () => { }, CancellationToken.None);
        await factory.Create(Profile(SpeechProviderKind.AzureSpeech) with
        {
            Endpoint = "https://eastasia.tts.speech.microsoft.com/cognitiveservices/v1"
        }, Voice with { VoiceId = "zh-CN-XiaoxiaoNeural" }, () => "azure-secret")
            .SynthesizeAsync("<voice> & hello", () => { }, CancellationToken.None);
    }

    [Theory]
    [InlineData("")]
    [InlineData("app-test")]
    public async Task DoubaoAuthenticatesAndJoinsChunksBeforeDecoding(string appId)
    {
        var frame = Mp3();
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("seed-tts-2.0", Assert.Single(request.Headers.GetValues("X-Api-Resource-Id")));
            if (appId.Length == 0)
            {
                Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
                Assert.False(request.Headers.Contains("X-Api-App-Id"));
            }
            else
            {
                Assert.Equal(appId, Assert.Single(request.Headers.GetValues("X-Api-App-Id")));
                Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("X-Api-Access-Key")));
                Assert.False(request.Headers.Contains("X-Api-Key"));
            }
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("private-voice", json.RootElement.GetProperty("req_params").GetProperty("speaker").GetString());
            Assert.Equal("mp3", json.RootElement.GetProperty("req_params").GetProperty("audio_params").GetProperty("format").GetString());
            var wire = JsonSerializer.Serialize(new { code = 0, data = Convert.ToBase64String(frame[..200]) }) +
                JsonSerializer.Serialize(new { code = 0, data = Convert.ToBase64String(frame[200..]) }) +
                "\n{\"code\":20000000,\"data\":null}\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowPieces(Encoding.UTF8.GetBytes(wire))) };
        }));
        var audio = await factory.Create(Profile(SpeechProviderKind.Doubao) with { AppId = appId }, Voice, () => "test-key")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.Equal(frame, audio.Bytes);
        Assert.False(audio.IsWave);
    }

    [Theory]
    [InlineData("{\"code\":0,\"data\":\"AA==\"}")]
    [InlineData("{\"code\":45000000,\"message\":\"PRIVATE_MESSAGE\"}")]
    [InlineData("{\"code\":0,\"data\":\"not base64!\"}{\"code\":20000000}")]
    [InlineData("[]")]
    [InlineData("{\"code\":\"20000000\"}")]
    public async Task DoubaoRefusesIncompleteOrFailedResponsesWithoutLeakingServerText(string wire)
    {
        using var factory = new SpeechProviderFactory(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(wire)
        })));
        var failure = await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile(SpeechProviderKind.Doubao), Voice, () => "test-key")
            .SynthesizeAsync("hello", () => { }, CancellationToken.None));
        Assert.DoesNotContain("PRIVATE_MESSAGE", failure.Message);
        Assert.False(failure.Fatal);
    }

    [Fact]
    public async Task GptSoVitsUsesServerSideReferencePathAndCompleteWaveWithExtraChunks()
    {
        using var factory = new SpeechProviderFactory(new Handler(async (request, token) =>
        {
            Assert.Equal("http://127.0.0.1:9880/tts", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("/server/voices/reference.wav", json.RootElement.GetProperty("ref_audio_path").GetString());
            Assert.Equal("参考原文", json.RootElement.GetProperty("prompt_text").GetString());
            Assert.Equal("zh", json.RootElement.GetProperty("text_lang").GetString());
            Assert.Equal("wav", json.RootElement.GetProperty("media_type").GetString());
            Assert.False(json.RootElement.GetProperty("streaming_mode").GetBoolean());
            return Audio(WaveWithExtraChunk());
        }));
        var result = await factory.Create(Profile(SpeechProviderKind.GptSoVits), Voice with
        {
            ReferenceAudioPath = "/server/voices/reference.wav", ReferenceText = "参考原文"
        }, () => "").SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.True(result.IsWave);
    }

    [Fact]
    public async Task InvalidAudioAndTruncatedWaveAreRefused()
    {
        foreach (var bytes in new[] { "{\"error\":\"private-details\"}"u8.ToArray(), WaveWithExtraChunk()[..^2] })
        {
            using var factory = new SpeechProviderFactory(new Handler((_, _) => Task.FromResult(Audio(bytes))));
            await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile() with { AudioFormat = "wav" }, Voice, () => "test-key")
                .SynthesizeAsync("你好", () => { }, CancellationToken.None));
        }
    }

    [Fact]
    public async Task FreeFishModelIsExplicitAndInvalidModelCannotFallBack()
    {
        using var factory = new SpeechProviderFactory(new Handler((request, _) =>
        {
            Assert.Equal(FishSpeech.Model, Assert.Single(request.Headers.GetValues("model")));
            return Task.FromResult(Audio(Mp3()));
        }));
        await factory.Create(Profile(SpeechProviderKind.Fish) with { Model = "" }, Voice, () => "test-key")
            .SynthesizeAsync("你好", () => { }, CancellationToken.None);
        Assert.Throws<BridgeException>(() => factory.Create(Profile(SpeechProviderKind.Fish) with { Model = "wrong-model" }, Voice, () => "test-key"));
    }

    [Fact]
    public async Task RejectedKeyIsBlockedUntilCredentialChangesOrUserResets()
    {
        var selected = "bad-key";
        var handler = new Handler((request, _) => Task.FromResult(request.Headers.Authorization?.Parameter == "bad-key"
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Audio(Mp3())));
        using var factory = new SpeechProviderFactory(handler);
        var speech = factory.Create(Profile(), Voice, () => selected);
        for (var i = 0; i < 2; i++)
        {
            var failure = await Assert.ThrowsAsync<BridgeException>(() => speech.SynthesizeAsync("你好", () => { }, CancellationToken.None));
            Assert.False(failure.Fatal);
        }
        Assert.Equal(1, handler.Count);
        selected = "new-key";
        await speech.SynthesizeAsync("你好", () => { }, CancellationToken.None);
        selected = "bad-key";
        factory.ResetFailures("connection-one");
        await Assert.ThrowsAsync<BridgeException>(() => speech.SynthesizeAsync("你好", () => { }, CancellationToken.None));
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task RateLimitHonorsRetryAfterAcrossCreatedSpeechObjects()
    {
        var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return Task.FromResult(response);
        });
        using var factory = new SpeechProviderFactory(handler);
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile(), Voice, () => "test-key")
                .SynthesizeAsync("你好", () => { }, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(402)]
    [InlineData(503)]
    [InlineData(307)]
    public async Task OtherProvidersNeverRetryOrFollowRedirectsAutomatically(int code)
    {
        var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)code);
            response.Headers.Location = new Uri("https://other.example.test/should-not-get-key");
            return Task.FromResult(response);
        });
        using var factory = new SpeechProviderFactory(handler);
        var failure = await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile(), Voice, () => "private-key")
            .SynthesizeAsync("hello", () => { }, CancellationToken.None));
        Assert.False(failure.Fatal);
        Assert.Equal(1, handler.Count);
        Assert.DoesNotContain("private-key", failure.Message);
    }

    [Fact]
    public async Task CancellationReachesHttpAndNeverDeliversAudio()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new SpeechProviderFactory(new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Audio(Mp3());
        }));
        using var stop = new CancellationTokenSource();
        var first = 0;
        var pending = factory.Create(Profile(), Voice, () => "test-key").SynthesizeAsync("hello", () => first++, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, first);
    }

    [Fact]
    public async Task OversizedAudioIsRejectedBeforeReading()
    {
        using var factory = new SpeechProviderFactory(new Handler((_, _) =>
        {
            var response = Audio([]);
            response.Content.Headers.ContentLength = 17 * 1024 * 1024;
            return Task.FromResult(response);
        }));
        await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile(), Voice, () => "test-key")
            .SynthesizeAsync("hi", () => { }, CancellationToken.None));
    }

    [Fact]
    public async Task FishFreeRetriesAreBoundedAndNeverChangeModel()
    {
        var handler = new Handler((request, _) =>
        {
            Assert.Equal(FishSpeech.Model, Assert.Single(request.Headers.GetValues("model")));
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var factory = new SpeechProviderFactory(handler);
        await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile(SpeechProviderKind.Fish), Voice, () => "test-key")
            .SynthesizeAsync("hi", () => { }, CancellationToken.None));
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task DeadlineCancelsAnUnresponsiveServiceAndReportsTimeout()
    {
        using var factory = new SpeechProviderFactory(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Audio(Mp3());
        }));
        var error = await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile() with { TimeoutSeconds = 5 }, Voice, () => "test-key")
            .SynthesizeAsync("hi", () => { }, CancellationToken.None));
        Assert.Contains("超时", error.Message);
    }

    [Fact]
    public async Task PaidFishKeepsItsSelectedModelAndDoesNotAutomaticallyRetry()
    {
        var handler = new Handler((request, _) =>
        {
            Assert.Equal(FishSpeech.PaidModel, Assert.Single(request.Headers.GetValues("model")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        using var factory = new SpeechProviderFactory(handler);
        await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile(SpeechProviderKind.Fish) with { Model = FishSpeech.PaidModel }, Voice, () => "test-key")
            .SynthesizeAsync("hi", () => { }, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task NetworkErrorsDoNotLeakCredentialOrProviderErrorDetails()
    {
        using var factory = new SpeechProviderFactory(new Handler((_, _) => throw new HttpRequestException("PRIVATE_KEY_SECRET")));
        var error = await Assert.ThrowsAsync<BridgeException>(() => factory.Create(Profile(), Voice, () => "PRIVATE_KEY_SECRET")
            .SynthesizeAsync("hi", () => { }, CancellationToken.None));
        Assert.DoesNotContain("PRIVATE_KEY_SECRET", error.Message);
        Assert.False(error.Fatal);
    }

    private static HttpResponseMessage Audio(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static byte[] Mp3()
    {
        var bytes = new byte[417];
        bytes[0] = 255; bytes[1] = 251; bytes[2] = 144;
        return bytes;
    }
    private static byte[] WaveWithExtraChunk()
    {
        // A valid 16-bit PCM file with a JUNK chunk, so the PCM header is not assumed to be 44 bytes.
        var bytes = new byte[58];
        "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 24000); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 48000);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "JUNK"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), 4);
        "data"u8.CopyTo(bytes.AsSpan(48)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 2);
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

    private sealed class SlowPieces(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
    }
}
