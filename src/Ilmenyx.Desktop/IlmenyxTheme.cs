// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Ilmenyx.Desktop;

/// <summary>
/// Fluent theme with the Ilmenyx identity: the brand accent for standard controls (check boxes, sliders, focus rings, selection),
/// compact rounded controls and short eased transitions. Since 2026-11-06 the surfaces follow a cinematic ambient dark direction
/// (maintainer decision): translucent surfaces over diffuse warm (wine) and cool (blue) light, almost invisible borders, Inter.
/// </summary>
public static class IlmenyxTheme
{
    /// <summary>Corner radii: controls and fields, cards, large containers; chips are pills.</summary>
    public const double ControlRadius = 9, CardRadius = 14, ContainerRadius = 18, Pill = 999;
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(160), Normal = TimeSpan.FromMilliseconds(220);
    public static readonly Easing Premium = new SplineEasing(0.22, 1, 0.36, 1);
    public static readonly FontFamily Mono = new("monospace");

    public static FluentTheme Create()
    {
        var theme = new FluentTheme();
        theme.Palettes[ThemeVariant.Light] = new ColorPaletteResources { Accent = Color.Parse("#255EE9") };
        theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse("#A4C1FB") };
        // Style setters outrank the Fluent control themes, but local values (accent buttons, chips) still win.
        theme.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(ControlRadius)),
                new Setter(TemplatedControl.BackgroundProperty, Palette.Hover),
                new Setter(TemplatedControl.ForegroundProperty, Palette.Fg),
                new Setter(Animatable.TransitionsProperty, new Transitions
                {
                    new BrushTransition { Property = TemplatedControl.BackgroundProperty, Duration = Fast, Easing = Premium },
                    new BrushTransition { Property = TemplatedControl.ForegroundProperty, Duration = Fast, Easing = Premium },
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = Fast, Easing = Premium },
                }),
            }
        });
        foreach (var field in new Func<Selector?, Selector>[] { x => x.OfType<TextBox>(), x => x.OfType<ComboBox>(), x => x.OfType<NumericUpDown>(), x => x.OfType<AutoCompleteBox>() })
            theme.Add(new Style(field) { Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(ControlRadius)) } });
        // Translucent fields with an almost invisible border, as in the reference search box.
        theme.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, Palette.Field), new Setter(TemplatedControl.BorderBrushProperty, Palette.Line) }
        });
        return theme;
    }

    /// <summary>
    /// Ambient light behind every surface: a dark diagonal base, a wide wine glow at the top centre and a faint blue glow at the bottom right.
    /// Diffuse, low-opacity layers (no visible edges). Rebuild after a theme switch.
    /// </summary>
    public static IReadOnlyList<Control> AmbientLayers()
    {
        bool light = Palette.IsLight;
        var accent = ((ISolidColorBrush)Palette.Accent).Color;
        var basis = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        foreach (var (c, o) in light ? new[] { ("#F6F6F9", 0.0), ("#F4F5F8", 0.5), ("#F0F2F7", 1.0) } : new[] { ("#19171C", 0.0), ("#15171D", 0.48), ("#101922", 1.0) })
            basis.GradientStops.Add(new GradientStop(Color.Parse(c), o));
        RadialGradientBrush Glow(double x, double y, double rx, double ry, Color color) => new()
        {
            Center = new RelativePoint(x, y, RelativeUnit.Relative), GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(rx, RelativeUnit.Relative), RadiusY = new RelativeScalar(ry, RelativeUnit.Relative),
            GradientStops = { new GradientStop(color, 0), new GradientStop(Color.FromArgb((byte)(color.A * 0.45), color.R, color.G, color.B), 0.38), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) },
        };
        // Light: the white reference has almost no ambient colour, only a hint of warmth and of the accent.
        var warm = light ? Color.FromArgb(0x14, 0xF2, 0xB8, 0xA8) : Color.FromArgb(0x4A, 0x6F, 0x1C, 0x2A);
        var cool = light ? Color.FromArgb(0x10, accent.R, accent.G, accent.B) : Color.FromArgb(0x4D, 0x1E, 0x31, 0x4D);
        return
        [
            new Border { Name = "AmbientBase", Background = basis, IsHitTestVisible = false },
            new Border { Name = "AmbientWarm", Background = Glow(0.47, 0.12, 0.62, 0.70, warm), IsHitTestVisible = false },
            new Border { Name = "AmbientCool", Background = Glow(0.95, 1.0, 0.62, 0.78, cool), IsHitTestVisible = false },
        ];
    }

    /// <summary>Single-brush ambient for secondary windows (dialogs, classic diagnostics).</summary>
    public static IBrush Ambient()
    {
        var accent = ((ISolidColorBrush)Palette.Accent).Color;
        var b = new LinearGradientBrush { StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative) };
        if (Palette.IsLight) { b.GradientStops.Add(new GradientStop(Color.Parse("#FBF8F9"), 0)); b.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, accent.R, accent.G, accent.B), 1)); }
        else { b.GradientStops.Add(new GradientStop(Color.Parse("#1D161C"), 0)); b.GradientStops.Add(new GradientStop(Color.Parse("#15171D"), 0.55)); b.GradientStops.Add(new GradientStop(Color.Parse("#111822"), 1)); }
        return b;
    }

    /// <summary>Soft, wide shadow for raised surfaces (hero, featured cards). Never hard or coloured.</summary>
    public static BoxShadows SoftShadow => new(new BoxShadow { OffsetY = 18, Blur = 48, Color = Color.FromArgb((byte)(Palette.IsLight ? 0x1A : 0x38), 0, 0, 0) });
}
