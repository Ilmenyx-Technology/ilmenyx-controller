// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Ambient window tint of the games window: a dark, desaturated colour taken from the selected game's art. Pure math (no UI types):
/// the average colour is darkened and desaturated until every text colour of the theme keeps WCAG AA contrast against it.
/// </summary>
public static class AmbientTint
{
    public const double MinContrast = 4.5, MaxSaturation = 0.32, MinLightness = 0.07, MaxLightness = 0.17;

    /// <summary>Saturation-weighted average of BGRA pixels, sampling every <paramref name="step"/>-th pixel. Transparent pixels are skipped; null when nothing is left.</summary>
    public static Rgb? Average(ReadOnlySpan<byte> bgra, int step = 1)
    {
        double r = 0, g = 0, b = 0, w = 0; int stride = 4 * Math.Max(1, step);
        for (int i = 0; i + 3 < bgra.Length; i += stride)
        {
            if (bgra[i + 3] < 128) continue;
            double pb = bgra[i], pg = bgra[i + 1], pr = bgra[i + 2], max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
            double weight = 0.15 + (max == 0 ? 0 : (max - min) / max); // vivid pixels define the tint; grey ones barely move it
            r += pr * weight; g += pg * weight; b += pb * weight; w += weight;
        }
        return w == 0 ? null : new((byte)Math.Round(r / w), (byte)Math.Round(g / w), (byte)Math.Round(b / w));
    }

    /// <summary>The tint for <paramref name="average"/>: saturation capped, lightness clamped to a dark band, then lowered until every colour in <paramref name="foregrounds"/> reaches <see cref="MinContrast"/>.</summary>
    public static Rgb Tint(Rgb average, IReadOnlyList<Rgb> foregrounds)
    {
        var (h, s, l) = ToHsl(average);
        s = Math.Min(s, MaxSaturation); l = Math.Clamp(l * 0.4, MinLightness, MaxLightness);
        var tint = FromHsl(h, s, l);
        while (l > 0 && foregrounds.Any(f => Contrast(f, tint) < MinContrast)) { l = Math.Max(0, l - 0.01); tint = FromHsl(h, s, l); }
        return tint;
    }

    private static double Channel(byte c) { double v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
    public static double Luminance(Rgb c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    /// <summary>WCAG 2 contrast ratio (1 to 21).</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static (double H, double S, double L) ToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d == 0) return (0, 0, l);
        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == r ? ((g - b) / d % 6) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return ((h * 60 + 360) % 360, s, l);
    }

    private static Rgb FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (double r, double g, double b) = (int)(h / 60) switch { 0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x) };
        byte B(double v) => (byte)Math.Round(Math.Clamp(v + m, 0, 1) * 255);
        return new(B(r), B(g), B(b));
    }
}

/// <summary>"HH:mm" clock of the games window top bar and the wait until the next minute.</summary>
public static class MinuteClock
{
    public static string Text(DateTime now, System.Globalization.CultureInfo culture) => now.ToString("HH:mm", culture);
    /// <summary>Milliseconds to the next minute boundary (at least 50, so the loop never spins).</summary>
    public static int MillisecondsToNextMinute(DateTime now) => Math.Max(50, (int)((60 - now.Second) * 1000 - now.Millisecond));
}
