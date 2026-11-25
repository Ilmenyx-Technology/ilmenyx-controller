// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>Positions (0..1 of the bar) of this PC and of the minimum / recommended ticks on a requirement meter. A null tick is not drawn.</summary>
public sealed record MeterLayout(double Local, double? Minimum, double? Recommended);

/// <summary>
/// Pure math of the "Meu PC roda isso?" meters. All values share one unit (hardware index, GB…). The bar spans 0 to the largest known value
/// plus 15% headroom, so the PC and the ticks fit and are never clamped away from each other; null when this PC's value is unknown (no meter is drawn).
/// </summary>
public static class RequirementMeter
{
    public const double Headroom = 1.15;

    public static MeterLayout? Layout(double? local, double? minimum, double? recommended)
    {
        if (local is not { } l || !double.IsFinite(l) || l < 0) return null;
        double? min = Valid(minimum), rec = Valid(recommended);
        if (min is null && rec is null) return null;
        double span = Math.Max(l, Math.Max(min ?? 0, rec ?? 0)) * Headroom;
        if (span <= 0) return null;
        double At(double v) => Math.Clamp(v / span, 0, 1);
        return new(At(l), min is { } a ? At(a) : null, rec is { } b ? At(b) : null);
    }

    private static double? Valid(double? v) => v is { } x && double.IsFinite(x) && x > 0 ? x : null;
}
