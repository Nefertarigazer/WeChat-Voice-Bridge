using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class CredentialSweepTests
{
    private static SpeechProviderProfile Service(string id, string credentialId, bool enabled = true) => new()
    {
        Id = id,
        Name = "服务 " + id,
        Kind = SpeechProviderKind.Fish,
        Enabled = enabled,
        Endpoint = "https://api.fish.audio/v1/tts",
        Model = "s2.1-pro-free",
        CredentialId = credentialId
    };

    [Fact]
    public void EveryServicePointerFishKeyNameAndOverrideCountsAsAReference()
    {
        var referenced = CredentialSweep.ReferencedNames(
            new[] { Service("a", "cred-a"), Service("b", "cred-b", enabled: false) },
            "fish-key",
            new[] { "cred-contact", "", null, "cred-test" });
        Assert.Contains("cred-a", referenced);
        Assert.Contains("cred-b", referenced);
        Assert.Contains("fish-key", referenced);
        Assert.Contains("cred-contact", referenced);
        Assert.Contains("cred-test", referenced);
        Assert.Equal(5, referenced.Count);
    }

    [Fact]
    public void UnreferencedEntriesAreSweptAndReferencedOnesNever()
    {
        var referenced = CredentialSweep.ReferencedNames(
            new[] { Service("a", "cred-a") }, "fish-key");
        var stale = CredentialSweep.UnreferencedEntries(
            new[] { "cred-a", "fish-key", "orphan-1", "orphan-2", "" }, referenced);
        Assert.Equal(new[] { "orphan-1", "orphan-2" }, stale);
    }

    [Fact]
    public void AnEntryCreatedThisSessionIsExemptUntilTheNextStartJudgesIt()
    {
        var referenced = CredentialSweep.ReferencedNames(new[] { Service("a", "cred-a") }, "");
        // This session: just added, not attached to anything yet — kept.
        var now = CredentialSweep.UnreferencedEntries(
            new[] { "cred-a", "brand-new" }, referenced, sessionNewNames: new[] { "brand-new" });
        Assert.Empty(now);
        // The next start: nothing is session-new any more, so it is judged again.
        var later = CredentialSweep.UnreferencedEntries(new[] { "cred-a", "brand-new" }, referenced);
        Assert.Equal(new[] { "brand-new" }, later);
    }

    [Fact]
    public void DeletingAServiceSweepsOnlyTheEntriesItOrphaned()
    {
        var before = new[] { Service("keep", "cred-shared"), Service("gone", "cred-shared"),
            Service("other", "cred-other") };
        var after = before.Where(service => service.Id != "gone").ToList();
        var stale = CredentialSweep.UnreferencedEntries(
            new[] { "cred-shared", "cred-other", "cred-gone-only" },
            CredentialSweep.ReferencedNames(after, ""));
        // cred-shared is still held by the surviving service; only the deleted
        // service's own private pointer goes.
        Assert.Equal(new[] { "cred-gone-only" }, stale);
        var afterBoth = after.Where(service => service.Id != "other").ToList();
        var staleBoth = CredentialSweep.UnreferencedEntries(
            new[] { "cred-shared", "cred-other", "cred-gone-only" },
            CredentialSweep.ReferencedNames(afterBoth, ""));
        Assert.Equal(new[] { "cred-other", "cred-gone-only" }, staleBoth);
    }

    [Fact]
    public void BlankPointersNeverKeepAnEntryAndNeverCrashTheSweep()
    {
        var referenced = CredentialSweep.ReferencedNames(
            new SpeechProviderProfile?[] { Service("a", ""), null }, "  ");
        Assert.Empty(referenced);
        var stale = CredentialSweep.UnreferencedEntries(
            new[] { "keep-me", "", "   " }, referenced, sessionNewNames: new[] { "keep-me" });
        Assert.Empty(stale);
        var judged = CredentialSweep.UnreferencedEntries(new[] { "keep-me", "" }, referenced);
        Assert.Equal(new[] { "keep-me" }, judged);
    }

    [Fact]
    public void AStandbyKeyOfAServiceIsNeverSwept()
    {
        // The multi-key UI keeps one service per key; each pointer must survive
        // even while the service is switched off, or enabling it later 401s.
        var referenced = CredentialSweep.ReferencedNames(
            new[] { Service("active", "cred-on"), Service("standby", "cred-off", enabled: false) }, "");
        var stale = CredentialSweep.UnreferencedEntries(new[] { "cred-on", "cred-off", "orphan" }, referenced);
        Assert.Equal(new[] { "orphan" }, stale);
    }
}
