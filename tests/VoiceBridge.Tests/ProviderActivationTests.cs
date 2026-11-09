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

        Assert.Equal(SpeechProviderKind.Fish, VoiceLibraryMigration.VendorOf(voice, []));
        Assert.Null(VoiceLibraryMigration.VendorOf(voice with { ProviderKind = null }, []));
    }

    [Fact]
    public void CacheVendorFillsInTheVendorWhileItsConnectionStillExists()
    {
        var live = Voice("v1", "service-1");
        var alreadyKnown = Voice("v2", "gone", SpeechProviderKind.Doubao);

        var cached = VoiceLibraryMigration.CacheVendor(
            [live, alreadyKnown], [Key("service-1", SpeechProviderKind.Doubao)], out var count);

        Assert.Equal(1, count);
        Assert.Equal(SpeechProviderKind.Doubao, cached[0].ProviderKind);
        Assert.Equal("voice-1", cached[0].VoiceId);
        Assert.Equal(alreadyKnown, cached[1]);
    }

    [Fact]
    public void LegacyJsonHasNoVendorButTheValueSurvivesARoundTrip()
    {
        const string json = """{"Id":"v1","Name":"旧音色","ProviderId":"live","VoiceId":"voice-1"}""";
        var legacy = JsonSerializer.Deserialize<VoiceProfile>(json)!;
        Assert.Null(legacy.ProviderKind);

        var cached = VoiceLibraryMigration.CacheVendor(
            [legacy], [Key("live", SpeechProviderKind.Fish)], out var count);
        Assert.Equal(1, count);

        var reloaded = JsonSerializer.Deserialize<VoiceProfile>(JsonSerializer.Serialize(cached[0]))!;
        Assert.Equal(SpeechProviderKind.Fish, VoiceLibraryMigration.VendorOf(reloaded, []));
        Assert.Equal("voice-1", reloaded.VoiceId);
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
}
