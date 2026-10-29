using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace LuxEngine.App.Helpers;

/// <summary>Lays children out left to right and starts a new row when the next one does not fit.</summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 10;
    public double VerticalSpacing { get; set; } = 10;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > available.Width)
            {
                y += rowHeight + VerticalSpacing;
                x = 0;
                rowHeight = 0;
            }
            x += size.Width + HorizontalSpacing;
            width = Math.Max(width, x - HorizontalSpacing);
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), y + rowHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowHeight = 0;
        var row = new List<UIElement>();

        void FinishRow()
        {
            // Cards in a row share its height, so their bottoms line up.
            double left = 0;
            foreach (var c in row)
            {
                c.Arrange(new Rect(left, y, c.DesiredSize.Width, rowHeight));
                left += c.DesiredSize.Width + HorizontalSpacing;
            }
            row.Clear();
        }

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > final.Width)
            {
                FinishRow();
                y += rowHeight + VerticalSpacing;
                x = 0;
                rowHeight = 0;
            }
            row.Add(child);
            x += size.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        FinishRow();
        return final;
    }
}
