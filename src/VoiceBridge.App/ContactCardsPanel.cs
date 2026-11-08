using System;
using System.Windows;
using System.Windows.Controls;

namespace VoiceBridge.App;

/// <summary>At most two equal-width contact cards per row, with safe spacing on small windows.</summary>
public sealed class ContactCardsPanel : Panel
{
    private const double Gap = 12;
    private static int Columns(double width, int count) => count > 1 && width >= 572 ? 2 : 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 572 : availableSize.Width;
        var columns = Columns(width, InternalChildren.Count);
        var cardWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns);
        double height = 0, rowHeight = 0;
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            child.Measure(new Size(cardWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (index % columns == columns - 1 || index == InternalChildren.Count - 1)
            {
                if (height > 0) height += Gap;
                height += rowHeight; rowHeight = 0;
            }
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns(finalSize.Width, InternalChildren.Count);
        var cardWidth = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns);
        double top = 0;
        for (var index = 0; index < InternalChildren.Count; index += columns)
        {
            double rowHeight = 0;
            for (var column = 0; column < columns && index + column < InternalChildren.Count; column++)
                rowHeight = Math.Max(rowHeight, InternalChildren[index + column].DesiredSize.Height);
            for (var column = 0; column < columns && index + column < InternalChildren.Count; column++)
                InternalChildren[index + column].Arrange(new Rect(column * (cardWidth + Gap), top, cardWidth, rowHeight));
            top += rowHeight + Gap;
        }
        return finalSize;
    }
}
