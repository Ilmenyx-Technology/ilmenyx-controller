// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>"Why is this reading missing?": the reason and the command to fix it. Commands are copied, never executed.</summary>
public sealed class SensorHelpDialog : Window
{
    public SensorHelpDialog(IReadOnlyList<SensorFix> fixes)
    {
        Icon = AppIcon.Window; Title = "Ilmenyx — Sensores da CPU"; Width = 640; Height = 520; MinWidth = 460; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Palette.Panel;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var list = new StackPanel { Spacing = 12 };
        list.Children.Add(UiKit.PageHeader("Leituras da CPU", "O Ilmenyx não usa root. Quando o Linux esconde um sensor, ele mostra o motivo e o comando; você decide se roda."));
        if (fixes.Count == 0) list.Children.Add(new TextBlock { Text = "Temperatura e consumo da CPU estão disponíveis.", Foreground = Palette.Muted });
        int i = 0;
        foreach (var fix in fixes)
        {
            var body = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = fix.Title, FontSize = 14, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = fix.Why, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } };
            foreach (var (label, command) in fix.Commands)
            {
                int index = i++;
                var status = new TextBlock { FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center };
                var copy = new Button { Name = "SensorFixCopy" + index, Content = "Copiar", FontSize = 11, Padding = new Thickness(10, 4) };
                copy.Click += async (_, _) => { if (GetTopLevel(this)?.Clipboard is { } c) { await c.SetTextAsync(command); status.Text = "Copiado. Cole no terminal."; } };
                body.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Palette.Fg, Margin = new Thickness(0, 4, 0, 0) });
                body.Children.Add(new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 8),
                    Child = new SelectableTextBlock { Name = "SensorFixCommand" + index, Text = command, FontFamily = IlmenyxTheme.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap } });
                body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copy, status } });
            }
            if (fix.Warning is { } w) body.Children.Add(new Border { Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 8), Child = new TextBlock { Text = w, Foreground = Palette.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap } });
            list.Children.Add(UiKit.Card(body, new Thickness(0)));
        }
        list.Children.Add(new TextBlock { Text = "Depois de rodar um comando, as leituras aparecem sozinhas em poucos segundos (sem reiniciar o Ilmenyx).", FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        var close = new Button { Name = "CloseSensorHelp", Content = "Fechar", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(20, 8) };
        close.Click += (_, _) => Close();
        var dock = new DockPanel { Margin = new Thickness(22, 18) };
        DockPanel.SetDock(close, Dock.Bottom); dock.Children.Add(close);
        dock.Children.Add(new ScrollViewer { Content = list, Margin = new Thickness(0, 0, 0, 12) });
        Content = dock;
        Opened += (_, _) => close.Focus();
    }
}
