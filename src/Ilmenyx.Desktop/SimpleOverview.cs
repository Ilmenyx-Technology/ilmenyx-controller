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
/// Clicking a card opens its detail right below the cards: a verdict in one sentence, where the resource went, who uses it most and
/// what to do now (actions that already exist: End, Liberar espaço, Inicialização, Sensores…).
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
    private long storageTotal, storageFree;
    private readonly Border[] cardBorders = new Border[4];
    private readonly StackPanel detail = new() { Spacing = 12 };
    private readonly Border detailCard;
    private int selected = -1; // which card's detail is open

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
            int index = i;
            var hit = new Button { Name = "SimpleCard" + i, Content = body, Background = Brushes.Transparent, Padding = new Thickness(18, 16), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius) };
            AutomationProperties.SetName(hit, label + ": " + L.T("ver detalhes"));
            hit.Click += (_, _) => { selected = selected == index ? -1 : index; ShowDetail(); };
            var card = UiKit.Card(hit, new Thickness(0, 0, 12, 12)); card.Padding = new Thickness(0);
            grid.Children.Add(card);
            cards[i] = (big, sub, c, bar); cardBorders[i] = card;
        }
        Children.Add(grid);
        detailCard = UiKit.Card(detail, new Thickness(0)); detailCard.Name = "SimpleDetail"; detailCard.Padding = new Thickness(18, 16); detailCard.IsVisible = false;
        Children.Add(detailCard);
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
                storageFraction = 1 - drive.AvailableFreeSpace / (double)drive.TotalSize; storageTotal = drive.TotalSize; storageFree = drive.AvailableFreeSpace;
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
            : MemoryLevel(s) == InsightLevel.Critical ? (Level.Critical, L.T("A memória está no fim. Programas podem travar ou ser fechados pelo sistema."))
            : MemoryLevel(s) == InsightLevel.Watch ? (Level.Hot, L.T("A memória está apertada. Clique no cartão Memória para ver o que fazer."))
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
        if (selected >= 0) FillDetail(s);
    }

    private static InsightLevel MemoryLevel(UsageSample s) => s is { MemoryTotal: { } t, MemoryUsed: { } u } ? OverviewInsight.Memory(t, u, s.SwapUsed ?? 0) : InsightLevel.Fine;

    private void ShowDetail()
    {
        for (int i = 0; i < cardBorders.Length; i++) cardBorders[i].BorderBrush = i == selected ? Palette.Accent : Palette.Line;
        detailCard.IsVisible = selected >= 0;
        if (selected >= 0 && usage.Last is { } s) FillDetail(s);
    }

    private void FillDetail(UsageSample s)
    {
        detail.Children.Clear();
        var groups = ProcessGroups.Build(s.Processes);
        var who = new StackPanel(); var tips = new StackPanel();
        void Verdict(InsightLevel level, string good, string watch, string bad, string text)
        {
            var tag = new TextBlock { Name = "SimpleDetailTag", Text = level switch { InsightLevel.Critical => "! " + bad, InsightLevel.Watch => "! " + watch, _ => "✓ " + good }, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = level switch { InsightLevel.Critical => Palette.Error, InsightLevel.Watch => Palette.Warn, _ => Palette.Success } };
            var line = new Grid { ColumnDefinitions = new("Auto,*") };
            var words = new TextBlock { Text = text, FontSize = 13, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            Grid.SetColumn(words, 1);
            line.Children.Add(new Border { CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(12, 5), Background = Palette.Hover, Child = tag, VerticalAlignment = VerticalAlignment.Top }); line.Children.Add(words);
            detail.Children.Add(line);
        }
        void Note(string text) => detail.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        void Split(params (long Bytes, IBrush Color, string Label)[] parts)
        {
            var bar = new Grid { Height = 12 }; var legend = new WrapPanel { ItemSpacing = 16, LineSpacing = 4 };
            foreach (var (bytes, color, label) in parts.Where(p => p.Bytes > 0))
            {
                bar.ColumnDefinitions.Add(new ColumnDefinition(bytes, GridUnitType.Star));
                var piece = new Border { Background = color }; Grid.SetColumn(piece, bar.ColumnDefinitions.Count - 1); bar.Children.Add(piece);
                legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = color, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = label + " " + UsageViewModel.Bytes(bytes), FontSize = 12, Foreground = Palette.Muted } } });
            }
            detail.Children.Add(new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = bar }); detail.Children.Add(legend);
        }
        void Tip(string text, string? action = null, Action? run = null)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 4) };
            row.Children.Add(new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            if (action is not null && run is not null)
            {
                var go = new Button { Content = action, FontSize = 12, Padding = new Thickness(12, 5), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                AutomationProperties.SetName(go, action + ": " + text);
                go.Click += (_, _) => run(); Grid.SetColumn(go, 1); row.Children.Add(go);
            }
            tips.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, tips.Children.Count == 0 ? 0 : 1, 0, 0), Child = row, Padding = new Thickness(0, 4) });
        }
        void Page(string key, string text, string action) { if (canOpen(key)) Tip(text, action, () => open(key)); }
        string Name(AppGroup g) => actions.FindApp(g.AppId)?.Name ?? g.AppId;
        bool Mine(AppGroup g) => g.AppId != "io.github.ilmenyx_technology.IlmenyxController" && g.Processes.Any(p => p.Uid == actions.CurrentUid);
        string whoLabel = L.T("QUEM MAIS USA");
        switch (selected)
        {
            case 0:
            {
                double? cpu = s.CpuPercent, temp = s.CpuTempC;
                var level = temp >= CpuHot + 15 ? InsightLevel.Critical : temp >= CpuHot || cpu > 80 ? InsightLevel.Watch : InsightLevel.Fine;
                Verdict(level, L.T("Processador tranquilo"), temp >= CpuHot ? L.T("Processador quente") : L.T("Processador ocupado"), L.T("Processador muito quente"),
                    level == InsightLevel.Critical ? L.T("Ele já reduz a velocidade para se proteger. Confira a ventilação e feche o que estiver pesado.")
                    : temp >= CpuHot ? L.T("Normal durante jogos ou tarefas pesadas. Se estiver assim parado, algo está exigindo demais.")
                    : level == InsightLevel.Watch ? L.T("Algum programa está exigindo muito. Tudo fica mais lento enquanto isso.") : L.T("Trabalhando pouco e com temperatura normal."));
                if (s.CpuModel is { Length: > 0 } model) Note(model);
                foreach (var g in groups.Take(4)) who.Children.Add(AppRow(g, UsageViewModel.Pct(g.CpuPercent), who.Children.Count == 0));
                if (groups.FirstOrDefault(g => g.CpuPercent >= 50 && Mine(g)) is { } busy) Tip(L.T($"{Name(busy)} usa {UsageViewModel.Pct(busy.CpuPercent)} do processador. Se não for de propósito, encerre."), L.T("Encerrar"), () => _ = EndAsync(busy, Name(busy)));
                if (temp >= CpuHot) Page("sensors", L.T("Veja a temperatura de cada peça e das ventoinhas."), L.T("Sensores"));
                break;
            }
            case 1 when s is { MemoryTotal: { } total and > 0, MemoryUsed: { } used }:
            {
                long swap = s.SwapUsed ?? 0, free = Math.Min(s.MemoryFree ?? total - used, total - used);
                var level = OverviewInsight.Memory(total, used, swap);
                Verdict(level, L.T("Memória folgada"), L.T("Memória apertada"), L.T("Memória no fim"),
                    level == InsightLevel.Critical ? L.T("Programas podem travar ou ser fechados pelo sistema. Feche o que não estiver usando.")
                    : OverviewInsight.HeavySwap(total, swap) ? L.T($"Cabe tudo, mas {UsageViewModel.Bytes(swap)} foram para o swap (memória de reserva, mais lenta). Trocar de programa pode engasgar.")
                    : level == InsightLevel.Watch ? L.T("Quase toda em uso. Feche o que não estiver usando.") : L.T("Sobra memória para abrir mais programas."));
                Split((used, Palette.DataMemory, L.T("Programas")), (total - used - free, Palette.Faint, L.T("Cache de arquivos")), (free, Palette.Line, L.T("Livre")));
                Note((total - used - free > 0 ? L.T("Cache não é problema: o sistema devolve na hora que um programa pedir.") + " " : "") + (swap > 0 && s.SwapTotal is { } st ? L.T($"Swap em uso: {UsageViewModel.Bytes(swap)} de {UsageViewModel.Bytes(st)}.") : ""));
                foreach (var g in groups.OrderByDescending(g => g.ResidentBytes).Take(4)) who.Children.Add(AppRow(g, UsageViewModel.Bytes(g.ResidentBytes), who.Children.Count == 0));
                if (OverviewInsight.IdleHog(groups.Where(Mine)) is { } hog) Tip(L.T($"{Name(hog)} está parado e segura {UsageViewModel.Bytes(hog.ResidentBytes)}. Fechar libera esse espaço na hora."), L.T("Encerrar"), () => _ = EndAsync(hog, Name(hog)));
                if (level != InsightLevel.Fine)
                {
                    Page("startup", L.T("Programas que abrem sozinhos com o sistema gastam memória o dia todo."), L.T("Ver"));
                    Tip(L.T($"Se isso acontece todo dia, {UsageViewModel.Bytes(total)} é pouco para o seu uso."));
                }
                break;
            }
            case 2 when storageFraction is { } full:
            {
                var level = full >= 0.9 ? InsightLevel.Critical : full >= 0.75 ? InsightLevel.Watch : InsightLevel.Fine;
                Verdict(level, L.T("Espaço de sobra"), L.T("Disco enchendo"), L.T("Disco quase cheio"),
                    level == InsightLevel.Critical ? L.T("O sistema fica lento e atualizações podem falhar. Libere espaço agora.")
                    : level == InsightLevel.Watch ? L.T("Ainda cabe, mas vale liberar espaço antes de lotar.") : L.T("O disco da sua pasta pessoal tem bastante espaço livre."));
                Split((storageTotal - storageFree, level == InsightLevel.Fine ? Palette.DataDisk : level == InsightLevel.Watch ? Palette.Warn : Palette.Error, L.T("Usado")), (storageFree, Palette.Line, L.T("Livre")));
                whoLabel = "";
                Page("cleanup", L.T("Veja o que dá para apagar com segurança: lixeira, caches, pacotes antigos e arquivos parados."), L.T("Liberar espaço"));
                Page("disks", L.T("Saúde, partições e pendrives."), L.T("Discos"));
                break;
            }
            case 3:
            {
                bool wifi = s.Interfaces?.Any(i => i.Wireless) == true;
                Verdict(s.NetInterface is null ? InsightLevel.Watch : InsightLevel.Fine, L.T("Conectado"), L.T("Sem conexão"), "",
                    s.NetInterface is null ? L.T("Nenhuma rede ativa. Confira o cabo ou o Wi-Fi.") : (wifi ? L.T("Pelo Wi-Fi.") : L.T("Pelo cabo.")) + " " + L.T($"Baixando {Rate(s.NetRxBps)}, enviando {Rate(s.NetTxBps)}."));
                whoLabel = L.T("CONEXÕES");
                foreach (var i in s.Interfaces ?? [])
                    who.Children.Add(new TextBlock { Text = i.Name + (i.Addresses.Count > 0 ? " · " + i.Addresses[0] : "") + " · ↓ " + Rate(i.RxBps) + " · ↑ " + Rate(i.TxBps), FontSize = 12.5, FontFamily = IlmenyxTheme.Mono, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4) });
                Page("net", L.T("Mais detalhes de cada conexão."), L.T("Rede"));
                break;
            }
            default: Note(L.T("Sem leitura agora.")); return;
        }
        if (tips.Children.Count == 0) Tip(L.T("Nada a fazer agora."));
        var right = new StackPanel { Children = { UiKit.Label(L.T("O QUE FAZER AGORA")), tips } };
        detail.Children.Add(who.Children.Count == 0 ? right : HealthCardView.Columns([new StackPanel { Children = { UiKit.Label(whoLabel), who } }, right], 760, 24));
    }

    private async Task EndAsync(AppGroup g, string name)
    {
        if (owner() is not { } window) return;
        if (!await ConfirmDialog.AskAsync(window, L.T("Encerrar ") + name + "?", L.T("O programa recebe um pedido para fechar, como ao clicar no X da janela, e pode salvar o trabalho antes."), L.T("Encerrar"), L.T("Cancelar"))) return;
        foreach (var p in g.Processes.Where(p => p.Uid == actions.CurrentUid)) await actions.Signal(p.Pid, p.StartTicks, false);
        appsAt = default;
    }

    /// <summary>One app: icon, name, its use and End (only for the user's own programs, never Ilmenyx itself).</summary>
    private Control AppRow(AppGroup g, string use, bool first)
    {
        var app = actions.FindApp(g.AppId);
        string name = app?.Name ?? g.AppId;
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), Margin = new Thickness(0, 4) };
        var label = new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var used = new TextBlock { Text = use, FontSize = 12, FontFamily = IlmenyxTheme.Mono, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(label, 1); Grid.SetColumn(used, 2);
        row.Children.Add(Icon(app, name)); row.Children.Add(label); row.Children.Add(used);
        if (g.AppId != "io.github.ilmenyx_technology.IlmenyxController" && g.Processes.Any(p => p.Uid == actions.CurrentUid))
        {
            var end = new Button { Content = L.T("Encerrar"), FontSize = 12, Padding = new Thickness(12, 5) };
            AutomationProperties.SetName(end, L.T("Encerrar ") + name);
            end.Click += async (_, _) => await EndAsync(g, name);
            Grid.SetColumn(end, 3); row.Children.Add(end);
        }
        return new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Child = row, Padding = new Thickness(0, 4) };
    }

    private enum Level { None, Normal, Hot, Critical }
    private const double CpuHot = 80; // same limit as the Sensors page: normal below 80 °C, slowing down near 95 °C

    private void FillApps(IReadOnlyList<ProcessUsage> processes)
    {
        apps.Children.Clear();
        var groups = ProcessGroups.Build(processes).Take(6).ToList();
        if (groups.Count == 0) { apps.Children.Add(new TextBlock { Text = L.T("Nenhum aplicativo aberto identificado."), FontSize = 12.5, Foreground = Palette.Muted, Margin = new Thickness(0, 6) }); return; }
        foreach (var g in groups) apps.Children.Add(AppRow(g, UsageViewModel.Pct(g.CpuPercent) + " · " + UsageViewModel.Bytes(g.ResidentBytes), apps.Children.Count == 0));
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
