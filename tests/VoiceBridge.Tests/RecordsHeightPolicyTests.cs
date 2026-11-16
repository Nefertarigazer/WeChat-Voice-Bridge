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
            lastSingleRowOverhead: null,
            rememberForOverflow: true);

        Assert.False(result.PageOverflows);
        Assert.Equal(237, result.MaxListHeight);
        Assert.Equal(506.5, result.RememberedOverhead);
    }

    [Fact]
    public void ZeroContactsOrPickerExpandsPageWithoutOverwritingRememberedOverhead()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 744,
            recordsCardTop: 234.5,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastSingleRowOverhead: 508.5,
            rememberForOverflow: false);

        Assert.False(result.PageOverflows);
        Assert.Equal(479, result.MaxListHeight);
        Assert.Equal(508.5, result.RememberedOverhead);
    }

    [Fact]
    public void MultiContactOverflowLocksToRememberedOverheadAtNormalWindowHeight()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 744,
            recordsCardTop: 790,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastSingleRowOverhead: 508.5,
            rememberForOverflow: false);

        Assert.True(result.PageOverflows);
        Assert.Equal(235, result.MaxListHeight);
        Assert.Equal(508.5, result.RememberedOverhead);
    }

    [Fact]
    public void MultiContactOverflowScalesGenerouslyWhenWindowIsMaximized()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 1040,
            recordsCardTop: 1020,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastSingleRowOverhead: 506.5,
            rememberForOverflow: false);

        Assert.True(result.PageOverflows);
        Assert.Equal(533, result.MaxListHeight);
    }

    [Fact]
    public void MultiContactLaunchWithoutPriorMeasurementUsesCalibratedBaseline()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 744,
            recordsCardTop: 790,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastSingleRowOverhead: null,
            rememberForOverflow: false);

        Assert.True(result.PageOverflows);
        Assert.Equal(237, result.MaxListHeight);
        Assert.Null(result.RememberedOverhead);
    }

    [Fact]
    public void InvalidViewportFallsBackToDefaultHeight()
    {
        var result = RecordsHeightPolicy.Compute(
            viewportHeight: 0,
            recordsCardTop: 400,
            cardChromeHeight: 10,
            bottomClearance: 20,
            lastSingleRowOverhead: 506.5);

        Assert.False(result.PageOverflows);
        Assert.Equal(RecordsHeightPolicy.DefaultListHeight, result.MaxListHeight);
        Assert.Equal(506.5, result.RememberedOverhead);
    }
}
