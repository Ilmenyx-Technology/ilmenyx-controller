// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

/// <summary>Occupancy ring: faint tonal track, accent arc with round ends for the used fraction (84x84 like the original card ring).</summary>
public sealed class RingGauge : Control
{
    private readonly double fraction;
    public RingGauge(double fraction) { this.fraction = Math.Clamp(fraction, 0, 1); Width = 84; Height = 84; }
    public override void Render(DrawingContext context)
    {
        var center = new Point(42, 42); const double radius = 35, thickness = 7;
        context.DrawEllipse(null, new Pen(Palette.Hover, thickness), center, radius, radius);
        if (fraction <= 0) return;
        if (fraction >= 0.999) { context.DrawEllipse(null, new Pen(Palette.Accent, thickness), center, radius, radius); return; }
        double angle = fraction * 2 * Math.PI - Math.PI / 2;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) { g.BeginFigure(start, false); g.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise); g.EndFigure(false); }
        context.DrawGeometry(null, new Pen(Palette.Accent, thickness, lineCap: PenLineCap.Round), geometry);
    }
}
