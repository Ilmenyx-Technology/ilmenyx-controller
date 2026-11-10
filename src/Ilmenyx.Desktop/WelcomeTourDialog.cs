// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// First run (after the permissions screen, if any): offers the optional guided tour once, like the welcome tours of GNOME/Steam.
/// Enter starts the tour, Esc or "Agora não" closes. Every text comes from <see cref="HelpContent"/>.
/// </summary>
public sealed class WelcomeTourDialog : Window
{
    /// <summary>True when the user chose to take the tour.</summary>
    public bool StartTour { get; private set; }

    public WelcomeTourDialog(HelpLanguage text)
    {
        Icon = AppIcon.Window; Title = text[HelpKeys.WelcomeWindow]; Width = 520; MinWidth = 420; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Palette.Panel; Foreground = Palette.Fg;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 12) };
        if (AppIcon.Symbol is { } symbol) head.Children.Add(new Image { Source = symbol, Height = 34, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Name = "WelcomeTitle", Text = text[HelpKeys.WelcomeTitle], FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var body = new TextBlock { Name = "WelcomeBody", Text = text[HelpKeys.WelcomeBody], FontSize = 13.5, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        var note = new TextBlock { Text = text[HelpKeys.WelcomeNote], FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        var start = UiKit.Primary("WelcomeStart", text[HelpKeys.WelcomeStart], 20, 9); start.IsDefault = true;
        var later = new Button { Name = "WelcomeLater", Content = text[HelpKeys.WelcomeLater], IsCancel = true, Padding = new Thickness(18, 9) };
        start.Click += (_, _) => { StartTour = true; Close(); };
        later.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0), Children = { later, start } };
        Content = new StackPanel { Margin = new Thickness(26, 22), Children = { head, body, note, buttons } };
        AutomationProperties.SetName(this, text[HelpKeys.WelcomeTitle]);
        Opened += (_, _) => start.Focus(NavigationMethod.Tab);
    }
}
