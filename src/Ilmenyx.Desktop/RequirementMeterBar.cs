// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// Horizontal meter of "Meu PC roda isso?": a bar filled up to this PC's value, a gray tick at the minimum and a white (ink) tick at the recommended.
/// Positions come from <see cref="RequirementMeter.Layout"/>; the colour is the status colour of the item. Decorative: the card carries the accessible text.
/// </summary>
public sealed class RequirementMeterBar : Control
{
    private readonly MeterLayout layout; private readonly IBrush fill;
    public RequirementMeterBar(MeterLayout layout, IBrush fill) { this.layout = layout; this.fill = fill; Height = 22; MinWidth = 120; IsHitTestVisible = false; Focusable = false; }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width; if (w <= 0) return;
        var track = new Rect(0, 7, w, 8);
        context.DrawRectangle(Palette.Hover, null, new RoundedRect(track, 4));
        if (layout.Local > 0) context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 7, Math.Max(8, w * layout.Local), 8), 4));
        void Tick(double? at, IBrush brush) { if (at is { } p) { double x = Math.Clamp(w * p, 1.5, w - 1.5); context.DrawLine(new Pen(brush, 3, lineCap: PenLineCap.Round), new Point(x, 2), new Point(x, 20)); } }
        Tick(layout.Minimum, Palette.Faint);
        Tick(layout.Recommended, Palette.Fg);
    }
}
