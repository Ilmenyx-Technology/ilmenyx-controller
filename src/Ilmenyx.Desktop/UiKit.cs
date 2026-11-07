// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// New Ilmenyx additions: shared builders of the cinematic ambient dark interface (2026-11-06). See LICENSING.md.
// Every page builds its surfaces from here, so tokens (Palette), radii and motion (IlmenyxTheme) stay consistent.
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

public static class UiKit
{
    // ---- Typography (compact hierarchy: page title 22, section 13, body 13, secondary 12, labels 11) ----
    public static TextBlock Label(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, LetterSpacing = 0.6 };

    /// <summary>Page title (22 px, bold) with an optional subtitle and trailing controls on the right.</summary>
    public static Grid PageHeader(string title, string? subtitle = null, Control? trailing = null, string? name = null)
    {
        var text = new StackPanel { Spacing = 3, Children = { new TextBlock { Name = name, Text = title, FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap } } };
        if (subtitle is not null) text.Children.Add(new TextBlock { Text = subtitle, FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        var g = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 2, 0, 16), Children = { text } };
        if (trailing is not null) { trailing.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetColumn(trailing, 1); g.Children.Add(trailing); }
        return g;
    }

    /// <summary>Section heading ("Special offers" in the reference): 14 px semibold, optional action on the right.</summary>
    public static Grid SectionHeader(string title, Control? action = null)
    {
        var g = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(2, 18, 2, 10), Children = { new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold } } };
        if (action is not null) { Grid.SetColumn(action, 1); g.Children.Add(action); }
        return g;
    }

    // ---- Surfaces ----
    /// <summary>Subtle translucent surface: tonal difference instead of strong borders.</summary>
    public static Border Card(Control child, Thickness? margin = null) => new()
    {
        Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius),
        Padding = new Thickness(20, 16), Margin = margin ?? new Thickness(0, 0, 0, 12), Child = child,
    };

    /// <summary>
    /// Cinematic banner: a deep gradient (or an image when one is given) with a soft legibility overlay and content on top.
    /// Used as the dominant element of a page. No visible border.
    /// </summary>
    /// <param name="height">Fixed height; <see cref="double.NaN"/> sizes to the content (use MinHeight on the result).</param>
    public static Border Hero(Control content, IBrush? background = null, IImage? image = null, double height = 250, string? name = null)
    {
        var layers = new Grid { ClipToBounds = true };
        if (image is not null) layers.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var overlay = new Border { Background = Overlay(image is not null), IsHitTestVisible = false };
        layers.Children.Add(overlay);
        content.Margin = new Thickness(26, 22); layers.Children.Add(content);
        var hero = new Border { Name = name, Height = height, CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), ClipToBounds = true, Background = background ?? HeroBrush(), BoxShadow = IlmenyxTheme.SoftShadow, Child = layers };
        // Repaint on a theme switch (brushes are built per theme); a background given by the caller stays the caller's.
        hero.ActualThemeVariantChanged += (_, _) =>
        {
            if (background is null) hero.Background = HeroBrush();
            overlay.Background = Overlay(image is not null); hero.BoxShadow = IlmenyxTheme.SoftShadow;
        };
        return hero;
    }

    /// <summary>Deep blue-to-wine banner fill in the brand accent (light: pale blue to blush), for heroes without imagery.</summary>
    public static IBrush HeroBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        foreach (var (c, o) in Palette.IsLight ? new[] { ("#DCE6FD", 0.0), ("#E9E6F6", 0.5), ("#F3E2E8", 1.0) } : new[] { ("#14284F", 0.0), ("#1B1E3A", 0.5), ("#3A1424", 1.0) })
            b.GradientStops.Add(new GradientStop(Color.Parse(c), o));
        return b;
    }

    /// <summary>Legibility overlay: darker at the left (and bottom on images), never flattening the picture. None on light fills.</summary>
    private static IBrush Overlay(bool image)
    {
        byte left = (byte)(image ? 0x70 : Palette.IsLight ? 0x00 : 0x40);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(left, 0, 0, 0), 0), new GradientStop(Colors.Transparent, 0.62) },
        };
    }

    /// <summary>
    /// Secondary highlight card: warm amber → wine → brand blue glow (the reference's gradient card, adapted to the Ilmenyx accent).
    /// Text inside should use Palette.Fg/Muted: the card is dark in the dark theme and pale in the light theme, and follows a theme switch.
    /// </summary>
    public static Border FeatureCard(Control content, string? name = null)
    {
        var glowLayer = new Border { IsHitTestVisible = false };
        content.Margin = new Thickness(20, 18);
        var card = new Border { Name = name, CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), ClipToBounds = true, Child = new Grid { Children = { glowLayer, content } } };
        void Paint()
        {
            var a = ((ISolidColorBrush)Palette.Accent).Color;
            var fill = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            foreach (var (c, o) in Palette.IsLight ? new[] { ("#FFF4E9", 0.0), ("#FCE4E2", 0.55), ("#E6ECFD", 1.0) } : new[] { ("#7A6233", 0.0), ("#5E2A2E", 0.45), ("#2B1B3F", 1.0) })
                fill.GradientStops.Add(new GradientStop(Color.Parse(c), o));
            glowLayer.Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.92, 0.78, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.92, 0.78, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb((byte)(Palette.IsLight ? 0x2C : 0x99), a.R, a.G, a.B), 0), new GradientStop(Color.FromArgb(0, a.R, a.G, a.B), 1) },
            };
            card.Background = fill; card.BoxShadow = IlmenyxTheme.SoftShadow;
        }
        Paint();
        card.ActualThemeVariantChanged += (_, _) => Paint();
        return card;
    }

    /// <summary>Compact metric tile: label, value and a short note (the reference's small thumbnails, as live readings).</summary>
    public static Border StatTile(string label, TextBlock value, TextBlock? note = null, string? icon = null, IBrush? tint = null)
    {
        value.FontSize = value.FontSize is 0 or 13 or 14 ? 20 : value.FontSize; value.FontWeight = FontWeight.SemiBold;
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon is not null) head.Children.Add(Icon(icon, 14, tint ?? Palette.Faint));
        head.Children.Add(Label(label));
        var stack = new StackPanel { Spacing = 4, Children = { head, value } };
        if (note is not null) { note.FontSize = 11.5; note.Foreground ??= Palette.Faint; note.TextWrapping = TextWrapping.Wrap; stack.Children.Add(note); }
        return Card(stack, new Thickness(0));
    }

    // ---- Controls ----
    public static Button Primary(string name, string text, double padX = 18, double padY = 8) => new()
    {
        Name = name, Content = text, Background = Palette.Accent, Foreground = Palette.OnAccent, FontWeight = FontWeight.SemiBold, FontSize = 12.5,
        Padding = new Thickness(padX, padY), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius),
    };

    /// <summary>
    /// Action over heroes and gradient cards (the reference's "Available now"): dark translucent in the dark theme,
    /// solid accent in the light theme (as in the white-theme reference). Follows a theme switch.
    /// </summary>
    public static Button Translucent(string name, string text)
    {
        var b = new Button { Name = name, Content = text, FontWeight = FontWeight.SemiBold, Padding = new Thickness(20, 9), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius) };
        void Paint()
        {
            b.Background = Palette.IsLight ? Palette.Accent : new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x0E, 0x14));
            b.Foreground = Palette.IsLight ? Palette.OnAccent : Brushes.White;
        }
        Paint(); b.ActualThemeVariantChanged += (_, _) => Paint();
        return b;
    }

    /// <summary>Round icon action of the header (bell, theme, pause). The accessible name is the action, the icon is decoration.</summary>
    public static Button IconButton(string name, string icon, string accessibleName, double size = 36)
    {
        var b = new Button
        {
            Name = name, Width = size, Height = size, Padding = new Thickness(0), CornerRadius = new CornerRadius(size / 2), Background = Palette.Field,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Content = Icon(icon, 17, Palette.Muted),
        };
        SetAction(b, icon, accessibleName);
        return b;
    }

    public static void SetAction(Button b, string icon, string accessibleName)
    {
        if (b.Content is Path p) p.Data = Geometry.Parse(icon); else b.Content = Icon(icon, 17, Palette.Muted);
        Avalonia.Automation.AutomationProperties.SetName(b, accessibleName); ToolTip.SetTip(b, accessibleName); b.Tag = accessibleName;
    }

    /// <summary>Vector icon from <see cref="Icons"/> (24×24 viewbox), scaled to <paramref name="size"/>.</summary>
    public static Path Icon(string data, double size = 16, IBrush? brush = null) => new()
    {
        Data = Geometry.Parse(data), Fill = brush ?? Palette.Muted, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Small capsule (categories, metadata): dark translucent fill, extremely subtle border.</summary>
    public static Border Tag(string text, IBrush? foreground = null, string? name = null, bool onImage = false) => new()
    {
        Name = name, Background = onImage ? new SolidColorBrush(Color.FromArgb(0x66, 0x14, 0x12, 0x18)) : Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(11, 4), Margin = new Thickness(0, 0, 6, 6), HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.Medium, Foreground = foreground ?? (onImage ? Brushes.White : Palette.Muted) },
    };

    public static void ChipStyle(Button b, bool on) { b.Background = on ? Palette.Fg : Palette.Card; b.Foreground = on ? Palette.OnFg : Palette.Muted; }

    /// <summary>Exclusive chip group. <see cref="Select"/> changes the visual selection without raising <paramref name="chosen"/> (used when loading a profile).</summary>
    public sealed class ChipGroup<T>
    {
        private readonly List<(Button Button, T Value)> items = [];
        public StackPanel Panel { get; }
        public ChipGroup(string name, IEnumerable<(string Label, T Value)> options, T initial, Action<T> chosen)
        {
            Panel = new StackPanel { Name = name, Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (label, value) in options)
            {
                var b = new Button { Content = label, FontSize = 11, Padding = new Thickness(13, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
                ChipStyle(b, EqualityComparer<T>.Default.Equals(value, initial)); items.Add((b, value));
                var v = value; b.Click += (_, _) => { Select(v); chosen(v); };
                Panel.Children.Add(b);
            }
        }
        public void Select(T value) { foreach (var (b, v) in items) ChipStyle(b, EqualityComparer<T>.Default.Equals(v, value)); }
    }
}
