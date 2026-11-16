using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class RecordsHeightPolicyTests
{
    [Fact]
    public void ExpandsToFillRemainingPageWhenContactsFit()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 744,
            recordsCardTop: 476.5,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastNonScrollingMaxListHeight: null,
            rememberForOverflow: true);

        Assert.False(result.PageOverflows);
        Assert.Equal(237, result.MaxListHeight);
        Assert.Equal(237, result.RememberedMaxListHeight);
    }

    [Fact]
    public void ZeroContactsOrPickerExpandsPageWithoutOverwritingRememberedCap()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 744,
            recordsCardTop: 234.5,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastNonScrollingMaxListHeight: 235,
            rememberForOverflow: false);

        Assert.False(result.PageOverflows);
        Assert.Equal(479, result.MaxListHeight);
        Assert.Equal(235, result.RememberedMaxListHeight);
    }

    [Fact]
    public void MultiContactOverflowLocksToRememberedPageHeight()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 744,
            recordsCardTop: 790,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastNonScrollingMaxListHeight: 227,
            rememberForOverflow: true);

        Assert.True(result.PageOverflows);
        Assert.Equal(227, result.MaxListHeight);
        Assert.Equal(227, result.RememberedMaxListHeight);
    }

    [Fact]
    public void MultiContactLaunchWithoutPriorMeasurementUsesCalibratedFallback()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 744,
            recordsCardTop: 790,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastNonScrollingMaxListHeight: null,
            rememberForOverflow: true);

        Assert.True(result.PageOverflows);
        Assert.Equal(RecordsHeightPolicy.DefaultOverflowMaxListHeight, result.MaxListHeight);
        Assert.Null(result.RememberedMaxListHeight);
    }

    [Fact]
    public void InvalidViewportFallsBackToDefaultHeight()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 0,
            recordsCardTop: 400,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastNonScrollingMaxListHeight: 237);

        Assert.False(result.PageOverflows);
        Assert.Equal(RecordsHeightPolicy.DefaultListHeight, result.MaxListHeight);
        Assert.Equal(237, result.RememberedMaxListHeight);
    }
}
