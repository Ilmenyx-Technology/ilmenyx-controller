// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (the original toast sits at the bottom-right and never takes focus): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ilmenyx.Desktop;

/// <summary>Small notice in the bottom-right corner that hides itself after 5 s. Uses the shared theme tokens and is never activated.</summary>
public sealed class ToastWindow : Window
{
    private readonly DispatcherTimer hide = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly TextBlock title = new() { Name = "ToastTitle", FontWeight = FontWeight.SemiBold, Foreground = Palette.Fg, FontSize = 13 };
    private readonly TextBlock text = new() { Name = "ToastText", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
    public string TitleText => title.Text ?? "";
    public string BodyText => text.Text ?? "";

    public ToastWindow()
    {
        Icon = AppIcon.Window; Title = "Ilmenyx Controller · Aviso"; WindowDecorations = WindowDecorations.None; Topmost = true; ShowInTaskbar = false; ShowActivated = false; CanResize = false; Focusable = false;
        Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border { Background = Palette.Panel, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(16, 12), Child = new StackPanel { Spacing = 2, Children = { title, text } } };
        SizeChanged += (_, _) => Place();
        hide.Tick += (_, _) => { hide.Stop(); Hide(); };
        Closed += (_, _) => hide.Stop();
    }

    public void Notify(string heading, string body)
    {
        title.Text = heading; text.Text = body;
        if (!IsVisible) Show();
        Place(); hide.Stop(); hide.Start();
    }

    private void Place()
    {
        if (Screens.Primary?.WorkingArea is not { } area || !IsVisible) return;
        double s = Screens.Primary.Scaling;
        Position = new PixelPoint(area.Right - (int)(Bounds.Width * s) - 16, area.Bottom - (int)(Bounds.Height * s) - 16);
    }
}
