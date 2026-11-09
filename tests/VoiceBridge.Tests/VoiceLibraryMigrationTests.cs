using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class VoiceLibraryMigrationTests
{
    private const string FactoryId = ProviderMigration.FactoryDefaultId;

    private static VoiceProfile FactoryVoice(string voiceId, string name) => new()
    {
        Id = VoiceLibraryMigration.FactoryVoicePrefix + voiceId,
        Name = name,
        ProviderId = FactoryId,
        VoiceId = voiceId,
        Language = "zh-CN"
    };

    private static VoiceProfile UserVoice(string id = "9f0c2a5b8e7d4c6f8a1b3d5e7c9a0b24") => new()
    {
        Id = id,
        Name = "我自己加的音色",
        ProviderId = "service-user-1",
        VoiceId = "voice-user-1",
        Language = "en-US",
        Speed = 1.25,
        Instructions = "speak softly"
    };

    private static SpeechProviderProfile Service(string id, string name = "我的 API") => new()
    {
        Id = id,
        Name = name,
        Kind = SpeechProviderKind.Fish,
        Endpoint = "https://api.fish.audio/v1/tts",
        Model = "s2.1-pro-free",
        CredentialId = "cred-user"
    };

    private static ContactProfile Contact(string voiceId, string conversation = "wxid-1") => new()
    {
        AccountId = "account-1",
        ConversationId = conversation,
        Name = "朋友",
        VoiceProfileId = voiceId,
        Selected = true,
        CustomAnnouncement = "开头提示"
    };

    [Fact]
    public void FreshInstallHasNoVoicesAtAll()
    {
        Assert.Empty(VoiceLibraryMigration.RemoveFactoryVoices(null, out var nullRemoved));
        Assert.Empty(nullRemoved);
        Assert.Empty(VoiceLibraryMigration.RemoveFactoryVoices([], out var emptyRemoved));
        Assert.Empty(emptyRemoved);
    }

    [Fact]
    public void AllSeededVoicesAreRemovedFromAnUpgradedLibrary()
    {
        var legacy = new[]
        {
            FactoryVoice("bf8f5cb1501d4e5a950d26738db7f772", "聂聂"),
            FactoryVoice("46deb5ee78c04a2a894073f993e8211e", "里奥"),
            FactoryVoice("2d1ad251fc944d5bb473318468544c60", "熊二"),
            FactoryVoice("05e50c98f48845999d2d9d101d95958c", "派大星"),
            FactoryVoice("an-older-choice", "此前使用的音色")
        };

        var kept = VoiceLibraryMigration.RemoveFactoryVoices(legacy, out var removed);

        Assert.Empty(kept);
        Assert.Equal(5, removed.Count);
        Assert.Equal(legacy.Select(voice => voice.Id), removed);
    }

    [Fact]
    public void UserAddedVoiceSurvivesVerbatimAndInOrder()
    {
        var first = UserVoice();
        var second = UserVoice("11111111111111111111111111111111") with { Name = "第二个" };
        var kept = VoiceLibraryMigration.RemoveFactoryVoices(
            [FactoryVoice("preset", "聂聂"), first, second], out var removed);

        Assert.Single(removed);
        Assert.Equal(2, kept.Count);
        Assert.Equal(first, kept[0]);
        Assert.Equal(second, kept[1]);
        Assert.Equal("service-user-1", kept[0].ProviderId);
        Assert.Equal("speak softly", kept[0].Instructions);
        Assert.Equal(1.25, kept[0].Speed);
    }

    [Fact]
    public void AFactoryVoiceTheUserRePointedIsNotDeleted()
    {
        var rePointed = FactoryVoice("preset", "改绑过的音色") with { ProviderId = "service-user-1" };

        var kept = VoiceLibraryMigration.RemoveFactoryVoices([rePointed], out var removed);

        Assert.Empty(removed);
        Assert.Equal(rePointed, Assert.Single(kept));
    }

    [Theory]
    [InlineData("service-user-1")]
    [InlineData("FISH-DEFAULT")]
    [InlineData("fish-default-1")]
    [InlineData("")]
    public void SimilarLookingProviderIdsAreNeverMistakenForTheFactoryOne(string providerId)
    {
        var voice = FactoryVoice("preset", "聂聂") with { ProviderId = providerId };

        var kept = VoiceLibraryMigration.RemoveFactoryVoices([voice], out var removed);

        Assert.Empty(removed);
        Assert.Equal(voice, Assert.Single(kept));
    }

    [Fact]
    public void BlankAndDuplicateIdsAreCleanedWithoutLosingUserVoices()
    {
        var user = UserVoice();
        var kept = VoiceLibraryMigration.RemoveFactoryVoices(
            [new VoiceProfile { Id = " ", Name = "空" }, user, user], out var removed);

        Assert.Empty(removed);
        Assert.Equal(user, Assert.Single(kept));
    }

    [Fact]
    public void MigrationIsIdempotent()
    {
        var once = VoiceLibraryMigration.RemoveFactoryVoices(
            [FactoryVoice("preset", "聂聂"), UserVoice()], out var firstRemoved);
        Assert.Single(firstRemoved);

        var twice = VoiceLibraryMigration.RemoveFactoryVoices(once, out var secondRemoved);
        Assert.Empty(secondRemoved);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void ContactsAndTestSelectionNoLongerPointAtRemovedVoices()
    {
        var gone = FactoryVoice("preset", "聂聂");
        var user = UserVoice();
        VoiceLibraryMigration.RemoveFactoryVoices([gone, user], out var removed);

        var contacts = VoiceLibraryMigration.CleanContactReferences(
            [Contact(gone.Id), Contact(user.Id, "wxid-2"), Contact("", "wxid-3")], removed);

        Assert.Equal("", contacts[0].VoiceProfileId);
        Assert.Equal(user.Id, contacts[1].VoiceProfileId);
        Assert.Equal("", contacts[2].VoiceProfileId);
        Assert.Equal("开头提示", contacts[0].CustomAnnouncement);
        Assert.True(contacts[0].Selected);
        Assert.Equal("", VoiceLibraryMigration.CleanTestVoiceReference(gone.Id, removed));
        Assert.Equal(user.Id, VoiceLibraryMigration.CleanTestVoiceReference(user.Id, removed));
        Assert.Equal("", VoiceLibraryMigration.CleanTestVoiceReference(null, removed));
    }

    [Fact]
    public void DeserializedLegacySettingsLoseOnlyFactoryVoices()
    {
        const string json = """
            {"VoiceLibrary":[
              {"Id":"fish-bf8f5cb1501d4e5a950d26738db7f772","Name":"聂聂","ProviderId":"fish-default","VoiceId":"bf8f5cb1501d4e5a950d26738db7f772"},
              {"Id":"9f0c2a5b8e7d4c6f8a1b3d5e7c9a0b24","Name":"我自己加的音色","ProviderId":"service-user-1","VoiceId":"voice-user-1"}
            ]}
            """;
        var parsed = JsonSerializer.Deserialize<LegacyLibrary>(json)!;

        var kept = VoiceLibraryMigration.RemoveFactoryVoices(parsed.VoiceLibrary, out var removed);

        Assert.Single(removed);
        Assert.Equal("我自己加的音色", Assert.Single(kept).Name);
    }

    [Fact]
    public void AddingTheFirstApiServiceMakesOrphanVoicesUsable()
    {
        var orphanByFactory = FactoryVoice("preset", "聂聂");
        var orphanByDeletedService = UserVoice("22222222222222222222222222222222") with { ProviderId = "service-deleted" };
        var orphanWithNoService = UserVoice("33333333333333333333333333333333") with { ProviderId = "" };
        var alreadyBound = UserVoice("44444444444444444444444444444444") with { ProviderId = "service-live" };
        var live = Service("service-live", "旧 API");
        var added = Service("service-new", "新 API");
        var services = new[] { live, added };

        var bound = VoiceLibraryMigration.BindOrphanVoices(
            [orphanByFactory, orphanByDeletedService, orphanWithNoService, alreadyBound],
            services, added, out var count);

        Assert.Equal(3, count);
        Assert.Equal(added.Id, bound[0].ProviderId);
        Assert.Equal(added.Id, bound[1].ProviderId);
        Assert.Equal(added.Id, bound[2].ProviderId);
        Assert.Equal(live.Id, bound[3].ProviderId);
        // The library resolves a provider name for each voice; none is "unbound" now.
        Assert.All(bound, voice => Assert.Contains(services, item => item.Id == voice.ProviderId));
        Assert.Equal("聂聂", bound[0].Name);
        Assert.Equal(1.25, bound[1].Speed);
    }

    [Fact]
    public void BindingOrphansIsIdempotentAndKeepsOtherFields()
    {
        var orphan = UserVoice() with { ProviderId = "service-deleted" };
        var added = Service("service-new");
        var first = VoiceLibraryMigration.BindOrphanVoices([orphan], [added], added, out var firstCount);
        var second = VoiceLibraryMigration.BindOrphanVoices(first, [added], added, out var secondCount);

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
        Assert.Equal(first, second);
        Assert.Equal("我自己加的音色", second[0].Name);
        Assert.Equal("voice-user-1", second[0].VoiceId);
        Assert.Equal("speak softly", second[0].Instructions);
    }

    private sealed record LegacyLibrary(List<VoiceProfile> VoiceLibrary);
}
