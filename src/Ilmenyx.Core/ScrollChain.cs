// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>
/// Chained scrolling of a page with a sticky inner list (store-page style). The page (outer) scrolls first when going down,
/// until the list header reaches the top; then only the list (inner) scrolls. Going up, the list returns to its own top first
/// and only then the page comes back down. Pure arithmetic so it can be tested without a UI.
/// </summary>
public static class ScrollChain
{
    /// <summary>Applies a scroll of <paramref name="delta"/> pixels (positive = down) and returns the new offsets, both clamped.</summary>
    public static (double Outer, double Inner) Step(double outer, double outerMax, double inner, double innerMax, double delta)
    {
        outerMax = Math.Max(0, outerMax); innerMax = Math.Max(0, innerMax);
        outer = Math.Clamp(outer, 0, outerMax); inner = Math.Clamp(inner, 0, innerMax);
        if (double.IsNaN(delta) || delta == 0) return (outer, inner);
        if (delta > 0)
        {
            double page = Math.Min(delta, outerMax - outer);
            return (outer + page, Math.Min(innerMax, inner + (delta - page)));
        }
        double up = -delta, list = Math.Min(up, inner);
        return (Math.Max(0, outer - (up - list)), inner - list);
    }

    /// <summary>Ease-out cubic between 0 and 1 for the smooth "back to top" animation.</summary>
    public static double EaseOut(double t) { t = Math.Clamp(t, 0, 1); return 1 - Math.Pow(1 - t, 3); }
}

/// <summary>WCAG 2.x relative luminance and contrast ratio, with alpha compositing for translucent surfaces.</summary>
public static class ColorContrast
{
    public readonly record struct Rgb(double R, double G, double B);

    /// <summary>Parses #RRGGBB or #AARRGGBB and composites it over <paramref name="under"/> (opaque) when it has alpha.</summary>
    public static Rgb Parse(string hex, Rgb? under = null)
    {
        string h = hex.TrimStart('#');
        if (h.Length is not (6 or 8)) throw new FormatException(L.T("Cor inválida: ") + hex);
        int i = h.Length == 8 ? 2 : 0;
        double a = h.Length == 8 ? Convert.ToInt32(h[..2], 16) / 255.0 : 1;
        double r = Convert.ToInt32(h.Substring(i, 2), 16), g = Convert.ToInt32(h.Substring(i + 2, 2), 16), b = Convert.ToInt32(h.Substring(i + 4, 2), 16);
        var u = under ?? new Rgb(255, 255, 255);
        return a >= 1 ? new Rgb(r, g, b) : new Rgb(r * a + u.R * (1 - a), g * a + u.G * (1 - a), b * a + u.B * (1 - a));
    }

    public static double Luminance(Rgb c)
    {
        static double Lin(double v) { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Ratio(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
