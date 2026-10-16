using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Temhalo.Infrastructure.Windows.Settings;
using Windows.UI;

namespace Temhalo.App.Controls;

/// <summary>Local editor preview only. The approved mark is never recolored by custom overlay accents.</summary>
public sealed class OverlayPreview : UserControl
{
    private readonly Grid _scene = new() { Width = 1920, Height = 1080 };
    private OverlayAppearance? _last;
    private int _style = -1, _state = -1;
    public OverlayPreview()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = new Viewbox { Child = _scene, Stretch = Stretch.Uniform, MaxHeight = 405, MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Center };
    }
    public void Render(OverlayAppearance appearance, int style, int state)
    {
        if (_last == appearance && _style == style && _state == state) return;
        _last = appearance; _style = style; _state = state;
        _scene.Children.Clear(); _scene.Background = Drawing.Brush("TemhaloCanvasBrush");
        var checker = new Canvas();
        for (int y = 0; y < 14; y++) for (int x = 0; x < 24; x++)
            if ((x + y) % 2 == 0)
            {
                var square = new Rectangle { Width = 80, Height = 80, Fill = Drawing.Brush("TemhaloSurfaceBrush") };
                Canvas.SetLeft(square, x * 80); Canvas.SetTop(square, y * 80); checker.Children.Add(square);
            }
        _scene.Children.Add(checker);
        var center = new TextBlock { Text = "Aperçu local · Fond transparent", FontSize = 30,
            Foreground = Drawing.Brush("TemhaloMutedBrush"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        _scene.Children.Add(center);
        string label = state == 0 ? "En direct" : state == 1 ? "Ajout du délai" : appearance.Label;
        string time = state == 0 ? "00:00" : state == 1 ? "00:30 → 01:00" : "00:30";
        AutomationProperties.SetName(this, $"Aperçu d’habillage, {label}, {time}. Aucune source navigateur n’est active.");
        if (state == 0 && !appearance.VisibleWhenLive) return;
        var accent = new SolidColorBrush(Color.FromArgb(255, Convert.ToByte(appearance.Accent.Substring(1, 2), 16),
            Convert.ToByte(appearance.Accent.Substring(3, 2), 16), Convert.ToByte(appearance.Accent.Substring(5, 2), 16)));
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, VerticalAlignment = VerticalAlignment.Center };
        if (style == 0) content.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/Brand/temhalo-symbol-color.svg")), Width = 64, Height = 64 });
        var texts = new StackPanel { Spacing = 0 };
        if (style != 3) texts.Children.Add(new TextBlock { Text = label, FontSize = 30, Foreground = Drawing.Brush("TemhaloSecondaryBrush") });
        if (state != 0 || style == 3) texts.Children.Add(new TextBlock { Text = time, FontSize = style == 3 ? 56 : 40,
            FontFamily = (FontFamily)Application.Current.Resources["TemhaloMonoFont"], Foreground = style == 3 ? Drawing.Brush("TemhaloTextBrush") : accent });
        if (style == 2) texts.Children.Add(new Border { Height = 3, Background = Drawing.Brush("TemhaloEchoBrush"), Margin = new Thickness(0, 12, 0, 0) });
        if (style == 1)
        {
            var ticks = new Grid { Width = 14, Margin = new Thickness(0, 0, 4, 0) };
            ticks.Children.Add(new Border { Width = 4, Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) });
            ticks.Children.Add(new Border { Width = 4, Background = Drawing.Brush("TemhaloEchoBrush"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) });
            content.Children.Add(ticks);
        }
        content.Children.Add(texts);
        var badge = new Border { Child = content, Background = Drawing.Brush("TemhaloCanvasBrush"),
            Padding = new Thickness(28, 20, 28, 20), CornerRadius = new CornerRadius(style == 0 ? 16 : 8) };
        if (style == 3) { badge.BorderBrush = accent; badge.BorderThickness = new Thickness(4, 0, 0, 0); }
        var scaled = new Viewbox { Child = badge, Stretch = Stretch.Uniform, Margin = new Thickness(135),
            HorizontalAlignment = appearance.Position % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = appearance.Position < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom };
        badge.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        scaled.Width = badge.DesiredSize.Width * appearance.ScalePercent / 100;
        scaled.Height = badge.DesiredSize.Height * appearance.ScalePercent / 100;
        _scene.Children.Add(scaled);
    }
}
