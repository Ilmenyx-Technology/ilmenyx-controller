// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions (replaces the original MessageBox YesNo for every confirmed action): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

/// <summary>Application-owned Yes/No confirmation. Escape and closing the window mean No; nothing proceeds without a click on Yes.</summary>
public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string yes = "Sim", string no = "Não")
    {
        Icon = AppIcon.Window; Title = title; Width = 520; MinWidth = 360; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Panel; Foreground = Palette.Fg;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var text = new TextBlock { Name = "ConfirmText", Text = message, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(text, message);
        var yesButton = new Button { Name = "ConfirmYes", Content = yes, IsDefault = false, Padding = new Thickness(20, 8) };
        var noButton = new Button { Name = "ConfirmNo", Content = no, IsCancel = true, Padding = new Thickness(20, 8) };
        yesButton.Click += (_, _) => Close(true); noButton.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Background = IlmenyxTheme.Ambient(), Margin = new Thickness(24), Spacing = 18,
            Children = { new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontSize = 18, FontWeight = FontWeight.Bold }, text, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Children = { noButton, yesButton } } }
        };
        Opened += (_, _) => noButton.Focus();
    }

    public static async Task<bool> AskAsync(Window owner, string title, string message, string yes = "Sim", string no = "Não") =>
        await new ConfirmDialog(title, message, yes, no).ShowDialog<bool?>(owner) == true;
}
