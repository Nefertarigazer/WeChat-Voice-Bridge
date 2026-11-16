namespace VoiceBridge.Core;

/// <summary>
/// Computes the maximum height of the message records list on the Home page:
/// - While contacts fit on the page, the records box grows seamlessly with incoming
///   messages up to the remaining viewport height (leaving a clean bottom margin)
///   without scrolling the page or showing an internal scrollbar prematurely.
/// - When more contacts push the page into scrolling, the records list scales its
///   maximum height with the current viewport using the remembered single-row
///   overhead (or the calibrated baseline 507 DIP), so maximizing the window opens a
///   spacious chat view while a normal 940x780 window stays at 237 DIP.
/// </summary>
public static class RecordsHeightPolicy
{
    public const double DefaultListHeight = 138;
    /// <summary>
    /// Measured on a 940x780 window (viewport = 744 DIP) with one contact: 744 - 507 = 237 DIP.
    /// </summary>
    public const double DefaultNonScrollingOverhead = 507;
    public const double MaxViewportShareOnOverflow = 0.68;

    public static RecordsHeightResult Compute(
        double viewportHeight,
        double recordsCardTop,
        double cardChromeHeight,
        double bottomClearance,
        double? lastSingleRowOverhead,
        bool rememberForOverflow = true)
    {
        if (viewportHeight <= 0 || double.IsNaN(viewportHeight) || double.IsInfinity(viewportHeight) ||
            double.IsNaN(recordsCardTop) || double.IsInfinity(recordsCardTop))
            return new(DefaultListHeight, lastSingleRowOverhead, false);

        var overhead = recordsCardTop + cardChromeHeight + bottomClearance;
        var available = Math.Floor(viewportHeight - overhead);
        if (available >= DefaultListHeight)
        {
            var remembered = rememberForOverflow ? overhead : lastSingleRowOverhead;
            return new(available, remembered, false);
        }

        var effectiveOverhead = lastSingleRowOverhead ?? DefaultNonScrollingOverhead;
        var scaled = Math.Floor(viewportHeight - effectiveOverhead);
        var shareCap = Math.Floor(viewportHeight * MaxViewportShareOnOverflow);
        var capped = Math.Max(DefaultListHeight, Math.Min(Math.Max(DefaultListHeight, scaled), shareCap));
        return new(capped, lastSingleRowOverhead, true);
    }
}

public readonly record struct RecordsHeightResult(
    double MaxListHeight,
    double? RememberedOverhead,
    bool PageOverflows);
