using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class ProviderCatalogTests
{
    [Fact]
    public void EveryPresetHasAUniqueIdAndAName()
    {
        var ids = ProviderCatalog.All.Select(item => item.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ProviderCatalog.All, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Label));
            Assert.False(string.IsNullOrWhiteSpace(item.Summary));
        });
    }

    [Fact]
    public void AllThreeGroupsAreOffered()
    {
        Assert.Contains(ProviderCatalog.All, item => item.Category == PresetCategory.Mainstream);
        Assert.Contains(ProviderCatalog.All, item => item.Category == PresetCategory.Generic);
        Assert.Contains(ProviderCatalog.All, item => item.Category == PresetCategory.Local);
        Assert.NotNull(ProviderCatalog.Find(ProviderCatalog.OpenAiCompatibleId));
        Assert.NotNull(ProviderCatalog.Find(ProviderCatalog.CustomHttpId));
    }

    [Fact]
    public void AddressesAreHttpsExceptOnThisComputer()
    {
        foreach (var endpoint in ProviderCatalog.All.SelectMany(item => item.Endpoints))
        {
            Assert.True(Uri.TryCreate(endpoint.Url, UriKind.Absolute, out var uri), endpoint.Url);
            Assert.True(uri!.Scheme == Uri.UriSchemeHttps || uri.IsLoopback, endpoint.Url);
            Assert.Empty(uri.Query);
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Label));
        }
        Assert.All(ProviderCatalog.All.Where(item => item.Category == PresetCategory.Local),
            item => Assert.All(item.Endpoints, endpoint => Assert.True(new Uri(endpoint.Url).IsLoopback)));
    }

    [Fact]
    public void ServicesWithoutAFixedAddressLetTheUserTypeOne()
    {
        Assert.All(ProviderCatalog.All.Where(item => item.Endpoints.Length == 0 && item.Kind != SpeechProviderKind.EdgeTts),
            item => Assert.True(item.CustomEndpoint, item.Id));
    }

    [Fact]
    public void ModelsAreUniqueAndVoicesOnlyNameModelsTheServiceOffers()
    {
        foreach (var preset in ProviderCatalog.All)
        {
            var models = preset.Models.Select(item => item.Id).ToArray();
            Assert.Equal(models.Length, models.Distinct(StringComparer.Ordinal).Count());
            var voices = preset.Voices.Select(item => item.Id).ToArray();
            Assert.Equal(voices.Length, voices.Distinct(StringComparer.Ordinal).Count());
            foreach (var voice in preset.Voices.Where(item => item.Models is not null))
                Assert.All(voice.Models!, model => Assert.Contains(model, models));
        }
    }

    [Fact]
    public void MainstreamVendorsThatNeedAKeySayWhereToGetIt()
    {
        Assert.All(ProviderCatalog.All.Where(item => item.Category == PresetCategory.Mainstream && item.Key == KeyNeed.Required),
            item => Assert.StartsWith("https://", item.KeyUrl));
        Assert.All(ProviderCatalog.All.Where(item => item.Category == PresetCategory.Mainstream),
            item => Assert.StartsWith("https://", item.DocsUrl));
    }

    [Fact]
    public void FishOffersItsCurrentModelsAndTheFreeOneComesFirst()
    {
        var fish = ProviderCatalog.Find("fish")!;
        Assert.Equal(SpeechModels.FishFree, fish.DefaultModel);
        Assert.Contains(SpeechModels.FishDrama, ProviderCatalog.FishModelIds);
        Assert.Contains("s2.1-pro", ProviderCatalog.FishModelIds);
        // s1 retires on 2026-12-31 and is not offered any more.
        Assert.DoesNotContain("s1", ProviderCatalog.FishModelIds);
    }

    [Fact]
    public void OpenAiVoicesFollowTheModelTheyNeed()
    {
        var openai = ProviderCatalog.Find("openai")!;
        Assert.Contains(openai.VoicesFor("gpt-4o-mini-tts"), item => item.Id == "marin");
        Assert.DoesNotContain(openai.VoicesFor("tts-1"), item => item.Id == "marin");
        Assert.Contains(openai.VoicesFor("tts-1"), item => item.Id == "alloy");
        Assert.NotEmpty(openai.Notes);
    }

    [Fact]
    public void SiliconFlowVoicesCarryTheirModel()
    {
        var siliconflow = ProviderCatalog.Find("siliconflow")!;
        Assert.All(siliconflow.Voices, voice => Assert.StartsWith(voice.Models![0] + ":", voice.Id));
    }

    [Fact]
    public void MainstreamKeysGroupByVendorAndOthersByAddress()
    {
        var minimax = ProviderCatalog.Find("minimax-cn")!;
        var key = new SpeechProviderProfile { Kind = minimax.Kind, Endpoint = minimax.Endpoints[1].Url };
        Assert.Equal("minimax-cn", minimax.VendorFor(key));

        var kokoro = ProviderCatalog.Find("kokoro")!;
        var local = new SpeechProviderProfile { Kind = kokoro.Kind, Endpoint = kokoro.DefaultEndpoint };
        Assert.Equal("openai-compatible@http://127.0.0.1:8880", kokoro.VendorFor(local));
    }

    [Fact]
    public void OldSettingsFindTheirEntry()
    {
        Assert.Equal("fish", ProviderCatalog.For(new() { Kind = SpeechProviderKind.Fish, Endpoint = ServiceIdentity.FishOfficialEndpoint }).Id);
        Assert.Equal("fish-speech", ProviderCatalog.For(new() { Kind = SpeechProviderKind.Fish, Endpoint = "http://127.0.0.1:8080/v1/tts" }).Id);
        Assert.Equal("openai", ProviderCatalog.For(new() { Kind = SpeechProviderKind.OpenAiCompatible, Endpoint = "https://api.openai.com/v1" }).Id);
        Assert.Equal(ProviderCatalog.OpenAiCompatibleId,
            ProviderCatalog.For(new() { Kind = SpeechProviderKind.OpenAiCompatible, Endpoint = "https://gw.example.com/v1" }).Id);
        Assert.Equal("azure", ProviderCatalog.For(new() { Kind = SpeechProviderKind.AzureSpeech }).Id);
        Assert.Equal("volcengine", ProviderCatalog.For(new() { Kind = SpeechProviderKind.Doubao }).Id);
        Assert.Equal("gpt-sovits", ProviderCatalog.For(new() { Kind = SpeechProviderKind.GptSoVits }).Id);
        // A stored preset wins.
        Assert.Equal("siliconflow", ProviderCatalog.For(new()
        {
            Kind = SpeechProviderKind.OpenAiCompatible, Preset = "siliconflow", Endpoint = "https://api.siliconflow.cn/v1"
        }).Id);
    }
}
