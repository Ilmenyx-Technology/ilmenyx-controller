// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

/// <summary>Fixed-window area chart (in-memory only, never persisted). Null samples leave gaps at zero height.</summary>
public sealed class UsageChart : Control
{
    public const int Window = 60; // default visible points (1 s each)
    private static readonly IBrush Back = Palette.Card, Fill = Palette.DataCpu; // série de CPU da paleta; segue o tema
    private static readonly IPen Grid = new Pen(Palette.Line, 1), Line = new Pen(Palette.AccentText, 1.6, lineJoin: PenLineJoin.Round);
    private static readonly IPen SecondLine = new Pen(Palette.Warn, 1.4);
    private double[] values = [], second = [];
    private double max = 1;
    public int Points { get; private set; } = Window;
    public double LastMax => max;
    public int Count => values.Length;
    public UsageChart() { MinHeight = 90; MinWidth = 120; Focusable = false; }

    /// <param name="series">Oldest first; at most <see cref="Window"/> newest values are kept.</param>
    /// <param name="ceiling">Fixed upper bound (e.g. 100 for percent) or null for auto-scale.</param>
    public void SetValues(IReadOnlyList<double> series, double? ceiling, int points = Window)
    {
        Points = Math.Max(2, points);
        values = series.Count > Points ? series.Skip(series.Count - Points).ToArray() : series.ToArray();
        max = ceiling ?? Math.Max(1, values.Length == 0 ? 1 : values.Max() * 1.15);
        InvalidateVisual();
    }

    /// <summary>Optional second line (e.g. GPU load) drawn over the first with the same ceiling; empty hides it.</summary>
    public void SetSecond(IReadOnlyList<double> series) { second = series.Count > Points ? series.Skip(series.Count - Points).ToArray() : series.ToArray(); InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(Back, null, rect, IlmenyxTheme.ControlRadius, IlmenyxTheme.ControlRadius);
        for (int i = 1; i < 4; i++) context.DrawLine(Grid, new Point(0, rect.Height * i / 4), new Point(rect.Width, rect.Height * i / 4));
        if (values.Length < 2 || rect.Width < 2 || rect.Height < 2) return;
        double step = rect.Width / (Points - 1), left = rect.Width - step * (values.Length - 1);
        Point P(int i) => new(left + step * i, rect.Height - Math.Clamp(values[i] / max, 0, 1) * (rect.Height - 2) - 1);
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(left, rect.Height), true);
            for (int i = 0; i < values.Length; i++) g.LineTo(P(i));
            g.LineTo(new Point(left + step * (values.Length - 1), rect.Height)); g.EndFigure(true);
        }
        // Soft vertical fade of the series colour (rebuilt per frame so it follows theme switches).
        var c = ((ISolidColorBrush)Fill).Color;
        var fade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0x70, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(0x08, c.R, c.G, c.B), 1) },
        };
        context.DrawGeometry(fade, null, area);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(P(0), false);
            for (int i = 1; i < values.Length; i++) g.LineTo(P(i));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, Line, line);
        if (second.Length >= 2)
        {
            double step2 = rect.Width / (Points - 1), left2 = rect.Width - step2 * (second.Length - 1);
            var g2 = new StreamGeometry();
            using (var g = g2.Open())
            {
                for (int i = 0; i < second.Length; i++)
                {
                    var pt = new Point(left2 + step2 * i, rect.Height - Math.Clamp(second[i] / max, 0, 1) * (rect.Height - 2) - 1);
                    if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(false);
            }
            context.DrawGeometry(null, SecondLine, g2);
        }
    }
}
