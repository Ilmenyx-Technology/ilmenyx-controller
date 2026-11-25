// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

/// <summary>Occupancy ring: faint tonal track, accent arc with round ends for the used fraction (84x84 like the original card ring).</summary>
public sealed class RingGauge : Control
{
    private readonly double fraction, size;
    private readonly IBrush arc;
    /// <param name="arc">Arc colour (default: the accent); Conquistas uses the amber token for its trophy rings.</param>
    /// <param name="size">Side in pixels (default 84; the ring scales with it).</param>
    public RingGauge(double fraction, IBrush? arc = null, double size = 84) { this.fraction = Math.Clamp(fraction, 0, 1); this.size = size; this.arc = arc ?? Palette.Accent; Width = Height = size; }
    public override void Render(DrawingContext context)
    {
        var center = new Point(size / 2, size / 2); double radius = size * 35 / 84, thickness = size * 7 / 84;
        context.DrawEllipse(null, new Pen(Palette.Hover, thickness), center, radius, radius);
        if (fraction <= 0) return;
        if (fraction >= 0.999) { context.DrawEllipse(null, new Pen(arc, thickness), center, radius, radius); return; }
        double angle = fraction * 2 * Math.PI - Math.PI / 2;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) { g.BeginFigure(start, false); g.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise); g.EndFigure(false); }
        context.DrawGeometry(null, new Pen(arc, thickness, lineCap: PenLineCap.Round), geometry);
    }
}
