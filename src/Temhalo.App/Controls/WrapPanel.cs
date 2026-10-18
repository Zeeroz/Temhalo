using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Temhalo.App.Controls;

/// <summary>A setting's explanation and editor share a row, then stack at compact widths.</summary>
public sealed class SettingRow : Panel
{
    public double EditorWidth { get; set; } = 260;
    private const double Gap = 24, Threshold = 580;
    protected override Size MeasureOverride(Size size)
    {
        if (Children.Count != 2) return new Size(size.Width, 0);
        bool wide = size.Width >= Threshold;
        Children[0].Measure(new Size(wide ? size.Width - EditorWidth - Gap : size.Width, double.PositiveInfinity));
        Children[1].Measure(new Size(wide ? EditorWidth : size.Width, double.PositiveInfinity));
        return new Size(size.Width, wide ? Math.Max(Children[0].DesiredSize.Height, Children[1].DesiredSize.Height)
            : Children[0].DesiredSize.Height + 12 + Children[1].DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        if (Children.Count != 2) return size;
        bool wide = size.Width >= Threshold;
        Children[0].Arrange(new Rect(0, wide ? (size.Height - Children[0].DesiredSize.Height) / 2 : 0,
            wide ? size.Width - EditorWidth - Gap : size.Width, Children[0].DesiredSize.Height));
        Children[1].Arrange(new Rect(wide ? size.Width - EditorWidth : 0,
            wide ? (size.Height - Children[1].DesiredSize.Height) / 2 : Children[0].DesiredSize.Height + 12,
            wide ? EditorWidth : size.Width, Children[1].DesiredSize.Height));
        return size;
    }
}

/// <summary>Content-width layout; collapses without relying on the desktop width.</summary>
public sealed class AdaptiveColumns : Panel
{
    public int Columns { get; set; } = 2;
    public double Threshold { get; set; } = 920;
    public double Spacing { get; set; } = 24;
    private int Count(double width) => width >= Threshold ? Math.Max(1, Columns) : 1;
    protected override Size MeasureOverride(Size size)
    {
        int count = Count(size.Width); double cell = Math.Max(0, (size.Width - (count - 1) * Spacing) / count), height = 0;
        for (int start = 0; start < Children.Count; start += count)
        {
            double row = 0;
            for (int i = start; i < Math.Min(start + count, Children.Count); i++)
            { Children[i].Measure(new Size(cell, double.PositiveInfinity)); row = Math.Max(row, Children[i].DesiredSize.Height); }
            height += row + (start == 0 ? 0 : Spacing);
        }
        return new Size(size.Width, height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        int count = Count(size.Width); double cell = Math.Max(0, (size.Width - (count - 1) * Spacing) / count), y = 0;
        for (int start = 0; start < Children.Count; start += count)
        {
            double row = 0;
            for (int i = start; i < Math.Min(start + count, Children.Count); i++) row = Math.Max(row, Children[i].DesiredSize.Height);
            for (int i = start; i < Math.Min(start + count, Children.Count); i++) Children[i].Arrange(new Rect((i - start) * (cell + Spacing), y, cell, row));
            y += row + Spacing;
        }
        return size;
    }
}

/// <summary>Measures each native control at its natural width; long French labels never share a fixed cell.</summary>
public sealed class WrapPanel : Panel
{
    public double Spacing { get; set; } = 8;
    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, row = 0, width = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width) { width = Math.Max(width, x - Spacing); x = 0; y += row + Spacing; row = 0; }
            x += size.Width + Spacing; row = Math.Max(row, size.Height);
        }
        return new Size(Math.Max(width, Math.Max(0, x - Spacing)), y + row);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, row = 0;
        foreach (UIElement child in Children)
        {
            Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width) { x = 0; y += row + Spacing; row = 0; }
            child.Arrange(new Rect(x, y, Math.Min(size.Width, finalSize.Width), size.Height));
            x += size.Width + Spacing; row = Math.Max(row, size.Height);
        }
        return finalSize;
    }
}
