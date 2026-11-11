// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <param name="Signal">Ends one process (pid, start ticks, force); null on success, else the reason. Tests pass a fake.</param>
/// <param name="FindApp">Name and icon of an app id (.desktop); null when unknown.</param>
/// <param name="UserName">Login name of a uid.</param>
/// <param name="CurrentUid">The user running Ilmenyx: only their processes can be ended.</param>
public sealed record ProcessActions(Func<int, long, bool, Task<string?>> Signal, Func<string, DesktopApp?> FindApp, Func<int, string> UserName, int CurrentUid)
{
    public static ProcessActions System()
    {
        var apps = new DesktopApps();
        return new((pid, start, force) => ProcessDetails.SignalAsync(pid, start, force), apps.Find, UserNames.Of, ProcessDetails.CurrentUid);
    }
}

/// <summary>
/// Processes page, like a desktop system monitor: apps grouped with their processes, or every process with PID, user, CPU, memory and disk;
/// search; End (asks the program to close) and Force end, both after a confirmation and only for the user's own processes.
/// Local view only: names are never exported.
/// </summary>
public sealed class ProcessTable : Border
{
    private enum Sort { Cpu, Memory, Disk, Name }
    private const int MaxShown = 300;
    private const string Self = "io.github.ilmenyx_technology.IlmenyxController";

    private readonly UsageViewModel usage;
    private readonly ProcessActions actions;
    private readonly StackPanel rows = new() { Name = "ProcessRows" };
    private readonly TextBlock summary = new() { Name = "ProcessSummary", FontSize = 12, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8) };
    private readonly TextBlock result = new() { Name = "ProcessResult", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(2, 0, 0, 8) };
    private readonly TextBox search = new() { Name = "ProcessSearch", Width = 260, FontSize = 12.5 };
    private readonly Button appsTab, allTab, mineChip, endButton, forceButton;
    private readonly Grid head = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly Dictionary<string, Bitmap?> icons = new(StringComparer.Ordinal);
    private bool appsView = true, onlyMine;
    private Sort sort = Sort.Cpu;
    // Selection survives refreshes: an app id, or a process (pid + start).
    private string? selectedApp;
    private (int Pid, long Start)? selectedProcess;
    private bool stale;

    public ProcessTable(UsageViewModel usage, ProcessActions? actions = null)
    {
        this.usage = usage; this.actions = actions ?? ProcessActions.System();
        search.PlaceholderText = L.T("Buscar por nome ou PID");
        AutomationProperties.SetName(search, L.T("Buscar processos"));
        search.TextChanged += (_, _) => Fill();
        appsTab = Chip("ProcessTabApps", L.T("Aplicativos")); allTab = Chip("ProcessTabAll", L.T("Todos os processos")); mineChip = Chip("ProcessOnlyMine", L.T("Só os meus"));
        appsTab.Click += (_, _) => { appsView = true; Paint(); Fill(); };
        allTab.Click += (_, _) => { appsView = false; Paint(); Fill(); };
        mineChip.Click += (_, _) => { onlyMine = !onlyMine; Paint(); Fill(); };
        endButton = new Button { Name = "BtnProcessEnd", Content = L.T("Encerrar"), FontWeight = FontWeight.SemiBold, Padding = new Thickness(16, 7), IsEnabled = false };
        forceButton = new Button { Name = "BtnProcessForce", Content = L.T("Forçar encerramento"), FontWeight = FontWeight.SemiBold, Padding = new Thickness(16, 7), IsEnabled = false, Foreground = Palette.Error };
        ToolTip.SetTip(forceButton, L.T("Fecha na hora, sem salvar nada. Use só se Encerrar não funcionou."));
        endButton.Click += async (_, _) => await EndAsync(force: false);
        forceButton.Click += async (_, _) => await EndAsync(force: true);

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var c in new Control[] { appsTab, allTab, mineChip }) { c.Margin = new Thickness(0, 0, 6, 6); toolbar.Children.Add(c); }
        search.Margin = new Thickness(8, 0, 12, 6); toolbar.Children.Add(search);
        endButton.Margin = new Thickness(0, 0, 6, 6); forceButton.Margin = new Thickness(0, 0, 0, 6);
        toolbar.Children.Add(endButton); toolbar.Children.Add(forceButton);

        usage.Updated += () => { if (TopLevel.GetTopLevel(this) is null || !IsEffectivelyVisible) { stale = true; return; } Fill(); };
        AttachedToVisualTree += (_, _) => { usage.DetailedProcesses = true; if (stale) { stale = false; Fill(); } };
        DetachedFromVisualTree += (_, _) => usage.DetailedProcesses = false;

        var tableLayout = new Grid { RowDefinitions = new("Auto,*") };
        var scroll = new ScrollViewer { Name = "ProcessScroll", Content = rows }; Grid.SetRow(scroll, 1);
        tableLayout.Children.Add(head); tableLayout.Children.Add(scroll);
        var surface = UiKit.Card(tableLayout, new Thickness(0)); surface.Padding = new Thickness(16, 10, 8, 8);
        var layout = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*") };
        var header = UiKit.PageHeader(L.T("Processos"), L.T("O que está rodando agora. Feche um programa travado com Encerrar; você só pode encerrar os seus programas, nunca os do sistema."));
        Grid.SetRow(toolbar, 1); Grid.SetRow(summary, 2); Grid.SetRow(result, 3); Grid.SetRow(surface, 4);
        layout.Children.Add(header); layout.Children.Add(toolbar); layout.Children.Add(summary); layout.Children.Add(result); layout.Children.Add(surface);
        Name = "ProcessPanel"; Background = Brushes.Transparent; Padding = new Thickness(0, 4, 10, 12); Child = layout;
        Paint(); Fill();
    }

    private static Button Chip(string name, string text) => new() { Name = name, Content = text, FontSize = 12.5, Padding = new Thickness(14, 6), CornerRadius = new CornerRadius(999) };

    private void Paint()
    {
        UiKit.ChipStyle(appsTab, appsView); UiKit.ChipStyle(allTab, !appsView); UiKit.ChipStyle(mineChip, onlyMine);
        BuildHeader();
    }

    private void BuildHeader()
    {
        head.Children.Clear();
        head.ColumnDefinitions = new(appsView ? "*,90,80,110,120" : "*,80,110,80,110,120");
        var columns = appsView
            ? new (string Text, Sort? Key, string Name)[] { (L.T("NOME"), Sort.Name, "SortName"), (L.T("PROCESSOS"), null, "ColCount"), ("CPU", Sort.Cpu, "SortCpu"), (L.T("Memória"), Sort.Memory, "SortMemory"), (L.T("Disco"), Sort.Disk, "SortDisk") }
            : [(L.T("NOME"), Sort.Name, "SortName"), ("PID", null, "ColPid"), (L.T("USUÁRIO"), null, "ColUser"), ("CPU", Sort.Cpu, "SortCpu"), (L.T("Memória"), Sort.Memory, "SortMemory"), (L.T("Disco"), Sort.Disk, "SortDisk")];
        for (int i = 0; i < columns.Length; i++)
        {
            var (text, key, name) = columns[i];
            Control cell;
            if (key is { } k)
            {
                bool on = sort == k;
                var b = new Button
                {
                    Name = name, Content = on ? text + (k == Sort.Name ? " ▲" : " ▼") : text, FontSize = 11, FontWeight = FontWeight.SemiBold, Padding = new Thickness(6, 3),
                    Background = Brushes.Transparent, Foreground = on ? Palette.AccentText : Palette.Faint,
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0),
                };
                b.Click += (_, _) => { sort = k; BuildHeader(); Fill(); };
                cell = b;
            }
            else
            {
                var label = UiKit.Label(text); label.Name = name; label.VerticalAlignment = VerticalAlignment.Center;
                label.HorizontalAlignment = HorizontalAlignment.Right; label.Margin = new Thickness(4, 0, 10, 0);
                cell = label;
            }
            Grid.SetColumn(cell, i); head.Children.Add(cell);
        }
    }

    private IEnumerable<ProcessUsage> Visible(IEnumerable<ProcessUsage> all)
    {
        string q = search.Text?.Trim() ?? "";
        foreach (var p in all)
        {
            if (onlyMine && p.Uid != actions.CurrentUid) continue;
            if (q.Length > 0 && !p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && p.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture) != q) continue;
            yield return p;
        }
    }

    private static double Disk(double? read, double? write) => read is null && write is null ? -1 : (read ?? 0) + (write ?? 0);

    private void Fill()
    {
        var all = usage.Last?.Processes ?? [];
        rows.Children.Clear();
        if (appsView) FillApps(all); else FillProcesses(all);
        Selection();
    }

    private void FillApps(IReadOnlyList<ProcessUsage> all)
    {
        string q = search.Text?.Trim() ?? "";
        var groups = ProcessGroups.Build(onlyMine ? all.Where(p => p.Uid == actions.CurrentUid) : all)
            .Select(g => (G: g, App: actions.FindApp(g.AppId)))
            .Where(x => q.Length == 0 || (x.App?.Name ?? x.G.AppId).Contains(q, StringComparison.OrdinalIgnoreCase) || x.G.Processes.Any(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        groups = (sort switch
        {
            Sort.Memory => groups.OrderByDescending(x => x.G.ResidentBytes),
            Sort.Disk => groups.OrderByDescending(x => Disk(x.G.ReadBps, x.G.WriteBps)),
            Sort.Name => groups.OrderBy(x => x.App?.Name ?? x.G.AppId, StringComparer.CurrentCultureIgnoreCase),
            _ => groups.OrderByDescending(x => x.G.CpuPercent ?? -1).ThenByDescending(x => x.G.ResidentBytes),
        }).ToList();
        int background = all.Count(p => ProcessGroups.AppId(p.AppUnit) is null);
        summary.Text = L.T($"{groups.Count} aplicativos abertos · {background} processos em segundo plano (sistema e serviços) na aba Todos os processos");
        if (groups.Count == 0) { rows.Children.Add(Empty(q.Length > 0 ? L.T("Nenhum aplicativo com esse nome.") : L.T("Nenhum aplicativo identificado. A aba Todos os processos mostra tudo o que está rodando."))); return; }
        foreach (var (g, app) in groups)
        {
            string name = app?.Name ?? g.AppId;
            var line = new Grid { ColumnDefinitions = new("*,90,80,110,120") };
            var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 6) };
            nameCell.Children.Add(Icon(app, name));
            nameCell.Children.Add(new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            line.Children.Add(nameCell);
            AddCells(line, 1, g.Processes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), UsageViewModel.Pct(g.CpuPercent), UsageViewModel.Bytes(g.ResidentBytes), DiskText(g.ReadBps, g.WriteBps));
            rows.Children.Add(Row(line, selectedApp == g.AppId, () => { selectedApp = g.AppId; selectedProcess = null; Fill(); }, name));
        }
    }

    private void FillProcesses(IReadOnlyList<ProcessUsage> all)
    {
        var list = Visible(all);
        list = sort switch
        {
            Sort.Memory => list.OrderByDescending(p => p.ResidentBytes ?? -1),
            Sort.Disk => list.OrderByDescending(p => Disk(p.ReadBps, p.WriteBps)),
            Sort.Name => list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => list.OrderByDescending(p => p.CpuPercent ?? -1).ThenByDescending(p => p.ResidentBytes ?? -1),
        };
        var shown = list.ToList();
        summary.Text = shown.Count > MaxShown
            ? L.T($"{usage.Last?.ProcessCount ?? 0} processos no sistema · {shown.Count} na lista · mostrando os {MaxShown} primeiros (use a busca)")
            : L.T($"{usage.Last?.ProcessCount ?? 0} processos no sistema · {shown.Count} na lista");
        if (shown.Count == 0) { rows.Children.Add(Empty(L.T("Nenhum processo com esse nome ou PID."))); return; }
        foreach (var p in shown.Take(MaxShown))
        {
            var line = new Grid { ColumnDefinitions = new("*,80,110,80,110,120") };
            line.Children.Add(new TextBlock { Text = p.Name, FontSize = 13, Margin = new Thickness(4, 8), TextTrimming = TextTrimming.CharacterEllipsis });
            AddCells(line, 1, p.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Uid is { } u ? actions.UserName(u) : "—", UsageViewModel.Pct(p.CpuPercent),
                p.ResidentBytes is { } b ? UsageViewModel.Bytes(b) : "—", DiskText(p.ReadBps, p.WriteBps));
            var key = (p.Pid, p.StartTicks);
            rows.Children.Add(Row(line, selectedProcess == key, () => { selectedProcess = key; selectedApp = null; Fill(); }, p.Name));
        }
    }

    private static string DiskText(double? read, double? write) =>
        read is null && write is null ? "—" : (read ?? 0) + (write ?? 0) < 1 ? "0" : UsageViewModel.Rate((read ?? 0) + (write ?? 0));

    private static void AddCells(Grid line, int first, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            var t = new TextBlock { Text = values[i], FontSize = 12.5, FontFamily = IlmenyxTheme.Mono, Margin = new Thickness(4, 8, 10, 8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Muted };
            Grid.SetColumn(t, first + i); line.Children.Add(t);
        }
    }

    private static Border Row(Grid line, bool selected, Action select, string name)
    {
        var row = new Border
        {
            BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = line, Focusable = true, Cursor = new Cursor(StandardCursorType.Hand),
            Background = selected ? Palette.Hover : Brushes.Transparent, CornerRadius = new CornerRadius(6),
        };
        AutomationProperties.SetName(row, name);
        row.PointerPressed += (_, _) => select();
        row.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { select(); e.Handled = true; } };
        return row;
    }

    private static TextBlock Empty(string text) => new() { Text = text, Foreground = Palette.Muted, FontSize = 13, Margin = new Thickness(4, 16), TextWrapping = TextWrapping.Wrap };

    private Control Icon(DesktopApp? app, string name)
    {
        if (app?.IconPng is { } path)
        {
            if (!icons.TryGetValue(path, out var bitmap))
            {
                try { bitmap = new Bitmap(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { bitmap = null; }
                icons[path] = bitmap;
            }
            if (bitmap is not null) return new Image { Source = bitmap, Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        }
        // No PNG icon (many themes ship only SVG): the first letter on a round tile.
        return new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Palette.AccentContainer, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Palette.OnAccentContainer, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }

    /// <summary>The selected processes that this user may end, with the name shown in the confirmation.</summary>
    private (string Name, IReadOnlyList<ProcessUsage> Targets, bool Others, bool Critical)? Target()
    {
        var all = usage.Last?.Processes ?? [];
        if (selectedApp is { } id)
        {
            var group = ProcessGroups.Build(all).FirstOrDefault(g => g.AppId == id);
            if (group is null) return null;
            var own = group.Processes.Where(p => p.Uid == actions.CurrentUid).ToArray();
            return (actions.FindApp(id)?.Name ?? id, own, own.Length < group.Processes.Count, false);
        }
        if (selectedProcess is { } key && all.FirstOrDefault(p => p.Pid == key.Pid && p.StartTicks == key.Start) is { } proc)
            return (proc.Name, proc.Uid == actions.CurrentUid ? [proc] : [], proc.Uid != actions.CurrentUid, ProcessGroups.IsSessionCritical(proc.Name));
        return null;
    }

    private void Selection()
    {
        var t = Target();
        bool self = selectedApp == Self || (t is { } x && x.Targets.Any(p => ProcessGroups.AppId(p.AppUnit) == Self));
        bool can = t is { Targets.Count: > 0 } && !self;
        endButton.IsEnabled = forceButton.IsEnabled = can;
        string tip = t is null ? L.T("Selecione um aplicativo ou processo na lista.")
            : self ? L.T("Este é o próprio Ilmenyx: feche pela janela.")
            : t.Value.Targets.Count == 0 ? L.T("Este processo é de outro usuário ou do sistema: o Ilmenyx não encerra.")
            : L.T("Pede para o programa fechar, como clicar no X da janela. Ele pode salvar o trabalho antes.");
        ToolTip.SetTip(endButton, tip);
    }

    private async Task EndAsync(bool force)
    {
        if (Target() is not { Targets.Count: > 0 } t || TopLevel.GetTopLevel(this) is not Window owner) return;
        string title = force ? L.T("Forçar o encerramento de ") + t.Name + "?" : L.T("Encerrar ") + t.Name + "?";
        string message = force
            ? L.T("O programa fecha na hora, sem salvar nada. Use só se Encerrar não funcionou.")
            : L.T("O programa recebe um pedido para fechar, como ao clicar no X da janela, e pode salvar o trabalho antes.");
        if (t.Targets.Count > 1) message += "\n\n" + L.T($"São {t.Targets.Count} processos deste aplicativo.");
        if (t.Others) message += "\n\n" + L.T("Partes dele que são do sistema ou de outro usuário continuam abertas.");
        if (t.Critical) message += "\n\n" + L.T("Atenção: este processo faz parte da sua área de trabalho. Encerrá-lo pode fechar a sessão ou congelar a tela.");
        if (!await ConfirmDialog.AskAsync(owner, title, message, force ? L.T("Forçar encerramento") : L.T("Encerrar"), L.T("Cancelar"))) return;
        var errors = new List<string>();
        foreach (var p in t.Targets)
            if (await actions.Signal(p.Pid, p.StartTicks, force) is { } error) errors.Add(error);
        int ok = t.Targets.Count - errors.Count;
        result.IsVisible = true;
        result.Foreground = errors.Count == 0 ? Palette.Success : ok > 0 ? Palette.Warn : Palette.Error;
        result.Text = errors.Count == 0
            ? (force ? L.T("Encerrado à força: ") : L.T("Pedido de encerramento enviado: ")) + t.Name + (force ? "." : L.T(". Se ele não fechar em alguns segundos, use Forçar encerramento."))
            : ok > 0 ? L.T($"{ok} de {t.Targets.Count} processos encerrados. ") + errors[0] : errors[0];
    }
}
