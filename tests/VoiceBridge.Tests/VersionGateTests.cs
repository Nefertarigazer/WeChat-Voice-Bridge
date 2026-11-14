using System.Text;
using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.Tests;

/// <summary>
/// The version gate is the first thing an unsupported client sees, so its decision
/// is a pure function of the detected version, the audited band and the published
/// manifest. These tests pin the four questions the dialog asks: show?, what does
/// the primary button do?, is "更新微信" offered?, and what does closing the box do?
/// </summary>
public sealed class VersionGateTests
{
    private static readonly Version Min = new(4, 1, 11);
    private static readonly Version Max = new(4, 2, 0, 52);

    private static VersionGateDecision Decide(string? raw, string? publishedVersion = null,
        string? support = null, string appVersion = "1.8.3")
    {
        Version? parsed = null;
        if (raw is not null) Version.TryParse(raw, out parsed);
        return VersionGatePolicy.Decide(raw, parsed, Min, Max, support, appVersion, publishedVersion);
    }

    [Theory]
    [InlineData("4.1.11")]
    [InlineData("4.2.0.13")]
    [InlineData("4.2.0.52")]
    public void SupportedClientShowsNoGate(string version)
    {
        var decision = Decide(version, publishedVersion: "1.8.4", support: "4.1.11-4.2.0.52");
        Assert.False(decision.Show);
        Assert.Equal(VersionGateReason.None, decision.Reason);
        Assert.False(decision.OffersWeChatUpdate);
    }

    [Fact]
    public void MissingClientShowsNoGate()
    {
        var decision = Decide(null, publishedVersion: "1.8.4");
        Assert.False(decision.Show);
        Assert.Equal(VersionGateReason.None, decision.Reason);
    }

    [Fact]
    public void OlderClientOffersWeChatDownload()
    {
        var decision = Decide("4.1.10.99");
        Assert.True(decision.Show);
        Assert.Equal(VersionGateReason.WeChatTooOld, decision.Reason);
        Assert.True(decision.OffersWeChatUpdate);
        Assert.True(decision.OffersBridgeUpdate);
    }

    [Theory]
    [InlineData("4.2.0.53")]
    [InlineData("4.2.1.0")]
    [InlineData("5.0.0.1")]
    public void NewerClientNeverOffersWeChatDownload(string version)
    {
        var decision = Decide(version);
        Assert.True(decision.Show);
        Assert.Equal(VersionGateReason.WeChatTooNew, decision.Reason);
        Assert.False(decision.OffersWeChatUpdate);
        // The bridge update stays the primary action even when it cannot help yet.
        Assert.True(decision.OffersBridgeUpdate);
    }

    [Fact]
    public void UnreadableClientVersionShowsTheGateWithoutWeChatDownload()
    {
        var decision = Decide("4.2.0.beta");
        Assert.True(decision.Show);
        Assert.Equal(VersionGateReason.VersionUnreadable, decision.Reason);
        Assert.False(decision.OffersWeChatUpdate);
        Assert.Equal("4.2.0.beta", decision.ClientVersion);
    }

    [Fact]
    public void NewerBridgeCoveringTheClientIsOfferedAsTheFix()
    {
        var decision = Decide("4.2.0.60", publishedVersion: "1.8.4", support: "4.1.11-4.2.0.60");
        Assert.Equal("1.8.4", decision.BridgeVersion);
        Assert.True(decision.BridgeUpdateCoversWeChat);
        Assert.False(decision.OffersWeChatUpdate);
    }

    [Fact]
    public void NewerBridgeThatStillDoesNotCoverTheClientSaysSo()
    {
        var decision = Decide("4.2.0.60", publishedVersion: "1.8.4", support: "4.1.11-4.2.0.52");
        Assert.Equal("1.8.4", decision.BridgeVersion);
        Assert.False(decision.BridgeUpdateCoversWeChat);
        Assert.False(decision.OffersWeChatUpdate);
    }

    [Fact]
    public void LatestBridgeStillUnsupportedLeavesNoUpdateToOffer()
    {
        var decision = Decide("4.2.0.60", publishedVersion: "1.8.3", appVersion: "1.8.3");
        Assert.True(decision.Show);
        Assert.Null(decision.BridgeVersion);
        Assert.False(decision.BridgeUpdateCoversWeChat);
        Assert.True(decision.OffersBridgeUpdate);
    }

    [Fact]
    public void OlderPublishedVersionIsNotOffered()
    {
        var decision = Decide("4.2.0.60", publishedVersion: "1.8.2", support: "4.1.11-4.2.0.60");
        Assert.Null(decision.BridgeVersion);
        Assert.False(decision.BridgeUpdateCoversWeChat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("4.1.11")]
    [InlineData("4.2.0.52-4.1.11")]
    [InlineData("4.1.11-4.2.0.52-4.3.0")]
    [InlineData("4.1-4.2")]
    [InlineData("abc-def")]
    [InlineData("4.1.11+")]
    public void MissingOrMalformedSupportBandFallsBackToOfferingTheUpdate(string? support)
    {
        var decision = Decide("4.2.0.60", publishedVersion: "1.8.4", support: support);
        Assert.Equal("1.8.4", decision.BridgeVersion);
        Assert.True(decision.BridgeUpdateCoversWeChat);
    }

    [Fact]
    public void MalformedPublishedVersionIsIgnoredInsteadOfThrowing()
    {
        var decision = Decide("4.2.0.60", publishedVersion: "latest");
        Assert.Null(decision.BridgeVersion);
        Assert.False(decision.BridgeUpdateCoversWeChat);
    }

    [Theory]
    [InlineData("4.1.11-4.2.0.52", "4.1.11", true)]
    [InlineData("4.1.11-4.2.0.52", "4.2.0.52", true)]
    [InlineData("4.1.11-4.2.0.52", "4.2.0.53", false)]
    [InlineData(" 4.1.11 - 4.2.0.52 ", "4.2.0.13", true)]
    [InlineData("4.1.11–4.2.0.52", "4.2.0.13", true)]
    public void SupportBandIsInclusiveOnBothEnds(string text, string client, bool inside)
    {
        Assert.True(WeChatSupportRange.TryParse(text, out var range));
        Assert.Equal(inside, range.Contains(new Version(client)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4.1.11")]
    [InlineData("4.1.11-")]
    [InlineData("-4.2.0.52")]
    [InlineData("4.2.0.52-4.1.11")]
    [InlineData("4.1-4.2")]
    [InlineData("4.1.11-4.2.0.x")]
    public void SupportBandRejectsUnusableText(string? text) =>
        Assert.False(WeChatSupportRange.TryParse(text, out _));

    [Theory]
    // Closing the box with nothing chosen quits, by the owner's explicit decision.
    [InlineData(false, false, VersionGateExit.ExitApplication)]
    // After picking 更新语音桥 or 更新微信 the close is an ordinary dismissal.
    [InlineData(true, false, VersionGateExit.KeepRunning)]
    // A started install handoff needs this process gone so files can be replaced.
    [InlineData(true, true, VersionGateExit.ExitApplication)]
    [InlineData(false, true, VersionGateExit.ExitApplication)]
    public void ClosingTheBoxQuitsUnlessARemediationWasChosen(bool chosen, bool installStarted,
        VersionGateExit expected) =>
        Assert.Equal(expected, VersionGatePolicy.OnDialogClosed(chosen, installStarted));

    [Fact]
    public void ManifestAcceptsTheOptionalSupportBand()
    {
        var manifest = UpdateManifest.Parse(ManifestJson(support: "4.1.11-4.2.0.52"));
        Assert.Equal("4.1.11-4.2.0.52", manifest.WeChatSupport);
    }

    [Fact]
    public void ManifestWithoutSupportBandStaysValid()
    {
        var manifest = UpdateManifest.Parse(ManifestJson());
        Assert.Null(manifest.WeChatSupport);
    }

    [Fact]
    public void ManifestIgnoresANonStringSupportBand()
    {
        var manifest = UpdateManifest.Parse(ManifestJson(supportObject: true));
        Assert.Null(manifest.WeChatSupport);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("wechatSupportPlus")]
    public void ManifestStillRejectsUnknownExtraFields(string name) =>
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Parse(ManifestJson(extraField: name)));

    private static byte[] ManifestJson(string? support = null, bool supportObject = false, string? extraField = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["version"] = "1.8.4",
            ["publishedAt"] = "2026-11-15T00:00:00Z",
            ["url"] = "https://iceland.de5.net/voice-bridge/build.zip",
            ["sha256"] = new string('a', 64),
            ["size"] = 42,
            ["notes"] = "第一行"
        };
        if (supportObject) fields["wechatSupport"] = new { min = "4.1.11" };
        else if (support is not null) fields["wechatSupport"] = support;
        if (extraField is not null) fields[extraField] = "x";
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fields));
    }
}
