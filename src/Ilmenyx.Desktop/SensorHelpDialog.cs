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
    /// <param name="run">Runs a fix's fixed script as root (polkit). Null: only the commands are shown.</param>
    public SensorHelpDialog(IReadOnlyList<SensorFix> fixes, Func<string, Task<(bool Ok, string Message)>>? run = null)
    {
        Icon = AppIcon.Window; Title = "Ilmenyx — Sensores da CPU"; Width = 640; Height = 560; MinWidth = 460; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Palette.Panel;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var list = new StackPanel { Spacing = 12 };
        list.Children.Add(UiKit.PageHeader("Leituras da CPU", "O Linux esconde alguns sensores do usuário comum. Um clique resolve: o sistema pede a sua senha (o Ilmenyx nunca a vê)."));
        if (fixes.Count == 0) list.Children.Add(new TextBlock { Text = "Temperatura e consumo da CPU estão disponíveis.", Foreground = Palette.Muted });
        int i = 0, f = 0;
        foreach (var fix in fixes)
        {
            int fixIndex = f++;
            var body = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = fix.Title, FontSize = 14, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = fix.Why, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } };
            if (fix.Apply is { } script && run is not null)
            {
                var applyStatus = new TextBlock { Name = "SensorFixStatus" + fixIndex, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                var apply = UiKit.Primary("SensorFixApply" + fixIndex, fix.ApplyLabel ?? "CORRIGIR");
                var undo = new Button { Name = "SensorFixUndo" + fixIndex, Content = "Desfazer", FontSize = 12, Padding = new Thickness(14, 7), IsVisible = false };
                apply.Click += async (_, _) =>
                {
                    if (!await ConfirmDialog.AskAsync(this, fix.Title, $"{fix.ApplyLabel ?? "Corrigir"}?\n\nO sistema vai abrir a janela dele pedindo a sua senha de administrador. Depois disso o Ilmenyx aplica a correção e ela continua valendo depois de reiniciar o PC. Dá para desfazer aqui." + (fix.Warning is { } wn ? "\n\n" + wn : ""), "Continuar", "Cancelar")) return;
                    apply.IsEnabled = false; applyStatus.Text = "Aguardando a sua senha na janela do sistema…";
                    var (ok, message) = await run(script);
                    applyStatus.Text = ok ? "Pronto. A leitura aparece em poucos segundos, sem reiniciar o Ilmenyx." : message;
                    applyStatus.Foreground = ok ? Palette.Success : Palette.Warn;
                    apply.IsEnabled = !ok; undo.IsVisible = ok && fix.Undo is not null;
                };
                undo.Click += async (_, _) =>
                {
                    undo.IsEnabled = false; applyStatus.Text = "Aguardando a sua senha na janela do sistema…";
                    var (ok, message) = await run(fix.Undo!);
                    applyStatus.Text = ok ? "Desfeito. Volta ao padrão do Linux." : message; applyStatus.Foreground = ok ? Palette.Muted : Palette.Warn;
                    undo.IsEnabled = true; undo.IsVisible = !ok; apply.IsEnabled = ok;
                };
                body.Children.Add(new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { apply, undo, applyStatus } });
            }
            var commands = new StackPanel { Spacing = 8, IsVisible = run is null || fix.Apply is null };
            if (fix.Commands.Count > 0 && run is not null && fix.Apply is not null)
            {
                var toggle = new Button { Name = "SensorFixShowCommands" + fixIndex, Content = "Ver comandos (para técnicos)", FontSize = 11, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 4) };
                toggle.Click += (_, _) => commands.IsVisible = !commands.IsVisible;
                body.Children.Add(toggle);
            }
            body.Children.Add(commands);
            foreach (var (label, command) in fix.Commands)
            {
                int index = i++;
                var status = new TextBlock { FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center };
                var copy = new Button { Name = "SensorFixCopy" + index, Content = "Copiar", FontSize = 11, Padding = new Thickness(10, 4) };
                copy.Click += async (_, _) => { if (GetTopLevel(this)?.Clipboard is { } c) { await c.SetTextAsync(command); status.Text = "Copiado. Cole no terminal."; } };
                commands.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Palette.Fg, Margin = new Thickness(0, 4, 0, 0) });
                commands.Children.Add(new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 8),
                    Child = new SelectableTextBlock { Name = "SensorFixCommand" + index, Text = command, FontFamily = IlmenyxTheme.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap } });
                commands.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copy, status } });
            }
            if (fix.Warning is { } w) body.Children.Add(new Border { Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 8), Child = new TextBlock { Text = w, Foreground = Palette.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap } });
            list.Children.Add(UiKit.Card(body, new Thickness(0)));
        }
        list.Children.Add(new TextBlock { Text = "Depois da correção, as leituras aparecem sozinhas em poucos segundos (sem reiniciar o Ilmenyx).", FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        var close = new Button { Name = "CloseSensorHelp", Content = "Fechar", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(20, 8) };
        close.Click += (_, _) => Close();
        var dock = new DockPanel { Margin = new Thickness(22, 18) };
        DockPanel.SetDock(close, Dock.Bottom); dock.Children.Add(close);
        dock.Children.Add(new ScrollViewer { Content = list, Margin = new Thickness(0, 0, 0, 12) });
        Content = dock;
        Opened += (_, _) => close.Focus();
    }
}
