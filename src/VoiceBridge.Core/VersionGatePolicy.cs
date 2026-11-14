namespace VoiceBridge.Core;

/// <summary>Where the detected WeChat client sits relative to the audited band.</summary>
public enum VersionGateReason
{
    /// <summary>Inside the supported band, or no client was detected at all.</summary>
    None,
    WeChatTooOld,
    WeChatTooNew,
    VersionUnreadable
}

/// <summary>What closing the gate dialog must do to the application.</summary>
public enum VersionGateExit
{
    KeepRunning,
    ExitApplication
}

/// <summary>
/// The WeChat band a published bridge version covers, as written into the manifest
/// field <c>wechatSupport</c> (for example <c>4.1.11-4.2.0.52</c>).
/// </summary>
/// <remarks>
/// A missing or malformed value is never an error: the caller falls back to
/// "a newer bridge version is offered, so offer the update". Parsing therefore
/// fails closed (returns false) instead of guessing a band.
/// </remarks>
public readonly record struct WeChatSupportRange(Version Min, Version Max)
{
    public bool Contains(Version version) => version >= Min && version <= Max;

    public static bool TryParse(string? text, out WeChatSupportRange range)
    {
        range = default;
        if (text is null) return false;
        // Muse writes the ASCII hyphen; accept the dashes a hand-edited copy may use.
        var cleaned = text.Trim().Replace('–', '-').Replace('—', '-').Replace('－', '-');
        if (cleaned.Length == 0) return false;
        var parts = cleaned.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !Version.TryParse(parts[0], out var min) || !Version.TryParse(parts[1], out var max))
            return false;
        // Both bounds must carry at least major.minor.build: "4.2" would otherwise
        // exclude every real 4.2.0.x client because Version compares a missing
        // component as -1.
        if (min.Build < 0 || max.Build < 0 || min > max) return false;
        range = new(min, max);
        return true;
    }
}

/// <summary>
/// The gate's verdict, separated from the dialog so it can be unit-tested
/// without WPF: whether to show anything, what the primary button offers, and
/// whether the "update WeChat" button belongs in the box.
/// </summary>
public sealed record VersionGateDecision
{
    public bool Show { get; internal init; }
    public VersionGateReason Reason { get; internal init; }

    /// <summary>The raw file version read from the running client, for the dialog copy.</summary>
    public string? ClientVersion { get; internal init; }

    /// <summary>The newer bridge version on offer, or null when the installed one is the latest.</summary>
    public string? BridgeVersion { get; internal init; }

    /// <summary>
    /// True when the offered bridge version states it covers this WeChat build.
    /// When the manifest has no usable <c>wechatSupport</c> this degrades to
    /// "a newer bridge version exists", which is the agreed fallback.
    /// </summary>
    public bool BridgeUpdateCoversWeChat { get; internal init; }

    /// <summary>Offered only for a client below the supported lower bound.</summary>
    public bool OffersWeChatUpdate => Reason == VersionGateReason.WeChatTooOld;

    /// <summary>The "更新语音桥" button is the primary action of every gate.</summary>
    public bool OffersBridgeUpdate => Show;
}

public static class VersionGatePolicy
{
    /// <summary>
    /// Decide what the gate should do. Every input is plain data: the detected
    /// client version, the audited band, and the update channel's verdict.
    /// </summary>
    public static VersionGateDecision Decide(string? clientRaw, Version? client,
        Version minSupported, Version maxSupported, string? publishedWeChatSupport,
        string? appVersion, string? publishedVersion)
    {
        ArgumentNullException.ThrowIfNull(minSupported);
        ArgumentNullException.ThrowIfNull(maxSupported);
        if (clientRaw is null)
            return new() { Reason = VersionGateReason.None };
        if (client is not null && client >= minSupported && client <= maxSupported)
            return new() { Reason = VersionGateReason.None, ClientVersion = clientRaw };

        var reason = client is null ? VersionGateReason.VersionUnreadable
            : client < minSupported ? VersionGateReason.WeChatTooOld
            : VersionGateReason.WeChatTooNew;
        var newer = IsNewer(publishedVersion, appVersion);
        // An unreadable client version cannot be matched against the published band,
        // so the update stays on offer exactly like a manifest without the field.
        var covers = newer && (client is null ||
            !WeChatSupportRange.TryParse(publishedWeChatSupport, out var published) ||
            published.Contains(client));
        return new()
        {
            Show = true,
            Reason = reason,
            ClientVersion = clientRaw,
            BridgeVersion = newer ? publishedVersion : null,
            BridgeUpdateCoversWeChat = covers
        };
    }

    /// <summary>
    /// Closing the box quits the application — that is the agreed behaviour, not a
    /// dismissal. The two exceptions are a remediation the user already picked
    /// (updating the bridge, or opening the WeChat download page) and a finished
    /// install handoff, which needs this process gone so the files can be replaced.
    /// </summary>
    public static VersionGateExit OnDialogClosed(bool remediationChosen, bool installStarted) =>
        installStarted || !remediationChosen ? VersionGateExit.ExitApplication : VersionGateExit.KeepRunning;

    private static bool IsNewer(string? publishedVersion, string? appVersion)
    {
        if (publishedVersion is null || appVersion is null) return false;
        try { return UpdateManifest.CompareVersions(publishedVersion, appVersion) > 0; }
        catch (InvalidDataException) { return false; }
    }
}
