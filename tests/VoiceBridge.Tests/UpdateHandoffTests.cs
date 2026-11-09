using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class UpdateHandoffTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("wvb-handoff-test-").FullName;

    private string Updates => Path.Combine(root, "updates");

    [Fact]
    public void FreshHandoffHasNeitherSignalAndMustNotShowTheWindow()
    {
        var handoff = UpdateHandoff.Create(Updates);
        Assert.True(UpdateHandoff.IsValidTicket(handoff.Ticket));
        Assert.False(handoff.IsReady());
        Assert.False(handoff.IsGo());
        Assert.False(handoff.ShouldShowMainWindow());
    }

    [Fact]
    public void ReadinessAloneNeverReleasesTheMainWindow()
    {
        var handoff = UpdateHandoff.Create(Updates);
        handoff.SignalReady();
        Assert.True(handoff.IsReady());
        Assert.False(handoff.IsGo());
        // This is the bug being fixed: the new build used to appear as soon as it
        // had started, without waiting for the user to dismiss the helper window.
        Assert.False(handoff.ShouldShowMainWindow());
    }

    [Fact]
    public void OnlyTheUsersDismissalReleasesTheMainWindow()
    {
        var handoff = UpdateHandoff.Create(Updates);
        handoff.SignalReady();
        Assert.False(handoff.ShouldShowMainWindow());

        handoff.SignalGo();
        Assert.True(handoff.IsGo());
        Assert.True(handoff.ShouldShowMainWindow());
    }

    [Fact]
    public void LaunchArgumentsRoundTripToTheRestartedProcess()
    {
        var helper = UpdateHandoff.Create(Updates);
        var args = new[] { "--update-result", "staging-abc", "1234", "stamp", "installed" }
            .Concat(helper.LaunchArguments).ToArray();
        Assert.Equal(7, args.Length);

        var restarted = UpdateHandoff.FromArguments(Updates, args);
        Assert.NotNull(restarted);
        Assert.Equal(helper.Ticket, restarted!.Ticket);
        Assert.False(restarted.ShouldShowMainWindow());

        restarted.SignalReady();
        Assert.True(helper.IsReady());
        helper.SignalGo();
        Assert.True(restarted.ShouldShowMainWindow());
    }

    [Fact]
    public void LegacyFiveArgumentRestartHasNoHandshakeAndShowsNormally()
    {
        var legacy = new[] { "--update-result", "staging-abc", "1234", "stamp", "installed" };
        Assert.Null(UpdateHandoff.FromArguments(Updates, legacy));
    }

    [Theory]
    [InlineData("--await-handoff")]
    [InlineData("--await-handoff", "not-a-guid")]
    [InlineData("--await-handoff", "../../escape")]
    [InlineData("--update-result", "staging-abc", "1234", "stamp", "installed")]
    [InlineData("--await-handoff", "0123456789abcdef0123456789abcde")] // 31 chars
    [InlineData("--await-handoff", "0123456789abcdef0123456789abcdef0")] // 33 chars
    [InlineData("--await-handoff", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("--await-handoff", "", "installed")]
    public void MalformedTicketsNeverCreateAHandshake(params string[] args) =>
        Assert.Null(UpdateHandoff.FromArguments(Updates, args));

    [Fact]
    public void TicketMayAppearAnywhereInTheArgumentList()
    {
        var helper = UpdateHandoff.Create(Updates);
        var args = new[] { "--update-result", "staging-abc", "1234", "stamp", "installed" }
            .Concat(helper.LaunchArguments).Append("--trailing").ToArray();
        Assert.Equal(helper.Ticket, UpdateHandoff.FromArguments(Updates, args)?.Ticket);
    }

    [Fact]
    public void TicketsAreUniqueAndCannotEscapeTheUpdatesDirectory()
    {
        var first = UpdateHandoff.Create(Updates);
        var second = UpdateHandoff.Create(Updates);
        Assert.NotEqual(first.Ticket, second.Ticket);

        first.SignalReady();
        first.SignalGo();
        Assert.False(second.IsReady());
        Assert.False(second.ShouldShowMainWindow());

        var files = Directory.GetFiles(Updates, "*", SearchOption.TopDirectoryOnly);
        Assert.Equal(2, files.Length);
        foreach (var file in files)
            Assert.Equal(Updates, Path.GetDirectoryName(file));
    }

    [Fact]
    public void CleanupRemovesOnlyThisHandshakesSignals()
    {
        var first = UpdateHandoff.Create(Updates);
        var second = UpdateHandoff.Create(Updates);
        first.SignalReady(); first.SignalGo();
        second.SignalReady(); second.SignalGo();

        first.Cleanup();
        Assert.False(first.IsReady());
        Assert.False(first.IsGo());
        Assert.True(second.IsReady());
        Assert.True(second.ShouldShowMainWindow());
    }

    [Fact]
    public void SignalsAreIdempotentAndNeverTemporaryFiles()
    {
        var handoff = UpdateHandoff.Create(Updates);
        handoff.SignalReady(); handoff.SignalReady();
        handoff.SignalGo(); handoff.SignalGo();
        Assert.True(handoff.IsReady());
        Assert.True(handoff.IsGo());
        Assert.DoesNotContain(Directory.GetFiles(Updates), file => file.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateRejectsAnEmptyUpdatesDirectory() =>
        Assert.Throws<ArgumentException>(() => UpdateHandoff.Create("  "));

    public void Dispose() => Directory.Delete(root, recursive: true);
}
