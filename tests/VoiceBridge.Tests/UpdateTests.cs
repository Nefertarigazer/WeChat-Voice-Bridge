using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("wvb-update-test-").FullName;
    private string Updates => Path.Combine(root, "data", "updates");
    private string Installation => Path.Combine(root, "安装目录");

    [Theory]
    [InlineData("1.2.0", "1.2.0", 0)]
    [InlineData("1.1.9", "1.2.0", -1)]
    [InlineData("1.10.0", "1.2.0", 1)]
    [InlineData("1.3.0-rc.2", "1.3.0-rc.11", -1)]
    [InlineData("1.3.0-rc.1", "1.3.0", -1)]
    [InlineData("1.3.0+build.6", "1.3.0+build.5", 0)]
    public void VersionsUseSemanticPrecedence(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(UpdateManifest.CompareVersions(left, right)));

    [Fact]
    public void ManifestRejectsMissingField()
    {
        var json = """{"version":"1.3.0","publishedAt":"2026-11-06T14:30:00Z","url":"https://iceland.de5.net/voice-bridge/build.zip","sha256":"0000000000000000000000000000000000000000000000000000000000000000","size":42}""";
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("http://iceland.de5.net/voice-bridge/build.zip")]
    [InlineData("https://iceland.de5.net@outside.example/voice-bridge/build.zip")]
    [InlineData("https://iceland.de5.net/voice-bridge/../build.zip")]
    public void ManifestRejectsUntrustedDownloadLocations(string url) =>
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Parse(ManifestJson("1.3.0", url: url)));

    [Theory]
    [InlineData("1.2.0", false)]
    [InlineData("1.0.0", false)]
    [InlineData("1.3.0", true)]
    public async Task CheckSendsVersionedUncachedGetAndOnlyOffersNewerRelease(string offeredVersion, bool available)
    {
        using var http = new HttpClient(new MockHttp((request, _) =>
        {
            Assert.Equal("https://iceland.de5.net/voice-bridge/latest.json", request.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.True(request.Headers.CacheControl?.NoCache);
            Assert.Equal("WeChatVoiceBridge/1.2.0", request.Headers.UserAgent.ToString());
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Content);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(ManifestJson(offeredVersion)) };
        }));
        var manifest = await new UpdateClient(http, "1.2.0").CheckAsync(CancellationToken.None);
        Assert.Equal(available, manifest is not null);
        if (manifest is not null)
            Assert.Equal("第一行\n第二行", manifest.Notes);
    }

    [Fact]
    public async Task DownloadReportsProgressAndProducesVerifiedZip()
    {
        var content = new byte[200_000];
        Random.Shared.NextBytes(content);
        using var http = DownloadHttp(content);
        var progress = new RecordedProgress();
        var archive = await new UpdateClient(http, "1.2.0")
            .DownloadAsync(Manifest(content), Updates, progress, CancellationToken.None);
        Assert.Equal(content, File.ReadAllBytes(archive));
        Assert.Equal(0, progress.Values[0]);
        Assert.Equal(100, progress.Values[^1]);
        Assert.Contains(progress.Values, value => value is > 0 and < 100);
        Assert.EndsWith(".zip", archive);
        Assert.Single(Directory.GetFiles(Updates, "*.zip"));
        Assert.Single(Directory.GetFiles(Updates, "*.state.json"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongSizeOrHashLeavesNoInstallableFile(bool wrongSize)
    {
        byte[] bytes = [1, 2, 3];
        using var http = DownloadHttp(bytes);
        var manifest = Manifest(wrongSize ? [1, 2] : [4, 5, 6]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(http, "1.2.0")
            .DownloadAsync(manifest, Updates, new RecordedProgress(), CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Updates));
    }

    [Fact]
    public async Task CancelledDownloadRetainsPartialFileAndRecoversAfterReopen()
    {
        byte[] bytes = new byte[128_000];
        using var http = DownloadHttp(bytes);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordedProgress { Callback = percent => { if (percent > 0) cancellation.Cancel(); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateClient(http, "1.2.0")
            .DownloadAsync(Manifest(bytes), Updates, progress, cancellation.Token));
        Assert.Single(Directory.GetFiles(Updates, "*.part"));
        Assert.Equal(Manifest(bytes), await new UpdateClient(http, "1.2.0").ReadPendingAsync(Updates, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReopenedDownloadResumesOrSafelyReplacesWhenServerIgnoresRange(bool rangeSupported)
    {
        var bytes = new byte[200_000];
        Random.Shared.NextBytes(bytes);
        using var cancellation = new CancellationTokenSource();
        using (var initial = DownloadHttp(bytes))
        {
            var progress = new RecordedProgress { Callback = value => { if (value > 0) cancellation.Cancel(); } };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateClient(initial, "1.2.0")
                .DownloadAsync(Manifest(bytes), Updates, progress, cancellation.Token));
        }
        var offset = new FileInfo(Assert.Single(Directory.GetFiles(Updates, "*.part"))).Length;
        Assert.InRange(offset, 1, bytes.Length - 1);
        using var http = new HttpClient(new MockHttp((request, _) =>
        {
            Assert.Equal(offset, Assert.Single(request.Headers.Range!.Ranges).From);
            var body = new ByteArrayContent(rangeSupported ? bytes[(int)offset..] : bytes);
            if (rangeSupported) body.Headers.ContentRange = new(offset, bytes.Length - 1, bytes.Length);
            return new(rangeSupported ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = body };
        }));
        var client = new UpdateClient(http, "1.2.0");
        var pending = await client.ReadPendingAsync(Updates, CancellationToken.None);
        Assert.Equal(Manifest(bytes), pending);
        var archive = await client.DownloadAsync(pending!, Updates, new RecordedProgress(), CancellationToken.None);
        Assert.Equal(bytes, File.ReadAllBytes(archive));
        Assert.Empty(Directory.GetFiles(Updates, "*.part"));
    }

    [Fact]
    public async Task VerifiedDownloadSurvivesReopenAndWorksOffline()
    {
        byte[] bytes = [9, 8, 7, 6];
        string saved;
        using (var http = DownloadHttp(bytes))
            saved = await new UpdateClient(http, "1.2.0").DownloadAsync(Manifest(bytes), Updates,
                new RecordedProgress(), CancellationToken.None);
        using var offline = new HttpClient(new MockHttp((_, _) => throw new InvalidOperationException("Must not use network")));
        var reopened = new UpdateClient(offline, "1.2.0");
        Assert.Equal(Manifest(bytes), await reopened.ReadPendingAsync(Updates, CancellationToken.None));
        Assert.Equal(saved, await reopened.DownloadAsync(Manifest(bytes), Updates, new RecordedProgress(), CancellationToken.None));
        Assert.Null(await new UpdateClient(offline, "1.3.0").ReadPendingAsync(Updates, CancellationToken.None));
    }

    [Fact]
    public async Task CorruptLocalStateIsIgnoredAndCacheValidationHonorsCancellation()
    {
        Directory.CreateDirectory(Updates);
        File.WriteAllText(Path.Combine(Updates, "broken.zip.state.json"), "not json");
        using var http = DownloadHttp([1, 2, 3]);
        var client = new UpdateClient(http, "1.2.0");
        Assert.Null(await client.ReadPendingAsync(Updates, CancellationToken.None));
        await client.DownloadAsync(Manifest([1, 2, 3]), Updates, new RecordedProgress(), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetCachedAsync(Manifest([1, 2, 3]), Updates, cancellation.Token));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("payload/settings.json")]
    [InlineData("payload/fish-keys.dpapi")]
    [InlineData("unins000.exe")]
    [InlineData("payload/COM¹.txt")]
    public void StageRejectsUnsafeZipEntries(string name)
    {
        PrepareOldInstall();
        var zip = WriteZip((name, "bad"));
        var manifest = Manifest(File.ReadAllBytes(zip));
        Assert.Throws<InvalidDataException>(() =>
            UpdateInstaller.Prepare(zip, manifest, Installation, Updates, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(Updates, "outside.txt")));
        Assert.Empty(Directory.GetDirectories(Updates));
    }

    [Fact]
    public void StageRechecksHashBeforeExtracting()
    {
        PrepareOldInstall();
        var zip = WriteZip((UpdateInstaller.ExecutableName, "new executable"));
        var manifest = Manifest(File.ReadAllBytes(zip)) with { Sha256 = new string('0', 64) };
        Assert.Throws<InvalidDataException>(() =>
            UpdateInstaller.Prepare(zip, manifest, Installation, Updates, CancellationToken.None));
        Assert.Empty(Directory.GetDirectories(Updates));
    }

    [Fact]
    public void InstallationReportsRealPhasesAndDoesNotFinishBeforeRestartConfirmation()
    {
        PrepareOldInstall();
        var zip = WriteZip((UpdateInstaller.ExecutableName, "new executable"), ("runtime/new.txt", "new data"));
        var progress = new InstallProgress();
        var stage = UpdateInstaller.Prepare(zip, Manifest(File.ReadAllBytes(zip)), Installation, Updates, CancellationToken.None, progress);
        Assert.Contains(progress.Values, value => value.Stage.Contains("校验"));
        Assert.Contains(progress.Values, value => value.Stage.Contains("解压") && value.Percent == 100);
        progress.Values.Clear();
        UpdateInstaller.Apply(stage, Installation, Updates, "1.2.0", _ =>
        {
            Assert.Equal("正在启动并确认新版本", progress.Values[^1].Stage);
            Assert.Equal(95, progress.Values[^1].Percent);
        }, progress);
        Assert.Contains(progress.Values, value => value.Stage.Contains("备份"));
        Assert.Contains(progress.Values, value => value.Stage.Contains("安装新版本"));
        Assert.DoesNotContain(progress.Values, value => value.Percent == 100);
    }

    private sealed class InstallProgress : IProgress<InstallationProgress>
    {
        public List<InstallationProgress> Values { get; } = [];
        public void Report(InstallationProgress value) => Values.Add(value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyBacksUpBeforeReplacingAndRollsBackIfRestartFails(bool restartFails)
    {
        PrepareOldInstall();
        var zip = WriteZip((UpdateInstaller.ExecutableName, "new executable"), ("runtime/new.txt", "new data"));
        var stage = UpdateInstaller.Prepare(zip, Manifest(File.ReadAllBytes(zip)), Installation, Updates, CancellationToken.None);
        var called = false;
        void Restart(string exe)
        {
            called = true;
            Assert.Equal("new executable", File.ReadAllText(exe));
            Assert.Equal("old executable", File.ReadAllText(Assert.Single(
                Directory.GetFiles(Path.Combine(Updates, "backup-1.2.0"), UpdateInstaller.ExecutableName, SearchOption.AllDirectories))));
            Assert.True(File.Exists(Path.Combine(Installation, "runtime", "new.txt")));
            if (restartFails)
                throw new IOException("simulated startup failure");
        }

        if (restartFails)
            Assert.Throws<IOException>(() => UpdateInstaller.Apply(stage, Installation, Updates, "1.2.0", Restart));
        else
            UpdateInstaller.Apply(stage, Installation, Updates, "1.2.0", Restart);

        Assert.True(called);
        Assert.Equal(restartFails ? "old executable" : "new executable",
            File.ReadAllText(Path.Combine(Installation, UpdateInstaller.ExecutableName)));
        Assert.Equal(!restartFails, Directory.Exists(Path.Combine(Installation, "runtime")));
        Assert.Equal("sentinel", File.ReadAllText(Path.Combine(root, "data", "settings.json")));
        Assert.Equal("uninstaller", File.ReadAllText(Path.Combine(Installation, "unins000.exe")));
    }

    private void PrepareOldInstall()
    {
        Directory.CreateDirectory(Installation);
        Directory.CreateDirectory(Updates);
        File.WriteAllText(Path.Combine(Installation, UpdateInstaller.ExecutableName), "old executable");
        File.WriteAllText(Path.Combine(Installation, "unins000.exe"), "uninstaller");
        File.WriteAllText(Path.Combine(root, "data", "settings.json"), "sentinel");
    }

    private string WriteZip(params (string Name, string Content)[] files)
    {
        var path = Path.Combine(Updates, "verified.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            using var stream = new StreamWriter(archive.CreateEntry(file.Name).Open());
            stream.Write(file.Content);
        }
        return path;
    }

    private static byte[] ManifestJson(string version, byte[]? bytes = null,
        string url = "https://iceland.de5.net/voice-bridge/build.zip")
    {
        bytes ??= [42];
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            version, publishedAt = "2026-11-06T14:30:00Z", url,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), size = bytes.Length, notes = "第一行\n第二行"
        });
    }

    private static UpdateManifest Manifest(byte[] bytes) => UpdateManifest.Parse(ManifestJson("1.3.0", bytes));

    private static HttpClient DownloadHttp(byte[] content) => new(new MockHttp((request, _) =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://iceland.de5.net/voice-bridge/build.zip", request.RequestUri?.AbsoluteUri);
        Assert.Null(request.Headers.Authorization);
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
    }));

    private sealed class RecordedProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];
        public Action<int>? Callback { get; init; }
        public void Report(int value)
        {
            Values.Add(value);
            Callback?.Invoke(value);
        }
    }

    private sealed class MockHttp(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request, cancellationToken));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
