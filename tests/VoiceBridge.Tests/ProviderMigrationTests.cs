using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class ProviderMigrationTests
{
    private static SpeechProviderProfile User(string id, string credential = "cred-user") => new()
    {
        Id = id,
        Name = "我的语音服务",
        Kind = SpeechProviderKind.Fish,
        Endpoint = "https://api.fish.audio/v1/tts",
        Model = "s2.1-pro-free",
        CredentialId = credential,
        Latency = "low",
        TimeoutSeconds = 77,
        AudioFormat = "wav"
    };

    private static SpeechProviderProfile Factory() => new()
    {
        Id = ProviderMigration.FactoryDefaultId,
        Name = "Fish Audio",
        Kind = SpeechProviderKind.Fish,
        Endpoint = "https://api.fish.audio/v1/tts",
        CredentialId = "speech-fish-default"
    };

    [Fact]
    public void FreshInstallHasNoProvidersAtAll()
    {
        Assert.Empty(ProviderMigration.RemoveFactoryDefault(null, out var nullRemoved));
        Assert.False(nullRemoved);
        Assert.Empty(ProviderMigration.RemoveFactoryDefault([], out var emptyRemoved));
        Assert.False(emptyRemoved);
    }

    [Fact]
    public void OnlyEntryIsTheFactoryDefault_SoUpgradeEndsWithAnEmptyList()
    {
        var result = ProviderMigration.RemoveFactoryDefault([Factory()], out var removed);
        Assert.True(removed);
        Assert.Empty(result);
    }

    [Fact]
    public void UserAddedServicesSurviveVerbatimAndInOrder()
    {
        var first = User("guid-aaaa");
        var second = User("guid-bbbb", "cred-other") with { Name = "本地服务" };
        var result = ProviderMigration.RemoveFactoryDefault([Factory(), first, second, Factory()], out var removed);

        Assert.True(removed);
        Assert.Equal(2, result.Count);
        Assert.Same(first, result[0]);
        Assert.Same(second, result[1]);
        // The credential reference (the user's own key in the encrypted store)
        // must never be rewritten or dropped by the migration.
        Assert.Equal("cred-user", result[0].CredentialId);
        Assert.Equal("cred-other", result[1].CredentialId);
        Assert.Equal("low", result[0].Latency);
        Assert.Equal(77, result[0].TimeoutSeconds);
        Assert.Equal("wav", result[0].AudioFormat);
    }

    [Fact]
    public void MigrationIsIdempotent()
    {
        var own = User("guid-cccc");
        var once = ProviderMigration.RemoveFactoryDefault([Factory(), own], out var firstRemoved);
        Assert.True(firstRemoved);
        var twice = ProviderMigration.RemoveFactoryDefault(once, out var secondRemoved);
        Assert.False(secondRemoved);
        Assert.Same(own, Assert.Single(twice));
    }

    [Theory]
    [InlineData("fish-default-1")]
    [InlineData("FISH-DEFAULT")]
    [InlineData("fish_default")]
    [InlineData(" my-fish-default ")]
    [InlineData("guid-0000")]
    public void SimilarLookingIdsAreNeverMistakenForTheFactoryEntry(string id)
    {
        var own = User(id);
        var result = ProviderMigration.RemoveFactoryDefault([own], out var removed);
        Assert.False(removed);
        Assert.Same(own, Assert.Single(result));
    }

    [Fact]
    public void BlankAndDuplicateIdsAreCleanedWithoutTouchingTheFirstWinner()
    {
        var own = User("guid-dddd");
        var result = ProviderMigration.RemoveFactoryDefault(
            [new(), own, User("guid-dddd", "cred-duplicate"), User(" ")], out var removed);
        Assert.False(removed);
        Assert.Same(own, Assert.Single(result));
    }

    [Fact]
    public void FactoryIdIsMatchedExactly()
    {
        Assert.True(ProviderMigration.IsFactoryDefault("fish-default"));
        Assert.False(ProviderMigration.IsFactoryDefault("Fish-Default"));
        Assert.False(ProviderMigration.IsFactoryDefault("fish-default "));
        Assert.False(ProviderMigration.IsFactoryDefault(null));
        Assert.False(ProviderMigration.IsFactoryDefault(""));
    }

    /// <summary>
    /// Replays an upgraded settings.json: one factory connection plus a service the
    /// user added with their own credential. Deserialization + migration must leave
    /// exactly the user's service, with the key reference intact.
    /// </summary>
    [Fact]
    public void DeserializedLegacySettingsLoseOnlyTheFactoryConnection()
    {
        const string legacy = """
            [
              {"Id":"fish-default","Name":"Fish Audio","Kind":0,
               "Endpoint":"https://api.fish.audio/v1/tts","Model":"s2.1-pro-free",
               "CredentialId":"speech-fish-default","Enabled":true,"Latency":"balanced",
               "TimeoutSeconds":45,"AudioFormat":"mp3"},
              {"Id":"9a1c2f0e5b6d4c3f8e7a0b1d2c3e4f5a","Name":"我自己的 Fish","Kind":0,
               "Endpoint":"https://api.fish.audio/v1/tts","Model":"drama-3",
               "CredentialId":"speech-9a1c2f0e","Enabled":true,"Latency":"low",
               "TimeoutSeconds":60,"AudioFormat":"wav"}
            ]
            """;
        var stored = JsonSerializer.Deserialize<List<SpeechProviderProfile>>(legacy)!;
        var migrated = ProviderMigration.RemoveFactoryDefault(stored, out var removed);

        Assert.True(removed);
        var own = Assert.Single(migrated);
        Assert.Equal("9a1c2f0e5b6d4c3f8e7a0b1d2c3e4f5a", own.Id);
        Assert.Equal("我自己的 Fish", own.Name);
        Assert.Equal("speech-9a1c2f0e", own.CredentialId);
        Assert.Equal("drama-3", own.Model);
        Assert.Equal("wav", own.AudioFormat);
    }
}
