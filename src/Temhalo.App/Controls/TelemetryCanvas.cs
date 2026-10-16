using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Temhalo.Presentation;
using Windows.Foundation;

namespace Temhalo.App.Controls;

internal static class Drawing
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static void Line(Canvas canvas, double x1, double y1, double x2, double y2, string color, double thickness = 1)
        => canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brush(color), StrokeThickness = thickness });
    public static void Text(Canvas canvas, string value, double x, double y, string color = "TemhaloMutedBrush", double size = 12)
    {
        var text = new TextBlock { Text = value, FontSize = size, Foreground = Brush(color),
            FontFamily = (FontFamily)Application.Current.Resources["TemhaloMonoFont"], TextWrapping = TextWrapping.NoWrap };
        Canvas.SetLeft(text, x); Canvas.SetTop(text, y); canvas.Children.Add(text);
    }
}

/// <summary>Observed media age and applied target, independent from the command slider.</summary>
public sealed class DelayTimeline : Canvas
{
    private long? _age;
    private int _target;
    public DelayTimeline() { Height = 66; SizeChanged += (_, _) => Draw(); }
    public void Update(long? milliseconds, int target)
    { if (_age == milliseconds && _target == target && Children.Count > 0) return; _age = milliseconds; _target = target; Draw(); }
    private void Draw()
    {
        Children.Clear(); if (ActualWidth < 40) return;
        double width = ActualWidth - 12, horizon = Math.Max(60, Math.Ceiling(Math.Max((_age ?? 0) / 1000.0, _target) / 30) * 30);
        double target = 6 + width * (1 - _target / horizon);
        Drawing.Line(this, 6, 16, width + 6, 16, "TemhaloRuleBrush", 2);
        if (_age.HasValue)
        {
            double current = 6 + width * (1 - _age.Value / 1000.0 / horizon);
            Drawing.Line(this, current, 16, width + 6, 16, "TemhaloHaloBrush", 2);
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Drawing.Brush("TemhaloTextBrush") };
            Canvas.SetLeft(dot, current - 4); Canvas.SetTop(dot, 12); Children.Add(dot);
        }
        Drawing.Line(this, target, 8, target, 25, "TemhaloEchoBrush", 2);
        int ticks = ActualWidth < 340 ? 2 : 4;
        for (int i = 0; i <= ticks; i++)
        {
            double seconds = horizon * (ticks - i) / ticks;
            string text = seconds == 0 ? "0 s" : $"−{seconds:0} s";
            double x = 6 + width * i / ticks - (i == ticks ? text.Length * 7 : i == 0 ? 0 : text.Length * 3.5);
            Drawing.Text(this, text, Math.Max(0, x), 34, size: 11);
        }
        AutomationProperties.SetName(this, _age.HasValue ? $"Média émis : {_age.Value / 1000} secondes. Cible : {_target} secondes." : $"En attente de média. Cible : {_target} secondes.");
    }
}

/// <summary>Draws only real session samples. Missing samples remain gaps; stopped sessions are not fabricated.</summary>
public sealed class BitrateChart : Canvas
{
    private IReadOnlyList<BitratePoint> _points = [];
    public BitrateChart() { Height = 178; SizeChanged += (_, _) => Draw(); }
    public void Update(IReadOnlyList<BitratePoint> points) { _points = points; Draw(); }
    private void Draw()
    {
        Children.Clear(); if (ActualWidth < 80) return;
        double right = ActualWidth - 6, top = 8, bottom = 142;
        double max = Math.Max(12, Math.Ceiling((_points.Count == 0 ? 0 : _points.Max(p => Math.Max(p.InputMbps, p.OutputMbps))) / 4) * 4);
        for (int i = 0; i <= 3; i++)
        {
            double y = top + (bottom - top) * i / 3;
            Drawing.Line(this, 34, y, right, y, "TemhaloRuleBrush");
            Drawing.Text(this, (max * (3 - i) / 3).ToString("0"), 0, y - 7, size: 11);
        }
        Drawing.Text(this, "−60 s", 34, 152, size: 11);
        Drawing.Text(this, "0 s", right - 22, 152, size: 11);
        if (_points.Count == 0)
        { Drawing.Text(this, "En attente de mesures", 45, 65, size: 12); return; }
        double now = _points[^1].Seconds;
        for (int track = 0; track < 2; track++)
        {
            Polyline? line = null; double previous = double.NegativeInfinity;
            foreach (var point in _points)
            {
                if (point.Seconds - previous > 3 || line is null)
                {
                    line = new Polyline { Stroke = Drawing.Brush(track == 0 ? "TemhaloHaloBrush" : "TemhaloEchoBrush"), StrokeThickness = 2 };
                    if (track == 1) line.StrokeDashArray = new DoubleCollection { 4, 3 };
                    Children.Add(line);
                }
                line.Points.Add(new Point(34 + Math.Clamp((point.Seconds - now + 60) / 60, 0, 1) * (right - 34),
                    bottom - Math.Clamp((track == 0 ? point.InputMbps : point.OutputMbps) / max, 0, 1) * (bottom - top)));
                previous = point.Seconds;
            }
        }
        AutomationProperties.SetName(this, $"Débits sur 60 secondes, en mégabits par seconde. Entrée : {_points[^1].InputMbps:0.00}. Sortie : {_points[^1].OutputMbps:0.00}.");
    }
}
