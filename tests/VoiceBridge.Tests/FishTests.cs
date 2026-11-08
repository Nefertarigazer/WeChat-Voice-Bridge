using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;

namespace VoiceBridge.Tests;

public sealed class FishTests
{
    [Fact]
    public async Task FixedModelAndVoiceIdAndFirstCompleteFrame()
    {
        using var http = new HttpClient(new FakeHandler(async (request, _, _) =>
        {
            Assert.Equal("https://api.fish.audio/v1/tts", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("s2.1-pro-free", Assert.Single(request.Headers.GetValues("model")));
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "unit-key"), request.Headers.Authorization);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("你好", json.RootElement.GetProperty("text").GetString());
            Assert.Equal("unit-voice", json.RootElement.GetProperty("reference_id").GetString());
            Assert.Equal("balanced", json.RootElement.GetProperty("latency").GetString());
            Assert.Equal("mp3", json.RootElement.GetProperty("format").GetString());
            var frame = new byte[417];
            frame[0] = 255;
            frame[1] = 251;
            frame[2] = 144;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(frame) };
        }));
        var hits = 0;
        var result = await Create(http).SynthesizeAsync("你好", () => hits++, CancellationToken.None);
        Assert.Equal(417, result.Bytes.Length);
        Assert.Equal(1, hits);
    }

    [Fact]
    public async Task TheVoiceThatIsChosenAtTheMomentIsUsed()
    {
        var voices = new List<string>();
        using var http = new HttpClient(new FakeHandler(async (request, _, _) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            voices.Add(json.RootElement.GetProperty("reference_id").GetString()!);
            var frame = new byte[417];
            frame[0] = 255;
            frame[1] = 251;
            frame[2] = 144;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(frame) };
        }));
        var chosen = "first-voice";
        var speech = new FishSpeech(http, "unit-key", chosen, "balanced", selectVoice: () => chosen);
        await speech.SynthesizeAsync("一", () => { }, CancellationToken.None);
        chosen = "second-voice";
        await speech.SynthesizeAsync("二", () => { }, CancellationToken.None);
        Assert.Equal(new[] { "first-voice", "second-voice" }, voices);
    }

    [Fact]
    public async Task StreamingKeepsItsOriginalKeyAndTheNextMessageUsesTheNewKey()
    {
        var selected = "unit-key-one";
        var used = new List<string?>();
        using var http = new HttpClient(new FakeHandler((request, _, _) =>
        {
            used.Add(request.Headers.Authorization?.Parameter);
            var content = new ByteArrayContent([0, 0]);
            content.Headers.ContentType = new("audio/pcm");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        var speech = new FishSpeech(http, selected, "unit-voice", "balanced", streamPcm: true, selectApiKey: () => selected);
        var first = await speech.SynthesizeAsync("first", () => { }, CancellationToken.None);
        selected = "unit-key-two";
        await first.Pcm!((_, _) => ValueTask.CompletedTask, CancellationToken.None);
        var second = await speech.SynthesizeAsync("second", () => { }, CancellationToken.None);
        await second.Pcm!((_, _) => ValueTask.CompletedTask, CancellationToken.None);
        Assert.Equal(new[] { "unit-key-one", "unit-key-two" }, used);
    }

    [Fact]
    public async Task RateLimitRemainsScopedToTheOriginalKey()
    {
        var selected = "unit-key-one";
        var handler = new FakeHandler((request, _, _) =>
        {
            if (request.Headers.Authorization?.Parameter == "unit-key-one")
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
                return Task.FromResult(limited);
            }
            var frame = new byte[417];
            frame[0] = 255;
            frame[1] = 251;
            frame[2] = 144;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(frame) });
        });
        using var http = new HttpClient(handler);
        var speech = new FishSpeech(http, selected, "unit-voice", "balanced", selectApiKey: () => selected);
        await Assert.ThrowsAsync<BridgeException>(() => speech.SynthesizeAsync("first", () => { }, CancellationToken.None));
        selected = "unit-key-two";
        await speech.SynthesizeAsync("second", () => { }, CancellationToken.None);
        selected = "unit-key-one";
        await Assert.ThrowsAsync<BridgeException>(() => speech.SynthesizeAsync("third", () => { }, CancellationToken.None));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task AnEmptyVoiceIsRefused()
    {
        using var http = new HttpClient(Status(200));
        var speech = new FishSpeech(http, "unit-key", "unit-voice", "balanced", selectVoice: () => "");
        await Assert.ThrowsAsync<BridgeException>(() =>
            speech.SynthesizeAsync("你好", () => { }, CancellationToken.None));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task FatalErrorsNeverRetry(int status)
    {
        var handler = Status(status);
        using var http = new HttpClient(handler);
        var result = await Assert.ThrowsAsync<BridgeException>(() =>
            Create(http).SynthesizeAsync("test", () => { }, CancellationToken.None));
        Assert.True(result.Fatal);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task SwitchingModelsUsesNewHeaderAndPaidBalanceFailureLeavesSessionOpen()
    {
        var models = new List<string>();
        var current = FishSpeech.Model;
        using var http = new HttpClient(new FakeHandler((request, _, _) =>
        {
            var model = Assert.Single(request.Headers.GetValues("model"));
            models.Add(model);
            if (model == FishSpeech.PaidModel)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PaymentRequired));
            var frame = new byte[417];
            frame[0] = 255;
            frame[1] = 251;
            frame[2] = 144;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(frame) });
        }));
        var speech = new FishSpeech(http, "unit-key", "unit-voice", "balanced", selectModel: () => current);
        await speech.SynthesizeAsync("first", () => { }, CancellationToken.None);
        current = FishSpeech.PaidModel;
        var error = await Assert.ThrowsAsync<BridgeException>(() => speech.SynthesizeAsync("second", () => { }, CancellationToken.None));
        Assert.False(error.Fatal);
        current = FishSpeech.Model;
        await speech.SynthesizeAsync("third", () => { }, CancellationToken.None);
        Assert.Equal(new[] { FishSpeech.Model, FishSpeech.PaidModel, FishSpeech.Model }, models);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task TransientErrorsRetryAtMostTwice(int status)
    {
        var handler = Status(status, TimeSpan.Zero);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<BridgeException>(() => Create(http).SynthesizeAsync("test", () => { }, CancellationToken.None));
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task RespectsLongRetryAfterByFailingWithoutEarlyRetry()
    {
        var handler = Status(429, TimeSpan.FromSeconds(120));
        using var http = new HttpClient(handler);
        var speech = Create(http);
        await Assert.ThrowsAsync<BridgeException>(() => speech.SynthesizeAsync("test", () => { }, CancellationToken.None));
        await Assert.ThrowsAsync<BridgeException>(() => speech.SynthesizeAsync("next", () => { }, CancellationToken.None));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task TimeoutAndCallerCancellationAreDistinct()
    {
        using var http = new HttpClient(new FakeHandler(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }));
        var error = await Assert.ThrowsAsync<BridgeException>(() =>
            Create(http, TimeSpan.FromMilliseconds(50)).SynthesizeAsync("test", () => { }, CancellationToken.None));
        Assert.Contains("超时", error.Message);
        using var cancel = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(http).SynthesizeAsync("test", () => { }, cancel.Token));
    }

    [Fact]
    public async Task RejectsNonAudioSuccessBody()
    {
        var handler = new FakeHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("not audio") }));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<BridgeException>(() => Create(http).SynthesizeAsync("test", () => { }, CancellationToken.None));
    }

    [Fact]
    public async Task PcmStreamReassemblesSplitSamplesAndDetectsTruncatedTail()
    {
        var handler = new FakeHandler(async (request, call, _) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("pcm", body.RootElement.GetProperty("format").GetString());
            Assert.Equal(24000, body.RootElement.GetProperty("sample_rate").GetInt32());
            var content = new StreamContent(new FragmentedStream(call == 1 ? [1, 2, 3, 4] : [1, 2, 3]));
            content.Headers.ContentType = new("application/octet-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var http = new HttpClient(handler);
        var speech = new FishSpeech(http, "unit-key", "unit-voice", "balanced", streamPcm: true);
        var first = 0;
        var audio = await speech.SynthesizeAsync("你好", () => first++, CancellationToken.None);
        Assert.Equal(0, handler.Requests);
        var received = new List<byte>();
        await audio.Pcm!((chunk, _) =>
        {
            Assert.Equal(0, chunk.Length % 2);
            received.AddRange(chunk.ToArray());
            return ValueTask.CompletedTask;
        }, CancellationToken.None);
        Assert.Equal(1, first);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, received);
        await Assert.ThrowsAsync<BridgeException>(() => audio.Pcm((_, _) => ValueTask.CompletedTask, CancellationToken.None));
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private static FishSpeech Create(HttpClient client, TimeSpan? timeout = null) =>
        new(client, "unit-key", "unit-voice", "balanced", timeout);

    private static FakeHandler Status(int code, TimeSpan? retry = null) => new((request, _, _) =>
    {
        Assert.Equal(FishSpeech.Model, Assert.Single(request.Headers.GetValues("model")));
        var response = new HttpResponseMessage((HttpStatusCode)code);
        if (retry.HasValue)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retry.Value);
        return Task.FromResult(response);
    });

    private sealed class FakeHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            response(request, Interlocked.Increment(ref Requests), cancellationToken);
    }
}
