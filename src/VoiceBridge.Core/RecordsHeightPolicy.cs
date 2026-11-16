namespace VoiceBridge.Core;

/// <summary>
/// Computes the maximum height of the message records list on the Home page.
/// When contacts fit on the page with room left below, the records card
/// expands up to that remaining viewport space (leaving a clean bottom margin)
/// without causing the page itself to scroll. Once additional contacts push the
/// page into scrolling, the records list caps its maximum height at the
/// remembered non-scrolling limit (or the calibrated fallback).
/// </summary>
public static class RecordsHeightPolicy
{
    public const double DefaultListHeight = 138;
    public const double DefaultOverflowMaxListHeight = 237;

    public static RecordsHeightResult Compute(
        double viewportHeight,
        double recordsCardTop,
        double cardChromeHeight,
        double bottomClearance,
        double? lastNonScrollingMaxListHeight,
        bool rememberForOverflow = true)
    {
        if (viewportHeight <= 0 || double.IsNaN(viewportHeight) || double.IsInfinity(viewportHeight) ||
            double.IsNaN(recordsCardTop) || double.IsInfinity(recordsCardTop))
            return new(DefaultListHeight, lastNonScrollingMaxListHeight, false);

        var available = Math.Floor(viewportHeight - recordsCardTop - cardChromeHeight - bottomClearance);
        if (available >= DefaultListHeight)
        {
            var remembered = rememberForOverflow ? available : lastNonScrollingMaxListHeight;
            return new(available, remembered, false);
        }

        var capped = Math.Max(DefaultListHeight, lastNonScrollingMaxListHeight ?? DefaultOverflowMaxListHeight);
        return new(capped, lastNonScrollingMaxListHeight, true);
    }
}

public readonly record struct RecordsHeightResult(
    double MaxListHeight,
    double? RememberedMaxListHeight,
    bool PageOverflows);
