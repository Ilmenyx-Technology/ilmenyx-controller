// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>
/// Opt-in "CPU too hot" alert. Turbo bursts (a fraction of a second at 70–80 °C on thin laptops) never fire it: the reading must stay
/// at or above the limit for <see cref="Hold"/>. After an alert it waits <see cref="Cooldown"/> and fires again only after the CPU
/// cooled below the limit minus <see cref="Hysteresis"/>.
/// </summary>
public sealed class TemperatureAlert(double limitC)
{
    public static readonly int[] Limits = [80, 85, 90, 95];
    public const int DefaultLimit = 90;
    public const double Hysteresis = 5;
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(20), Cooldown = TimeSpan.FromMinutes(10);

    public double LimitC { get; set; } = limitC;
    /// <summary>Highest reading of the current hot stretch (what the alert reports).</summary>
    public double? Peak { get; private set; }
    private DateTimeOffset? above, firedAt;
    private bool armed = true;

    /// <summary>Feeds one reading; true when an alert should be shown now. A missing reading ends the hot stretch.</summary>
    public bool Feed(DateTimeOffset now, double? tempC)
    {
        if (tempC is not { } t) { above = null; return false; }
        if (t < LimitC - Hysteresis) armed = true;
        if (t < LimitC) { above = null; return false; }
        if (above is null) { above = now; Peak = t; } else Peak = Math.Max(Peak ?? t, t);
        if (!armed || now - above < Hold || firedAt is { } f && now - f < Cooldown) return false;
        firedAt = now; armed = false;
        return true;
    }
}
