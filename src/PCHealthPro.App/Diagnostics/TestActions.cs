using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace PCHealthPro.App.Diagnostics;

// Preserve keyboard/tab order while allowing translated button labels to wrap onto new rows.
internal sealed class TestActions : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return Layout(availableSize.Width, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, true);
        return finalSize;
    }

    private Size Layout(double width, bool arrange)
    {
        const double gap = 8;
        double x = 0, y = 0, rowHeight = 0, used = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += rowHeight + gap;
                rowHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, size.Width, size.Height));
            used = Math.Max(used, x + size.Width);
            x += size.Width + gap;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(used, y + rowHeight);
    }
}
