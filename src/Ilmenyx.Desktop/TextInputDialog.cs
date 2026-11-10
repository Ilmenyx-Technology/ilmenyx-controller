// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

/// <summary>Asks for one line of text (e.g. a game's name). Escape or closing the window returns null; empty text is not accepted.</summary>
public sealed class TextInputDialog : Window
{
    public TextInputDialog(string title, string label, string initial, int maxLength = 80, string ok = "Salvar")
    {
        Icon = AppIcon.Window; Title = title; Width = 460; MinWidth = 340; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Panel; Foreground = Palette.Fg;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var box = new TextBox { Name = "InputText", Text = initial, MaxLength = maxLength };
        AutomationProperties.SetName(box, label);
        var okButton = new Button { Name = "InputOk", Content = ok, IsDefault = true, Padding = new Thickness(20, 8) };
        var cancel = new Button { Name = "InputCancel", Content = "Cancelar", IsCancel = true, Padding = new Thickness(20, 8) };
        void Refresh() => okButton.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        box.TextChanged += (_, _) => Refresh(); Refresh();
        okButton.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(box.Text)) Close(box.Text.Trim()); };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Background = IlmenyxTheme.Ambient(), Margin = new Thickness(24), Spacing = 12,
            Children = { new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold }, new TextBlock { Text = label, Foreground = Palette.Muted, FontSize = 12 }, box,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Children = { cancel, okButton } } },
        };
        Opened += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    public static Task<string?> AskAsync(Window owner, string title, string label, string initial) =>
        new TextInputDialog(title, label, initial).ShowDialog<string?>(owner);
}
