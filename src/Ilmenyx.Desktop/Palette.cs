// New Ilmenyx additions: see LICENSING.md. Provisional palette from the user's visual reference; not an official brand palette.
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Ilmenyx.Desktop;

/// <summary>
/// Shared brushes. They are mutable <see cref="SolidColorBrush"/> instances, so switching the theme recolours every existing control at once.
/// Fg is the "ink" colour (light on dark, dark on light); wherever Fg is a background (inverted chips/badges) the text on it must be OnFg.
/// </summary>
public static class Palette
{
    private static readonly (string Dark, string Light)[] Colors =
    [
        ("#17131C", "#FAF7FD"), ("#1F1A26", "#F1ECF6"), ("#2A2531", "#FFFFFF"), ("#352F3E", "#EBE4F2"), ("#3D3646", "#D8CFE3"),
        ("#F3EEF8", "#1D1824"), ("#B9B2C4", "#5A5166"), ("#7C7488", "#7C7488"), ("#D9A8FF", "#D9A8FF"), ("#F3CE85", "#8A5A00"),
        ("#000000", "#FFFFFF"), ("#D9A8FF", "#6B2FB0"), ("#3A3020", "#FFF1CC")
    ];
    private static readonly SolidColorBrush[] Brushes_ = Colors.Select(c => new SolidColorBrush(Color.Parse(c.Dark))).ToArray();

    public static readonly IBrush Bg = Brushes_[0], Panel = Brushes_[1], Card = Brushes_[2], Hover = Brushes_[3], Line = Brushes_[4], Fg = Brushes_[5],
        Muted = Brushes_[6], Faint = Brushes_[7], Accent = Brushes_[8], Warn = Brushes_[9], OnFg = Brushes_[10], AccentText = Brushes_[11], WarnBg = Brushes_[12];

    public static bool IsLight { get; private set; }

    /// <summary>Switches every shared brush and the Fluent theme variant (standard controls, scroll bars, text boxes).</summary>
    public static void Apply(bool light)
    {
        IsLight = light;
        for (int i = 0; i < Colors.Length; i++) Brushes_[i].Color = Color.Parse(light ? Colors[i].Light : Colors[i].Dark);
        if (Application.Current is { } app) app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
