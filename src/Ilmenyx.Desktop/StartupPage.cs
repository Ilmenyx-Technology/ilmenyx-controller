// New Ilmenyx additions (layout follows the original Startup page: summary, restore button, toggle list with remove): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class StartupPage : StackPanel
{
    private readonly LinuxStartupManager manager;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly StackPanel list = new() { Name = "StartupList" };
    private readonly TextBlock summary = new() { Name = "StartupSummary", Text = "PROGRAMAS DE INICIALIZAÇÃO", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint };
    private readonly TextBlock result = new() { Name = "StartupResult", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button restore = new() { Name = "BtnStartupRestore", Content = "RESTAURAR REMOVIDOS", Padding = new Thickness(10, 4), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    public IReadOnlyList<StartupEntry> Entries { get; private set; } = [];

    public StartupPage(LinuxStartupManager manager, Func<Window?> owner, CancellationToken lifetime)
    {
        this.manager = manager; this.owner = owner; this.lifetime = lifetime; Spacing = 4;
        var scan = new Button { Name = "BtnStartupScan", Content = "ATUALIZAR", Background = Palette.Accent, Foreground = Palette.OnAccent, FontWeight = FontWeight.Bold, Padding = new Thickness(20, 9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        scan.Click += async (_, _) => { result.Text = ""; await ScanAsync(); };
        var header = UiKit.PageHeader("Inicialização", "Programas que abrem junto com a sua sessão · desative ou remova o que não precisa");
        header.RowDefinitions = new("Auto,Auto");
        Grid.SetRow(scan, 1); scan.Margin = new Thickness(0, 10, 0, 0);
        header.Children.Add(scan);
        restore.Click += async (_, _) => await RestoreAsync();
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 10), Children = { summary, restore } };
        Children.Add(header);
        Children.Add(new Border
        {
            Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(22, 18),
            Child = new StackPanel
            {
                Children =
                {
                    top,
                    new TextBlock { Text = "O interruptor ativa ou desativa na hora (a entrada continua lá e pode ser religada). Entradas do sistema (/etc/xdg/autostart) são desativadas por um arquivo seu em ~/.config/autostart, sem privilégios, e o arquivo do sistema não é alterado. REMOVER vale só para entradas suas: o arquivo vai para um backup e dá para desfazer em \"Restaurar removidos\". Vale na próxima vez que a sessão iniciar.", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) },
                    list, result
                }
            }
        });
        Loaded += async (_, _) => { if (Entries.Count == 0) await ScanAsync(); };
    }

    public async Task ScanAsync()
    {
        try
        {
            Entries = await manager.ListAsync(lifetime);
            int on = Entries.Count(e => e.Enabled);
            summary.Text = Entries.Count == 0 ? "NENHUM PROGRAMA NA INICIALIZAÇÃO" : $"{Entries.Count} PROGRAMAS · {on} ATIVADOS · {Entries.Count - on} DESATIVADOS";
            int removed = manager.Removed().Count; restore.IsEnabled = removed > 0; restore.Content = removed > 0 ? $"RESTAURAR REMOVIDOS ({removed})" : "RESTAURAR REMOVIDOS";
            list.Children.Clear();
            foreach (var e in Entries) list.Children.Add(Row(e));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result.Text = "Não foi possível listar: " + ex.Message; }
    }

    private Control Row(StartupEntry e)
    {
        var toggle = new CheckBox { Name = "StartupToggle_" + e.Id, IsChecked = e.Enabled, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        AutomationProperties.SetName(toggle, "Ativar ou desativar " + e.Name);
        toggle.Click += async (_, _) =>
        {
            bool on = toggle.IsChecked == true;
            try { await manager.SetEnabledAsync(e, on, lifetime); result.Text = $"{e.Name}: {(on ? "ativado" : "desativado")}. Vale na próxima inicialização da sessão."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException or System.ComponentModel.Win32Exception) { result.Text = $"{e.Name}: falhou ({ex.Message})."; }
            await ScanAsync();
        };
        var head = new Grid(); head.Children.Add(new TextBlock { Text = e.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 150, 0) });
        head.Children.Add(new TextBlock { Name = "StartupState_" + e.Id, Text = e.State, FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
        var command = new TextBlock { Text = e.Command, Foreground = Palette.Faint, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }; ToolTip.SetTip(command, e.Command);
        var info = new StackPanel { Children = { head } };
        if (e.Detail.Length > 0) info.Children.Add(new TextBlock { Text = e.Detail, Foreground = Palette.Muted, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        info.Children.Add(command); info.Children.Add(new TextBlock { Text = e.Source, Foreground = Palette.Faint, FontSize = 10.5, Margin = new Thickness(0, 1, 0, 0) });
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto") }; Grid.SetColumn(info, 1);
        grid.Children.Add(toggle); grid.Children.Add(info);
        if (e is { Kind: "Desktop", System: false })
        {
            var remove = new Button { Name = "StartupRemove_" + e.Id, Content = "REMOVER", Padding = new Thickness(12, 5), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            remove.Click += async (_, _) => await RemoveAsync(e);
            Grid.SetColumn(remove, 2); grid.Children.Add(remove);
        }
        return UiKit.Card(grid, new Thickness(0, 0, 0, 8));
    }

    private async Task RemoveAsync(StartupEntry e)
    {
        if (owner() is not { } window) return;
        if (!await ConfirmDialog.AskAsync(window, "Inicialização", $"Remover \"{e.Name}\" da inicialização da sessão?\n\n{e.Command}\n\nO programa continua instalado, só deixa de abrir sozinho. Dá para desfazer em \"Restaurar removidos\".\n\nSe só quiser testar, prefira desligar o interruptor.")) return;
        try { manager.Remove(e); result.Text = $"{e.Name} removido da inicialização."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { result.Text = $"{e.Name}: não foi possível remover ({ex.Message})."; }
        await ScanAsync();
    }

    private async Task RestoreAsync()
    {
        var removed = manager.Removed(); if (removed.Count == 0 || owner() is not { } window) return;
        string names = string.Join("\n", removed.Take(12).Select(b => $"  • {b.Name}")) + (removed.Count > 12 ? $"\n  … e mais {removed.Count - 12}" : "");
        if (!await ConfirmDialog.AskAsync(window, "Inicialização", $"Devolver {removed.Count} programa(s) à inicialização?\n\n{names}")) return;
        try { await manager.RestoreAllAsync(lifetime); result.Text = "Programas restaurados."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result.Text = "Não restaurados: " + ex.Message; }
        await ScanAsync();
    }
}
