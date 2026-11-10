// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Startup page: summary, restore button, toggle list with remove): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
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
        var header = UiKit.PageHeader("Inicialização", "Programas que abrem junto com a sua sessão, separados entre os do sistema e os seus, com o que cada um faz");
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
            int on = Entries.Count(e => e.Enabled), sys = Entries.Count(e => e.Advice.Group == StartupGroup.System);
            summary.Text = Entries.Count == 0 ? "NENHUM PROGRAMA NA INICIALIZAÇÃO" : $"{Entries.Count} PROGRAMAS · {on} ATIVADOS · {Entries.Count - on} DESATIVADOS · {sys} DO SISTEMA · {Entries.Count - sys} SEUS";
            int removed = manager.Removed().Count; restore.IsEnabled = removed > 0; restore.Content = removed > 0 ? $"RESTAURAR REMOVIDOS ({removed})" : "RESTAURAR REMOVIDOS";
            list.Children.Clear();
            AddGroup(StartupGroup.System, "StartupGroupSystem", StartupCatalog.GroupSystemTitle,
                "Peças da área de trabalho e da distribuição (teclas, som, rede, senhas, aparência). Desativar uma delas pode deixar algo sem funcionar; o Ilmenyx avisa antes.");
            AddGroup(StartupGroup.User, "StartupGroupUser", StartupCatalog.GroupUserTitle,
                "Programas que você instalou (ou que se colocaram aqui sozinhos). Desativar não desinstala nada; o programa só deixa de abrir com a sessão.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result.Text = "Não foi possível listar: " + ex.Message; }
    }

    private void AddGroup(StartupGroup group, string name, string title, string note)
    {
        var items = Entries.Where(e => e.Advice.Group == group).OrderBy(e => !e.Advice.RunsHere).ToList();
        var panel = new StackPanel { Name = name, Margin = new Thickness(0, 6, 0, 8) };
        panel.Children.Add(new TextBlock { Text = $"{title.ToUpperInvariant()} ({items.Count})", FontSize = 12, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 2) });
        panel.Children.Add(new TextBlock { Text = note, Foreground = Palette.Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        if (items.Count == 0) panel.Children.Add(new TextBlock { Text = "Nenhum item neste grupo.", Foreground = Palette.Faint, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var e in items) panel.Children.Add(Row(e));
        list.Children.Add(panel);
    }

    private static IBrush RoleBrush(StartupRole role) => role switch
    {
        StartupRole.Required => Palette.Error,
        StartupRole.Recommended => Palette.AccentText,
        StartupRole.Unknown => Palette.Warn,
        StartupRole.App or StartupRole.User => Palette.Success,
        _ => Palette.Muted,
    };

    private Control Row(StartupEntry e)
    {
        var a = e.Advice;
        var toggle = new CheckBox { Name = "StartupToggle_" + e.Id, IsChecked = e.Enabled, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        AutomationProperties.SetName(toggle, "Ativar ou desativar " + e.Name);
        toggle.Click += async (_, _) =>
        {
            bool on = toggle.IsChecked == true;
            if (!on && a.ConfirmBeforeDisable && !await ConfirmDisableAsync(e)) { toggle.IsChecked = true; result.Text = $"{e.Name}: nada mudou."; return; }
            try { await manager.SetEnabledAsync(e, on, lifetime); result.Text = $"{e.Name}: {(on ? "ativado" : "desativado")}. Vale na próxima inicialização da sessão."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException or System.ComponentModel.Win32Exception) { result.Text = $"{e.Name}: falhou ({ex.Message})."; }
            await ScanAsync();
        };
        var badge = new Border
        {
            Name = "StartupBadge_" + e.Id, BorderBrush = RoleBrush(a.Role), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(8, 1),
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = a.Badge, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = RoleBrush(a.Role) },
        };
        var title = new WrapPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = e.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, badge } };
        if (a.Component.Length > 0) title.Children.Add(new TextBlock { Text = a.Component, FontSize = 11, Foreground = Palette.Faint, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var head = new Grid { ColumnDefinitions = new("*,Auto") }; head.Children.Add(title);
        var state = new TextBlock { Name = "StartupState_" + e.Id, Text = e.State, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 3, 0, 0) };
        Grid.SetColumn(state, 1); head.Children.Add(state);
        var info = new StackPanel { Spacing = 1, Children = { head } };
        if (a.Explanation.Length > 0) info.Children.Add(new TextBlock { Name = "StartupExplain_" + e.Id, Text = a.Explanation, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        info.Children.Add(new TextBlock { Name = "StartupAdvice_" + e.Id, Text = a.Recommendation, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = a.RunsHere ? RoleBrush(a.Role) : Palette.Muted, TextWrapping = TextWrapping.Wrap });
        var command = new TextBlock { Text = e.Command, Foreground = Palette.Faint, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) }; ToolTip.SetTip(command, e.Command);
        info.Children.Add(command); info.Children.Add(new TextBlock { Text = e.Source, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto") }; Grid.SetColumn(info, 1);
        grid.Children.Add(toggle); grid.Children.Add(info);
        if (e is { Kind: "Desktop", System: false } && a.Group == StartupGroup.User)
        {
            var remove = new Button { Name = "StartupRemove_" + e.Id, Content = "REMOVER", Padding = new Thickness(12, 5), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            remove.Click += async (_, _) => await RemoveAsync(e);
            Grid.SetColumn(remove, 2); grid.Children.Add(remove);
        }
        var card = UiKit.Card(grid, new Thickness(0, 0, 0, 8));
        if (a.Group == StartupGroup.System) card.BorderBrush = a.Role == StartupRole.Required && a.RunsHere ? Palette.Error : Palette.Line;
        if (!a.RunsHere) card.Opacity = 0.92; // was 0.7: the row text fell below the readable contrast
        return card;
    }

    /// <summary>Visible warning before disabling something that belongs to the system (or that nobody recognises).</summary>
    private async Task<bool> ConfirmDisableAsync(StartupEntry e)
    {
        if (owner() is not { } window) return false;
        var a = e.Advice;
        string what = a.Role == StartupRole.Unknown
            ? $"Não sabemos ao certo o que \"{e.Name}\" faz: ela foi instalada para todos os usuários do PC.\n\n{a.Explanation}\n\nPesquise o nome antes de desativar."
            : $"\"{e.Name}\" faz parte do sistema{(a.Component.Length > 0 ? $" ({a.Component})" : "")}.\n\n{a.Explanation}\n\nRecomendação: {a.Recommendation}";
        string risk = a.Role == StartupRole.Required ? "\n\nATENÇÃO: desativar pode deixar teclas, som, rede, senhas ou a aparência sem funcionar na próxima sessão." : "";
        return await ConfirmDialog.AskAsync(window, "Desativar item do sistema?", what + risk + "\n\nDá para religar depois no mesmo interruptor (o arquivo do sistema não é alterado).", "Desativar mesmo assim", "Cancelar");
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
