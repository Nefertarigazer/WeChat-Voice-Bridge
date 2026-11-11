using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class ProviderActivationTests
{
    private static SpeechProviderProfile Key(string id,
        SpeechProviderKind kind = SpeechProviderKind.Fish, bool enabled = true, string name = "") => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        Enabled = enabled,
        Endpoint = "https://api.fish.audio/v1/tts",
        Model = "s2.1-pro-free",
        CredentialId = "cred-" + id
    };

    private static VoiceProfile Voice(string id, string providerId,
        SpeechProviderKind? vendor = null, string voiceId = "voice-1") => new()
    {
        Id = id,
        Name = "音色 " + id,
        ProviderId = providerId,
        ProviderKind = vendor,
        VoiceId = voiceId,
        Language = "zh-CN"
    };

    // Mirrors the UI rule: adding a key never applies it when the vendor already
    // has an active one.
    private static SpeechProviderProfile Add(
        IEnumerable<SpeechProviderProfile> existing, SpeechProviderProfile candidate) =>
        ProviderActivation.StaysOnStandby(existing, candidate)
            ? candidate with { Enabled = false } : candidate;

    [Fact]
    public void TheFirstKeyOfAVendorStartsActive()
    {
        var first = Add([], Key("fish-1"));

        Assert.True(first.Enabled);
        Assert.False(ProviderActivation.NeedsActivation([first], first));
    }

    [Fact]
    public void AddingASecondKeyDoesNotApplyIt()
    {
        var first = Key("fish-1");
        var second = Add([first], Key("fish-2"));

        Assert.False(second.Enabled);
        Assert.True(first.Enabled);
        Assert.True(ProviderActivation.NeedsActivation([first, second], first));
    }

    [Fact]
    public void AddingAThirdKeyKeepsWhicheverOneWasAlreadyActive()
    {
        var first = Key("fish-1");
        var second = Add([first], Key("fish-2"));
        var active = ProviderActivation.Activate([first, second], "fish-2");
        Assert.Equal("fish-2", Assert.Single(active, item => item.Enabled).Id);

        var third = Add(active, Key("fish-3"));

        Assert.False(third.Enabled);
        Assert.Equal("fish-2", Assert.Single(active, item => item.Enabled).Id);
    }

    [Fact]
    public void DifferentVendorsEachKeepTheirOwnActiveKey()
    {
        var fish = Key("fish-1", SpeechProviderKind.Fish);
        var doubao = Key("db-1", SpeechProviderKind.Doubao);

        // Adding a doubao key is not blocked by an active Fish key.
        Assert.False(ProviderActivation.StaysOnStandby([fish], doubao));
        Assert.False(ProviderActivation.NeedsActivation([fish, doubao], fish));
        Assert.False(ProviderActivation.NeedsActivation([fish, doubao], doubao));

        var fish2 = Key("fish-2", SpeechProviderKind.Fish);
        var all = new[] { fish, fish2, doubao };
        Assert.True(ProviderActivation.NeedsActivation(all, fish));
        Assert.False(ProviderActivation.NeedsActivation(all, doubao));

        var switched = ProviderActivation.Activate(all, "fish-2");
        Assert.False(switched.Single(item => item.Id == "fish-1").Enabled);
        Assert.True(switched.Single(item => item.Id == "fish-2").Enabled);
        Assert.Same(doubao, switched.Single(item => item.Id == "db-1"));
        Assert.True(switched.Single(item => item.Id == "db-1").Enabled);
    }

    [Fact]
    public void ActivatingIsIdempotentAndIgnoresVendorsWithOneKey()
    {
        var fish1 = Key("fish-1");
        var fish2 = Key("fish-2", enabled: false);
        var single = Key("db-1", SpeechProviderKind.Doubao);

        var once = ProviderActivation.Activate([fish1, fish2, single], "fish-2");
        var twice = ProviderActivation.Activate(once, "fish-2");

        Assert.Equal(once, twice);
        Assert.Same(single, ProviderActivation.Activate([single], single.Id).Single());
    }

    [Fact]
    public void AnUnknownIdNeverChangesAnything()
    {
        var all = new[] { Key("fish-1"), Key("fish-2", enabled: false) };

        Assert.Equal(all, ProviderActivation.Activate(all, "does-not-exist"));
    }

    [Fact]
    public void DeletingAConnectionDoesNotEraseTheVendorName()
    {
        var voice = Voice("v1", "deleted-service", SpeechProviderKind.Fish);

        Assert.Equal(ServiceIdentity.Fish, VoiceLibraryMigration.VendorOf(voice, []));
        Assert.Null(VoiceLibraryMigration.VendorOf(voice with { ProviderKind = null }, []));
    }

    [Fact]
    public void CacheVendorFillsInTheVendorWhileItsConnectionStillExists()
    {
        var live = Voice("v1", "service-1");
        var alreadyKnown = Voice("v2", "gone", SpeechProviderKind.Doubao) with { Vendor = ServiceIdentity.Volcengine };

        var cached = VoiceLibraryMigration.CacheVendor(
            [live, alreadyKnown], [Key("service-1", SpeechProviderKind.Doubao)], out var count);

        Assert.Equal(1, count);
        Assert.Equal(SpeechProviderKind.Doubao, cached[0].ProviderKind);
        Assert.Equal(ServiceIdentity.Volcengine, cached[0].Vendor);
        Assert.Equal("voice-1", cached[0].VoiceId);
        Assert.Equal(alreadyKnown, cached[1]);
    }

    [Fact]
    public void AnOrphanFromAnOlderBuildGetsTheVendorItsProtocolImplied()
    {
        var orphan = Voice("v1", "gone", SpeechProviderKind.Doubao);

        var cached = VoiceLibraryMigration.CacheVendor([orphan], [], out var count);

        Assert.Equal(1, count);
        Assert.Equal(ServiceIdentity.Volcengine, cached[0].Vendor);
    }

    [Fact]
    public void LegacyJsonHasNoVendorButTheValueSurvivesARoundTrip()
    {
        const string json = """{"Id":"v1","Name":"旧音色","ProviderId":"live","VoiceId":"voice-1"}""";
        var legacy = JsonSerializer.Deserialize<VoiceProfile>(json)!;
        Assert.Null(legacy.ProviderKind);
        Assert.Equal("", legacy.Vendor);
        Assert.Equal("", legacy.Model);

        var cached = VoiceLibraryMigration.CacheVendor(
            [legacy], [Key("live", SpeechProviderKind.Fish)], out var count);
        Assert.Equal(1, count);

        var reloaded = JsonSerializer.Deserialize<VoiceProfile>(JsonSerializer.Serialize(cached[0]))!;
        Assert.Equal(ServiceIdentity.Fish, VoiceLibraryMigration.VendorOf(reloaded, []));
        Assert.Equal("voice-1", reloaded.VoiceId);
    }

    // ---- A vendor is a service, not a protocol ----

    private static SpeechProviderProfile Compatible(string id, string endpoint, bool enabled = true) => new()
    {
        Id = id, Kind = SpeechProviderKind.OpenAiCompatible, Endpoint = endpoint, Model = "tts",
        Enabled = enabled, CredentialId = "cred-" + id
    };

    [Fact]
    public void TwoOpenAiCompatibleServicesAreTwoVendors()
    {
        var siliconflow = Compatible("sf", "https://api.siliconflow.cn/v1");
        var kokoro = Compatible("kokoro", "http://127.0.0.1:8880/v1");

        Assert.False(ServiceIdentity.Same(siliconflow, kokoro));
        // Each keeps its own active key: adding Kokoro is not blocked by SiliconFlow.
        Assert.False(ProviderActivation.StaysOnStandby([siliconflow], kokoro));
        Assert.False(ProviderActivation.NeedsActivation([siliconflow, kokoro], kokoro));

        // A SiliconFlow voice is never run by the Kokoro key.
        var voice = Voice("v1", "sf") with { Vendor = ServiceIdentity.Of(siliconflow) };
        Assert.Null(VoiceLibraryMigration.ActiveKeyFor(voice, [kokoro]));
        Assert.Equal("sf", VoiceLibraryMigration.ActiveKeyFor(voice, [kokoro, siliconflow])!.Id);
    }

    [Fact]
    public void TwoKeysForTheSameAddressAreTwoKeysOfOneService()
    {
        var first = Compatible("a", "https://api.siliconflow.cn/v1");
        var second = Compatible("b", "https://API.siliconflow.cn/v1/audio/speech/");

        Assert.True(ServiceIdentity.Same(first, second));
        Assert.True(ProviderActivation.StaysOnStandby([first], second));
        var switched = ProviderActivation.Activate([first, second with { Enabled = false }], "b");
        Assert.Equal("b", Assert.Single(switched, item => item.Enabled).Id);
    }

    [Fact]
    public void AStoredVendorWinsOverTheAddress()
    {
        // A mainstream preset groups every key of the vendor, whatever region it calls.
        var china = Compatible("cn", "https://api.minimax.cn/v1") with { Vendor = "minimax-cn" };
        var beijing = Compatible("bj", "https://api-bj.minimaxi.com/v1") with { Vendor = "minimax-cn" };

        Assert.True(ServiceIdentity.Same(china, beijing));
        Assert.True(ProviderActivation.NeedsActivation([china, beijing], china));
    }

    [Fact]
    public void OldSettingsKeepTheirBuiltInVendorsAndSplitCompatibleServices()
    {
        var official = Compatible("o", "https://api.openai.com/v1/audio/speech");
        var blank = Compatible("blank", "");
        var gateway = Compatible("g", "https://gateway.example.com/v1");

        var cached = ServiceIdentity.CacheVendor([Key("fish-1"), official, blank, gateway,
            Key("db", SpeechProviderKind.Doubao), Key("az", SpeechProviderKind.AzureSpeech)], out var filled);

        Assert.Equal(6, filled);
        Assert.Equal(new[] { ServiceIdentity.Fish, ServiceIdentity.OpenAi, ServiceIdentity.OpenAi,
                "openai-compatible@https://gateway.example.com", ServiceIdentity.Volcengine, ServiceIdentity.Azure },
            cached.Select(item => item.Vendor));
        // A second pass changes nothing.
        ServiceIdentity.CacheVendor(cached, out var again);
        Assert.Equal(0, again);
    }

    [Fact]
    public void AModelPickedOnTheHomePageAppliesOnlyWhenTheServiceListsIt()
    {
        var provider = Compatible("sf", "https://api.siliconflow.cn/v1") with
        {
            Model = "FunAudioLLM/CosyVoice2-0.5B", AvailableModels = ["fishaudio/fish-speech-1.5"]
        };
        var listed = new ContactProfile { ModelOverride = "fishaudio/fish-speech-1.5" };
        var stale = new ContactProfile { ModelOverride = "removed-model" };

        Assert.Equal("fishaudio/fish-speech-1.5", listed.ApplySpeechPreferences(provider).Model);
        Assert.Equal("FunAudioLLM/CosyVoice2-0.5B", stale.ApplySpeechPreferences(provider).Model);
    }

    [Fact]
    public void VolcengineSwitchesItsModelThroughTheResourceId()
    {
        var doubao = Key("db", SpeechProviderKind.Doubao) with
        {
            ResourceId = "seed-tts-2.0", AvailableModels = ["seed-tts-1.0"], Model = ""
        };

        Assert.Equal(new[] { "seed-tts-2.0", "seed-tts-1.0" }, SpeechModels.Listed(doubao));
        var picked = new ContactProfile { ModelOverride = "seed-tts-1.0" }.ApplySpeechPreferences(doubao);
        Assert.Equal("seed-tts-1.0", picked.ResourceId);
        Assert.Equal("", picked.Model);
        Assert.Equal("seed-icl-2.0", SpeechModels.Use(doubao, "seed-icl-2.0").ResourceId);
        Assert.Same(doubao, SpeechModels.Use(doubao, ""));
    }

    [Fact]
    public void ActivatingAKeyMovesOnlyThatVendorsVoices()
    {
        var fishVoice = Voice("v1", "fish-old", SpeechProviderKind.Fish);
        var doubaoVoice = Voice("v2", "db-1", SpeechProviderKind.Doubao);
        var target = Key("fish-new", SpeechProviderKind.Fish);

        var rebound = VoiceLibraryMigration.UseActiveKey(
            [fishVoice, doubaoVoice], [Key("db-1", SpeechProviderKind.Doubao), target],
            target, out var count);

        Assert.Equal(1, count);
        Assert.Equal("fish-new", rebound[0].ProviderId);
        Assert.Equal(SpeechProviderKind.Fish, rebound[0].ProviderKind);
        Assert.Equal(ServiceIdentity.Fish, rebound[0].Vendor);
        Assert.Equal(doubaoVoice, rebound[1]);
    }

    [Fact]
    public void FollowingTheActiveKeyIsIdempotent()
    {
        var voice = Voice("v1", "fish-old", SpeechProviderKind.Fish);
        var target = Key("fish-new", SpeechProviderKind.Fish);

        var once = VoiceLibraryMigration.UseActiveKey([voice], [target], target, out var first);
        var twice = VoiceLibraryMigration.UseActiveKey(once, [target], target, out var second);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(once, twice);
        Assert.Equal("音色 v1", twice[0].Name);
    }

    [Fact]
    public void OrphanVoicesNeverGetRePointedToAnotherVendor()
    {
        var fishOrphan = Voice("v1", "gone-fish", SpeechProviderKind.Fish);
        var doubaoOrphan = Voice("v2", "gone-db", SpeechProviderKind.Doubao);
        var legacy = Voice("v3", ""); // older settings carry no vendor at all
        var target = Key("fish-new", SpeechProviderKind.Fish);

        var bound = VoiceLibraryMigration.BindOrphanVoices(
            [fishOrphan, doubaoOrphan, legacy], [target], target, out var count);

        Assert.Equal(2, count);
        Assert.Equal("fish-new", bound[0].ProviderId);
        Assert.Equal("gone-db", bound[1].ProviderId);
        Assert.Equal("fish-new", bound[2].ProviderId);
    }

    // ---- Voice availability: only losing every key of a vendor matters ----

    [Fact]
    public void AVoiceStaysUsableWhileAnyKeyOfItsVendorSurvives()
    {
        var first = Key("fish-1");
        var second = Key("fish-2", enabled: false);
        var voice = Voice("v1", "fish-1", SpeechProviderKind.Fish);

        // Two keys, only one active: still usable, and the active key is chosen.
        Assert.True(VoiceLibraryMigration.IsUsable(voice, [first, second]));
        Assert.Equal("fish-1", VoiceLibraryMigration.ActiveKeyFor(voice, [first, second])!.Id);

        // The voice's own key is deleted; the other key of the vendor covers it.
        Assert.True(VoiceLibraryMigration.IsUsable(voice, [second]));
        Assert.Equal("fish-2", VoiceLibraryMigration.ActiveKeyFor(voice, [second])!.Id);

        // Every key of the vendor is gone: only now is the voice unavailable.
        Assert.False(VoiceLibraryMigration.IsUsable(voice, [Key("db-1", SpeechProviderKind.Doubao)]));
        Assert.Null(VoiceLibraryMigration.ActiveKeyFor(voice, []));
    }

    [Fact]
    public void SwitchingTheActiveKeyNeverInvalidatesAVoice()
    {
        var first = Key("fish-1");
        var second = Key("fish-2", enabled: false);
        var voice = Voice("v1", "fish-1", SpeechProviderKind.Fish);

        var switched = ProviderActivation.Activate([first, second], "fish-2");

        Assert.True(VoiceLibraryMigration.IsUsable(voice, switched));
        Assert.Equal("fish-2", VoiceLibraryMigration.ActiveKeyFor(voice, switched)!.Id);
    }

    [Fact]
    public void AddingASecondKeyOfTheSameVendorKeepsTheSelectionUsable()
    {
        var first = Key("fish-1");
        var voice = Voice("v1", "fish-1", SpeechProviderKind.Fish);
        var second = Add([first], Key("fish-2")); // stays on standby

        Assert.True(VoiceLibraryMigration.IsUsable(voice, [first, second]));
        Assert.Equal("fish-1", VoiceLibraryMigration.ActiveKeyFor(voice, [first, second])!.Id);
    }

    [Fact]
    public void AVoiceOfAnotherVendorIsNeverRunByAnUnrelatedKey()
    {
        var doubao = Key("db-1", SpeechProviderKind.Doubao);
        var fishVoice = Voice("v1", "gone-fish", SpeechProviderKind.Fish);

        Assert.False(VoiceLibraryMigration.IsUsable(fishVoice, [doubao]));
        Assert.Null(VoiceLibraryMigration.ActiveKeyFor(fishVoice, [doubao]));
    }

    // ---- Settings load must never rewrite the service -> credential link ----

    [Fact]
    public void LoadingSettingsKeepsEveryServicePointingAtItsOwnCredential()
    {
        var fish = Key("fish-1") with { CredentialId = "speech-kept" };
        var doubao = Key("db-1", SpeechProviderKind.Doubao) with { CredentialId = "speech-other" };
        var factory = Key("fish-default") with { CredentialId = "speech-shell" };

        // The exact chain LocalSettings.Load() runs at startup.
        var providers = ProviderMigration.RemoveFactoryDefault(
            [fish, doubao, factory], out var removedFactory);
        var voices = VoiceLibraryMigration.CacheVendor([Voice("v1", "fish-1")], providers, out var cached);
        _ = VoiceLibraryMigration.CleanContactReferences(
            [new ContactProfile { AccountId = "a", ConversationId = "c", VoiceProfileId = "v1" }],
            ["gone"]);

        Assert.True(removedFactory);
        Assert.Equal(1, cached);
        // No step of the migration may substitute a generated or placeholder name.
        Assert.Equal("speech-kept", providers.Single(item => item.Id == "fish-1").CredentialId);
        Assert.Equal("speech-other", providers.Single(item => item.Id == "db-1").CredentialId);
        Assert.DoesNotContain(providers, item => item.CredentialId == "speech-shell");
    }

    [Fact]
    public void SwitchingAndAddingKeysLeavesTheOtherConnectionsCredentialsAlone()
    {
        var first = Key("fish-1") with { CredentialId = "speech-kept" };
        var second = Key("fish-2", enabled: false) with { CredentialId = "speech-second" };
        var doubao = Key("db-1", SpeechProviderKind.Doubao) with { CredentialId = "speech-other" };
        var all = new[] { first, second, doubao };

        var switched = ProviderActivation.Activate(all, "fish-2");

        Assert.Equal("speech-kept", switched.Single(item => item.Id == "fish-1").CredentialId);
        Assert.Equal("speech-second", switched.Single(item => item.Id == "fish-2").CredentialId);
        Assert.Equal("speech-other", switched.Single(item => item.Id == "db-1").CredentialId);
    }
}
