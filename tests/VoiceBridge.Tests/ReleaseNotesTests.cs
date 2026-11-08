using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class ReleaseNotesTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("wvb-release-notes-").FullName;

    [Fact]
    public void FreshInstallationSetsBaselineAndTheNextVersionShowsNotes()
    {
        using var state = ReleaseNotesState.TryAcquire(directory);
        Assert.NotNull(state);
        Assert.False(state.ShouldShow("1.3.2", existingUser: false));
        Assert.False(state.ShouldShow("1.3.2", existingUser: true));
        Assert.True(state.ShouldShow("1.3.3", existingUser: true));
    }

    [Fact]
    public void LegacyUpgradeShowsOnceAndPersistsAcrossProcessLifetimes()
    {
        using (var state = ReleaseNotesState.TryAcquire(directory))
        {
            Assert.NotNull(state);
            Assert.True(state.ShouldShow("1.3.2", existingUser: true));
            state.Acknowledge("1.3.2");
        }
        using var reopened = ReleaseNotesState.TryAcquire(directory);
        Assert.NotNull(reopened);
        Assert.False(reopened.ShouldShow("1.3.2", existingUser: true));
        Assert.False(reopened.ShouldShow("1.3.1", existingUser: true));
        Assert.True(reopened.ShouldShow("1.3.3", existingUser: true));
    }

    [Fact]
    public void UnacknowledgedNotesAreShownAgainAndParallelInstancesCannotShowTwice()
    {
        using (var first = ReleaseNotesState.TryAcquire(directory))
        {
            Assert.NotNull(first);
            Assert.True(first.ShouldShow("1.3.2", existingUser: true));
            Assert.Null(ReleaseNotesState.TryAcquire(directory));
        }
        using var next = ReleaseNotesState.TryAcquire(directory);
        Assert.NotNull(next);
        Assert.True(next.ShouldShow("1.3.2", existingUser: true));
    }

    [Fact]
    public void CorruptRecordRecoversAndOtherSettingsRemainByteIdentical()
    {
        var settings = Path.Combine(directory, "settings.json");
        File.WriteAllText(settings, "settings sentinel");
        var before = File.ReadAllBytes(settings);
        File.WriteAllText(Path.Combine(directory, "release-notes-seen.json"), "{invalid");
        using var state = ReleaseNotesState.TryAcquire(directory);
        Assert.NotNull(state);
        Assert.True(state.ShouldShow("1.3.2", existingUser: true));
        state.Acknowledge("1.3.2");
        Assert.Equal(before, File.ReadAllBytes(settings));
        Assert.Equal("1.3.2", JsonSerializer.Deserialize<string>(
            File.ReadAllText(Path.Combine(directory, "release-notes-seen.json"))));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
