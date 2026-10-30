// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Ilmenyx.Desktop;

/// <summary>
/// Fluent theme with the Ilmenyx identity (docs/branding/MANUAL-ILMENYX.md): the brand accent for standard controls
/// (check boxes, sliders, focus rings, selection), pill-shaped buttons with a neutral tonal fill (Material 3) and
/// rounded fields. Accent values are the palette's primary role (see Palette.cs).
/// </summary>
public static class IlmenyxTheme
{
    /// <summary>Corner radii of the manual: controls, cards, large containers; buttons are pills.</summary>
    public const double ControlRadius = 10, CardRadius = 20, ContainerRadius = 28, Pill = 20;

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
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(Pill)),
                new Setter(TemplatedControl.BackgroundProperty, Palette.Hover),
                new Setter(TemplatedControl.ForegroundProperty, Palette.Fg),
            }
        });
        foreach (var field in new Func<Selector?, Selector>[] { x => x.OfType<TextBox>(), x => x.OfType<ComboBox>(), x => x.OfType<NumericUpDown>() })
            theme.Add(new Style(field) { Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(ControlRadius)) } });
        return theme;
    }

    /// <summary>Ambient light behind the glass surfaces: the accent fading from the top right corner. Rebuild after a theme switch.</summary>
    public static IBrush Ambient()
    {
        var accent = ((ISolidColorBrush)Palette.Accent).Color;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.45, 0.55, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb((byte)(Palette.IsLight ? 0x1C : 0x26), accent.R, accent.G, accent.B), 0), new GradientStop(Colors.Transparent, 1) }
        };
    }
}
