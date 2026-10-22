// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

/// <summary>Fixed-window area chart (in-memory only, never persisted). Null samples leave gaps at zero height.</summary>
public sealed class UsageChart : Control
{
    public const int Window = 60;
    private static readonly IBrush Back = Brush.Parse("#1B1721"), Fill = new SolidColorBrush(Color.Parse("#B07CD6"), 0.55);
    private static readonly IPen Grid = new Pen(Brush.Parse("#4A4454"), 1), Line = new Pen(Brush.Parse("#D9A8FF"), 1.4);
    private double[] values = [];
    private double max = 1;
    public double LastMax => max;
    public int Count => values.Length;
    public UsageChart() { MinHeight = 90; MinWidth = 160; Focusable = false; }

    /// <param name="series">Oldest first; at most <see cref="Window"/> newest values are kept.</param>
    /// <param name="ceiling">Fixed upper bound (e.g. 100 for percent) or null for auto-scale.</param>
    public void SetValues(IReadOnlyList<double> series, double? ceiling)
    {
        values = series.Count > Window ? series.Skip(series.Count - Window).ToArray() : series.ToArray();
        max = ceiling ?? Math.Max(1, values.Length == 0 ? 1 : values.Max() * 1.15);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(Back, null, rect, 6, 6);
        for (int i = 1; i < 6; i++) context.DrawLine(Grid, new Point(rect.Width * i / 6, 0), new Point(rect.Width * i / 6, rect.Height));
        for (int i = 1; i < 4; i++) context.DrawLine(Grid, new Point(0, rect.Height * i / 4), new Point(rect.Width, rect.Height * i / 4));
        if (values.Length < 2 || rect.Width < 2 || rect.Height < 2) return;
        double step = rect.Width / (Window - 1), left = rect.Width - step * (values.Length - 1);
        Point P(int i) => new(left + step * i, rect.Height - Math.Clamp(values[i] / max, 0, 1) * (rect.Height - 2) - 1);
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(left, rect.Height), true);
            for (int i = 0; i < values.Length; i++) g.LineTo(P(i));
            g.LineTo(new Point(left + step * (values.Length - 1), rect.Height)); g.EndFigure(true);
        }
        context.DrawGeometry(Fill, null, area);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(P(0), false);
            for (int i = 1; i < values.Length; i++) g.LineTo(P(i));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, Line, line);
    }
}
