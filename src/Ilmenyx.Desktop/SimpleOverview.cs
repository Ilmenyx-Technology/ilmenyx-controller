// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// Simple mode of the Overview ("Seu PC agora"): one sentence saying whether the PC is fine, four big cards (processor, memory, storage,
/// internet) and the open apps with End. Reads only what the live monitor already samples; refreshes only while on screen.
/// </summary>
public sealed class SimpleOverview : StackPanel
{
    private readonly UsageViewModel usage;
    private readonly ProcessActions actions;
    private readonly Action<string> open;
    private readonly Func<Window?> owner;
    private readonly TextBlock statusTag = new() { Name = "SimpleStatusTag", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock statusText = new() { Name = "SimpleStatusText", FontSize = 13, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button freeSpace = new() { Name = "SimpleFreeSpace", FontSize = 12, Padding = new Thickness(12, 5), IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly (TextBlock Big, TextBlock Sub, UsageChart? Chart, ProgressBar? Bar)[] cards = new (TextBlock, TextBlock, UsageChart?, ProgressBar?)[4];
    private readonly StackPanel apps = new() { Name = "SimpleApps" };
    private readonly Dictionary<string, Bitmap?> icons = new(StringComparer.Ordinal);
    private DateTime appsAt;
    private double? storageFraction;

    private readonly Func<string, bool> canOpen;

    /// <param name="canOpen">Whether a page exists (the free-space shortcut only shows when the Liberar espaço page is there).</param>
    public SimpleOverview(UsageViewModel usage, Action<string> open, Func<Window?> owner, ProcessActions? actions = null, Func<string, bool>? canOpen = null)
    {
        this.usage = usage; this.open = open; this.owner = owner; this.actions = actions ?? ProcessActions.System(); this.canOpen = canOpen ?? (_ => true);
        Name = "SimpleOverview"; Spacing = 14;
        freeSpace.Content = L.T("Liberar espaço"); freeSpace.Click += (_, _) => open("cleanup");
        var tagBox = new Border { CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(12, 5), Background = Palette.Hover, Child = statusTag, VerticalAlignment = VerticalAlignment.Center };
        var statusGrid = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        statusText.Margin = new Thickness(14, 0, 12, 0);
        Grid.SetColumn(statusText, 1); Grid.SetColumn(freeSpace, 2);
        statusGrid.Children.Add(tagBox); statusGrid.Children.Add(statusText); statusGrid.Children.Add(freeSpace);
        var status = UiKit.Card(statusGrid, new Thickness(0)); status.Padding = new Thickness(18, 14);
        Children.Add(status);
        var grid = new UniformGrid { Name = "SimpleCards", Columns = 4 };
        grid.SizeChanged += (_, e) => grid.Columns = e.NewSize.Width < 620 ? 1 : e.NewSize.Width < 980 ? 2 : 4;
        (string Label, string Icon, IBrush Tint, bool Chart)[] defs =
        [
            (L.T("PROCESSADOR"), Icons.Cpu, Palette.DataCpu, true), (L.T("MEMÓRIA"), Icons.Memory, Palette.DataMemory, true),
            (L.T("ARMAZENAMENTO"), Icons.Disks, Palette.DataDisk, false), (L.T("INTERNET"), Icons.Network, Palette.DataNetwork, true),
        ];
        for (int i = 0; i < defs.Length; i++)
        {
            var (label, icon, tint, chart) = defs[i];
            var big = new TextBlock { Name = "SimpleBig" + i, Text = "—", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = tint, TextWrapping = TextWrapping.Wrap };
            var sub = new TextBlock { Name = "SimpleSub" + i, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UiKit.Icon(icon, 14, tint), UiKit.Label(label) } };
            var body = new StackPanel { Spacing = 2, Children = { head, big, sub } };
            UsageChart? c = null; ProgressBar? bar = null;
            if (chart) { c = new UsageChart { Height = 44, MinHeight = 44, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch }; body.Children.Add(c); }
            else { bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8, MinHeight = 8, Margin = new Thickness(0, 26, 0, 0) }; AutomationProperties.SetName(bar, L.T("Espaço usado")); body.Children.Add(bar); }
            var card = UiKit.Card(body, new Thickness(0, 0, 12, 12)); card.Padding = new Thickness(18, 16);
            grid.Children.Add(card);
            cards[i] = (big, sub, c, bar);
        }
        Children.Add(grid);
        var allProcs = new Button { Name = "SimpleAllProcesses", Content = L.T("Ver todos os processos"), FontSize = 12, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Right };
        allProcs.Click += (_, _) => open("procs");
        var appsHead = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetColumn(allProcs, 1); appsHead.Children.Add(UiKit.Label(L.T("APLICATIVOS ABERTOS"))); appsHead.Children.Add(allProcs);
        var appsCard = UiKit.Card(new StackPanel { Children = { appsHead, apps } }, new Thickness(0)); appsCard.Padding = new Thickness(18, 14);
        Children.Add(appsCard);
        usage.Updated += UiKit.OnScreen(this, Refresh);
        AttachedToVisualTree += (_, _) => { appsAt = default; Refresh(); };
    }

    private static string Rate(double? bps) => bps is { } b ? UsageViewModel.Rate(b) : "—";

    private void Refresh()
    {
        var s = usage.Last;
        if (s is null) { statusTag.Text = L.T("Lendo…"); return; }
        // Processor
        double? cpu = s.CpuPercent, temp = s.CpuTempC;
        var levelTemp = temp is { } t ? t >= CpuHot + 15 ? Level.Critical : t >= CpuHot ? Level.Hot : Level.Normal : Level.None;
        cards[0].Big.Text = UsageViewModel.Pct(cpu);
        cards[0].Sub.Text = (temp is { } tc ? tc.ToString("0", CultureInfo.InvariantCulture) + " °C · " : "") + (levelTemp switch { Level.Critical => L.T("muito quente"), Level.Hot => L.T("quente"), _ => cpu is > 80 ? L.T("bem ocupado") : L.T("tranquilo") });
        cards[0].Chart?.SetValues(usage.CpuSeries, 100, 60);
        // Memory (+ swap)
        double? mem = s is { MemoryTotal: > 0, MemoryUsed: { } mu } ? 100.0 * mu / s.MemoryTotal.Value : null;
        cards[1].Big.Text = UsageViewModel.Pct(mem);
        cards[1].Sub.Text = (s is { MemoryTotal: { } mt, MemoryUsed: { } u2 } ? L.T($"{UsageViewModel.Bytes(u2)} de {UsageViewModel.Bytes(mt)}") : "") + (s.SwapUsed is > 0 && s.SwapTotal is { } st ? " · swap " + UsageViewModel.Bytes(s.SwapUsed.Value) : "");
        cards[1].Chart?.SetValues(usage.MemorySeries, 100, 60);
        // Storage: the file system of the home folder (where the user's files and usually the system live)
        storageFraction = null;
        try
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? "/";
            var drive = DriveInfo.GetDrives().Where(d => d.IsReady && home.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal)).OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
            if (drive is { TotalSize: > 0 })
            {
                storageFraction = 1 - drive.AvailableFreeSpace / (double)drive.TotalSize;
                cards[2].Big.Text = (storageFraction.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
                cards[2].Sub.Text = L.T($"{UsageViewModel.Bytes(drive.AvailableFreeSpace)} livres");
                if (cards[2].Bar is { } bar) { bar.Value = storageFraction.Value; bar.Foreground = storageFraction >= 0.9 ? Palette.Error : storageFraction >= 0.75 ? Palette.Warn : Palette.Success; }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // Internet
        cards[3].Big.Text = "↓ " + Rate(s.NetRxBps);
        var wifi = s.Interfaces?.FirstOrDefault(i => i.Wireless);
        cards[3].Sub.Text = (s.NetInterface is null ? L.T("sem conexão ativa") : wifi is not null ? "Wi-Fi" : L.T("cabo")) + " · ↑ " + Rate(s.NetTxBps);
        cards[3].Chart?.SetValues(usage.NetSeries, null, 60);
        // One sentence: the worst thing first.
        var (level, text) = levelTemp == Level.Critical ? (Level.Critical, L.T("O processador está muito quente e já reduz a velocidade. Confira a ventilação."))
            : storageFraction >= 0.9 ? (Level.Critical, L.T("O disco está quase cheio: o sistema fica lento e atualizações podem falhar."))
            : mem >= 90 ? (Level.Hot, L.T("A memória está quase toda em uso. Feche o que não estiver usando."))
            : levelTemp == Level.Hot ? (Level.Hot, L.T("O processador está quente: normal durante jogos ou tarefas pesadas."))
            : storageFraction >= 0.75 ? (Level.Normal, L.T($"Nada está esquentando nem lotado. Só o disco está {storageFraction.Value * 100:0}% cheio."))
            : (Level.Normal, L.T("Nada está esquentando nem lotado."));
        statusTag.Text = level switch { Level.Critical => L.T("! Precisa de atenção"), Level.Hot => L.T("! Fique de olho"), _ => L.T("✓ Está tudo bem") };
        statusTag.Foreground = level switch { Level.Critical => Palette.Error, Level.Hot => Palette.Warn, _ => Palette.Success };
        statusText.Text = text;
        freeSpace.IsVisible = storageFraction >= 0.75 && canOpen("cleanup");
        // The app list changes slowly: every 3 s is enough and keeps the page light.
        if (DateTime.UtcNow - appsAt < TimeSpan.FromSeconds(3)) return;
        appsAt = DateTime.UtcNow;
        FillApps(s.Processes);
    }

    private enum Level { None, Normal, Hot, Critical }
    private const double CpuHot = 80; // same limit as the Sensors page: normal below 80 °C, slowing down near 95 °C

    private void FillApps(IReadOnlyList<ProcessUsage> processes)
    {
        apps.Children.Clear();
        var groups = ProcessGroups.Build(processes).Take(6).ToList();
        if (groups.Count == 0) { apps.Children.Add(new TextBlock { Text = L.T("Nenhum aplicativo aberto identificado."), FontSize = 12.5, Foreground = Palette.Muted, Margin = new Thickness(0, 6) }); return; }
        foreach (var g in groups)
        {
            var app = actions.FindApp(g.AppId);
            string name = app?.Name ?? g.AppId;
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), Margin = new Thickness(0, 4) };
            var label = new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var use = new TextBlock { Text = UsageViewModel.Pct(g.CpuPercent) + " · " + UsageViewModel.Bytes(g.ResidentBytes), FontSize = 12, FontFamily = IlmenyxTheme.Mono, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(label, 1); Grid.SetColumn(use, 2);
            row.Children.Add(Icon(app, name)); row.Children.Add(label); row.Children.Add(use);
            var own = g.Processes.Where(p => p.Uid == actions.CurrentUid).ToList();
            if (own.Count > 0 && g.AppId != "io.github.ilmenyx_technology.IlmenyxController")
            {
                var end = new Button { Name = "SimpleEnd_" + g.AppId, Content = L.T("Encerrar"), FontSize = 12, Padding = new Thickness(12, 5) };
                AutomationProperties.SetName(end, L.T("Encerrar ") + name);
                end.Click += async (_, _) =>
                {
                    if (owner() is not { } window) return;
                    if (!await ConfirmDialog.AskAsync(window, L.T("Encerrar ") + name + "?", L.T("O programa recebe um pedido para fechar, como ao clicar no X da janela, e pode salvar o trabalho antes."), L.T("Encerrar"), L.T("Cancelar"))) return;
                    foreach (var p in own) await actions.Signal(p.Pid, p.StartTicks, false);
                    appsAt = default;
                };
                Grid.SetColumn(end, 3); row.Children.Add(end);
            }
            apps.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, apps.Children.Count == 0 ? 0 : 1, 0, 0), Child = row, Padding = new Thickness(0, 4) });
        }
    }

    private Control Icon(DesktopApp? app, string name)
    {
        if (app?.IconPng is { } path)
        {
            if (!icons.TryGetValue(path, out var bitmap))
            {
                try { bitmap = new Bitmap(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { bitmap = null; }
                icons[path] = bitmap;
            }
            if (bitmap is not null) return new Image { Source = bitmap, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        }
        return new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = Palette.AccentContainer, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Palette.OnAccentContainer, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }
}
