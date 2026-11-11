// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// "Jogo não abre?" (JOGOS): read-only checklist of why Proton/Flatpak/a game does not start (GameLaunchChecks). Each card: status, what
// was found, plain explanation, risk, the exact commands to copy and, only for user-level Flatpak changes, a one-click fix with confirmation
// and undo. Root or security-reducing changes are never run by the app.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <param name="Collect">Read-only facts (no root).</param>
/// <param name="RunUserFix">Runs one whitelisted user-level flatpak call; null hides the one-click buttons.</param>
/// <param name="OpenPage">Opens another page of the shell (Otimiza, Driver NVIDIA); null hides the link buttons.</param>
public sealed record GameCheckServices(
    Func<CancellationToken, Task<GameCheckFacts>> Collect,
    Func<IReadOnlyList<string>, CancellationToken, Task<(bool Ok, string Message)>>? RunUserFix,
    Action<string>? OpenPage = null);

public sealed class GameCheckPage : StackPanel
{
    private enum Filter { Action, Ok, All }
    private readonly GameCheckServices services;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly TextBlock status = new() { Name = "GcStatus", Text = L.T("Clique em Verificar."), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8) };
    private readonly TextBlock summary = new() { Name = "GcSummary", FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock distro = new() { Name = "GcDistro", FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel counts = new() { Name = "GcCounts", ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StatusRing ring = new();
    private readonly TextBlock ringText = new() { FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, IsVisible = false, Margin = new Thickness(0, 0, 0, 10), MinHeight = 4 };
    private readonly WrapPanel filters = new() { ItemSpacing = 6, Margin = new Thickness(0, 4, 0, 10) };
    private readonly StackPanel list = new() { Name = "GcList" };
    private readonly Border listCard;
    private readonly Border hero;
    private readonly Button scan;
    private Filter filter = Filter.Action;
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);
    private bool busy;
    public IReadOnlyList<GameCheck> Checks { get; private set; } = [];

    public GameCheckPage(GameCheckServices services, Func<Window?> owner, CancellationToken lifetime)
    {
        this.services = services; this.owner = owner; this.lifetime = lifetime;
        scan = new Button { Name = "BtnGcScan", Content = L.T("Verificar"), FontWeight = FontWeight.SemiBold, Padding = new Thickness(16, 7) };
        scan.Click += async (_, _) => await RefreshAsync();
        Children.Add(UiKit.PageHeader(L.T("Por que o jogo não abre?"), L.T("Conferimos os motivos mais comuns para a Steam, o Proton, o Flatpak ou um jogo não abrirem, sem mudar nada. Comece pelos itens marcados como Problema."), scan));
        Children.Add(status);
        Children.Add(progress);
        // Health panel: how many checks are fine (ring), what was found and the counts by state, with words and not only colors.
        var ringBox = new Grid { Width = 84, Height = 84, VerticalAlignment = VerticalAlignment.Center, Children = { ring, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { ringText, new TextBlock { Text = L.T("em ordem"), FontSize = 10.5, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Center } } } } };
        var heroGrid = new Grid { ColumnDefinitions = new("Auto,*") };
        var text = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { summary, distro, counts } };
        Grid.SetColumn(text, 1); heroGrid.Children.Add(ringBox); heroGrid.Children.Add(text);
        hero = UiKit.Card(heroGrid, new Thickness(0, 0, 0, 12)); hero.Padding = new Thickness(22, 18); hero.IsVisible = false;
        Children.Add(hero);
        Children.Add(filters);
        listCard = UiKit.Card(list, new Thickness(0)); listCard.Padding = new Thickness(0); listCard.ClipToBounds = true; listCard.IsVisible = false;
        Children.Add(listCard);
        Loaded += async (_, _) => { if (Checks.Count == 0 && !busy) await RefreshAsync(); };
    }

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; scan.IsEnabled = false; progress.IsVisible = true; status.Text = L.T("Conferindo o sistema (somente leitura). As correções aparecem no fim.");
        try
        {
            var facts = await services.Collect(lifetime);
            Checks = GameLaunchChecks.Build(facts);
            int problems = Checks.Count(c => c.Status == CheckStatus.Problem), attention = Checks.Count(c => c.Status == CheckStatus.Attention), ok = Checks.Count - problems - attention;
            summary.Text = problems == 1 ? L.T("1 coisa pode estar impedindo seus jogos")
                : problems > 1 ? L.T($"{problems} coisas podem estar impedindo seus jogos")
                : attention > 0 ? L.T($"Nenhum problema · {attention} aviso(s) opcional(is)") : L.T($"Nenhum problema nas {Checks.Count} verificações");
            summary.Foreground = problems > 0 ? Palette.Error : attention > 0 ? Palette.Warn : Palette.Success;
            distro.Text = (facts.Distro.Name.Length > 0 ? facts.Distro.Name : L.T("Sistema não identificado")) + L.T($" · verificado às {DateTime.Now:HH:mm} · nada foi alterado");
            ring.Set(ok, attention, problems); ringText.Text = $"{ok}/{Checks.Count}";
            counts.Children.Clear();
            if (problems > 0) counts.Children.Add(UiKit.Tag(L.T($"{problems} problema(s)"), Palette.Error));
            if (attention > 0) counts.Children.Add(UiKit.Tag(L.T($"{attention} aviso(s) opcional(is)"), Palette.Warn));
            counts.Children.Add(UiKit.Tag(L.T($"{ok} em ordem"), Palette.Success));
            // The first problem opens by itself; the rest stay short until clicked.
            expanded.Clear();
            if (Checks.OrderByDescending(c => c.Status).FirstOrDefault(c => c.Status == CheckStatus.Problem) is { } first) expanded.Add(first.Id);
            filter = problems + attention > 0 ? Filter.Action : Filter.All;
            hero.IsVisible = listCard.IsVisible = true;
            Render();
            status.Text = L.T($"Verificado às {DateTime.Now:HH:mm:ss}. Nada foi alterado.") + (problems + attention == 0 ? L.T(" Se um jogo ainda não abre, veja a página Compatibilidade dele.") : "");
        }
        catch (OperationCanceledException) { status.Text = L.T("Verificação cancelada."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        { status.Text = L.T("Não foi possível terminar a verificação: ") + e.Message + L.T(" Clique em Verificar para tentar de novo."); status.Foreground = Palette.Warn; }
        finally { busy = false; scan.IsEnabled = true; progress.IsVisible = false; }
    }

    private void Render()
    {
        int action = Checks.Count(c => c.Status is CheckStatus.Problem or CheckStatus.Attention), fine = Checks.Count - action;
        filters.Children.Clear();
        foreach (var (f, label, name) in new[] { (Filter.Action, L.T($"Precisa de atenção · {action}"), "GcFilterAction"), (Filter.Ok, L.T($"Em ordem · {fine}"), "GcFilterOk"), (Filter.All, L.T($"Todos · {Checks.Count}"), "GcFilterAll") })
        {
            var b = new Button { Name = name, Content = label, FontSize = 12.5, Padding = new Thickness(14, 6), CornerRadius = new CornerRadius(999) };
            UiKit.ChipStyle(b, filter == f);
            b.Click += (_, _) => { filter = f; Render(); };
            filters.Children.Add(b);
        }
        list.Children.Clear();
        var shown = Checks.Where(c => filter == Filter.All || (filter == Filter.Action) == (c.Status is CheckStatus.Problem or CheckStatus.Attention)).OrderByDescending(c => c.Status).ToList();
        if (shown.Count == 0)
            list.Children.Add(new TextBlock { Text = filter == Filter.Action ? L.T("Nada precisa de atenção. Veja os itens em ordem ou todos.") : L.T("Nenhum item aqui."), Foreground = Palette.Muted, Margin = new Thickness(18, 16), TextWrapping = TextWrapping.Wrap });
        foreach (var c in shown) list.Children.Add(Row(c));
    }

    private static IBrush StatusBrush(CheckStatus s) => s switch { CheckStatus.Ok => Palette.Success, CheckStatus.Problem => Palette.Error, CheckStatus.Attention => Palette.Warn, _ => Palette.Muted };

    /// <summary>One check: icon, title, what was found and the main action on one line; the explanation, risk and commands open below.</summary>
    private Border Row(GameCheck c)
    {
        bool open = expanded.Contains(c.Id);
        var icon = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center, Background = Palette.Hover,
            Child = new TextBlock { Text = c.Status switch { CheckStatus.Ok => "✓", CheckStatus.Info => "i", _ => "!" }, FontWeight = FontWeight.Bold, FontSize = 14, Foreground = StatusBrush(c.Status), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var titleLine = new WrapPanel { Children = {
            new TextBlock { Text = c.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 10, 2), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
            UiKit.Tag(GameLaunchChecks.StatusText(c.Status), StatusBrush(c.Status), "GcState_" + c.Id) } };
        var found = new TextBlock { Name = "GcFound_" + c.Id, Text = c.Found, FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        var fixStatus = new TextBlock { Name = "GcFixStatus_" + c.Id, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, IsVisible = false };
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (services.RunUserFix is not null)
            for (int i = 0; i < c.Fixes.Count; i++)
                AddFix(actions, fixStatus, c, c.Fixes[i], i);
        if (c.LinkPage is { } page && services.OpenPage is { } openPage)
        {
            var link = new Button { Name = "GcLink_" + c.Id, Content = c.LinkLabel ?? L.T("Abrir"), FontSize = 12, Padding = new Thickness(14, 7) };
            link.Click += (_, _) => openPage(page);
            actions.Children.Add(link);
        }
        var chevron = UiKit.Icon(open ? Icons.ChevronDown : Icons.ChevronRight, 14, Palette.Faint); chevron.VerticalAlignment = VerticalAlignment.Center;
        var head = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        var middle = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Children = { titleLine, found } };
        Grid.SetColumn(middle, 1); Grid.SetColumn(actions, 2); Grid.SetColumn(chevron, 3); chevron.Margin = new Thickness(12, 0, 0, 0);
        head.Children.Add(icon); head.Children.Add(middle); head.Children.Add(actions); head.Children.Add(chevron);

        var detail = new StackPanel { Name = "GcDetail_" + c.Id, Spacing = 6, Margin = new Thickness(42, 10, 0, 0), IsVisible = open };
        detail.Children.Add(new TextBlock { Text = c.Explanation, FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        detail.Children.Add(new TextBlock { Text = L.T("Risco: ") + c.Risk, FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        if (c.Warning is { } w)
            detail.Children.Add(new Border { Name = "GcWarning_" + c.Id, Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 8), Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock { Text = L.T("Atenção: ") + w + L.T(" O Ilmenyx nunca aplica isto sozinho."), Foreground = Palette.Warn, FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap } });
        if (c.Commands.Count > 0)
        {
            var commands = new StackPanel { Name = "GcCommands_" + c.Id, Spacing = 8, Margin = new Thickness(0, 4, 0, 0), IsVisible = false };
            int k = 0;
            foreach (var (label, command) in c.Commands)
            {
                var box = new TextBox { Name = $"GcCmd_{c.Id}_{k}", Text = command, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = IlmenyxTheme.Mono, FontSize = 11.5 };
                AutomationProperties.SetName(box, label);
                var copy = new Button { Name = $"GcCopy_{c.Id}_{k}", Content = L.T("Copiar comando"), FontSize = 11.5, Padding = new Thickness(12, 7), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
                AutomationProperties.SetName(copy, L.T("Copiar: ") + label);
                string cmd = command;
                copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(cmd); fixStatus.Text = L.T("Comando copiado. Cole no Terminal e confira antes de executar; comandos com sudo pedem a sua senha."); fixStatus.Foreground = Palette.Muted; fixStatus.IsVisible = true; } };
                var row = new Grid { ColumnDefinitions = new("*,Auto") }; Grid.SetColumn(copy, 1); row.Children.Add(box); row.Children.Add(copy);
                commands.Children.Add(new StackPanel { Spacing = 3, Children = { new TextBlock { Text = label, FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap }, row } });
                k++;
            }
            var toggle = new Button { Name = "GcShowCommands_" + c.Id, Content = L.T("Ver comandos"), FontSize = 11.5, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 4) };
            toggle.Click += (_, _) => { commands.IsVisible = !commands.IsVisible; toggle.Content = commands.IsVisible ? L.T("Esconder comandos") : L.T("Ver comandos"); };
            detail.Children.Add(toggle);
            detail.Children.Add(commands);
        }
        var body = new StackPanel { Children = { head, detail, fixStatus } };
        fixStatus.Margin = new Thickness(42, 6, 0, 0);
        var item = new Border
        {
            Name = "GcCheck_" + c.Id, Child = body, Padding = new Thickness(18, 14), BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 0, 0, 1),
            Background = open ? Palette.Hover : Brushes.Transparent, Focusable = true,
        };
        AutomationProperties.SetName(item, c.Title + " · " + GameLaunchChecks.StatusText(c.Status));
        void Toggle()
        {
            bool now = !detail.IsVisible; detail.IsVisible = now;
            if (now) expanded.Add(c.Id); else expanded.Remove(c.Id);
            item.Background = now ? Palette.Hover : Brushes.Transparent;
            chevron.Data = Avalonia.Media.Geometry.Parse(now ? Icons.ChevronDown : Icons.ChevronRight);
        }
        head.PointerPressed += (_, e) => { if (e.Source is not Button && (e.Source as Control)?.FindAncestorOfType<Button>() is null) Toggle(); };
        item.KeyDown += (_, e) => { if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space && e.Source == item) { Toggle(); e.Handled = true; } };
        return item;
    }

    private void AddFix(WrapPanel actions, TextBlock fixStatus, GameCheck c, UserFix fix, int index)
    {
        var apply = UiKit.Primary($"GcFix_{c.Id}_{index}", fix.Button);
        var undo = new Button { Name = $"GcUndo_{c.Id}_{index}", Content = fix.UndoButton ?? L.T("Desfazer"), FontSize = 12, Padding = new Thickness(14, 7), IsVisible = false };
        apply.Click += async (_, _) =>
        {
            if (owner() is not { } window || services.RunUserFix is not { } runner) return;
            if (!await ConfirmDialog.AskAsync(window, c.Title, fix.Confirm, L.T("Continuar"), L.T("Cancelar"))) { fixStatus.Text = L.T("Cancelado: nada foi feito."); fixStatus.Foreground = Palette.Muted; fixStatus.IsVisible = true; return; }
            apply.IsEnabled = false; fixStatus.Text = L.T("Aplicando…"); fixStatus.IsVisible = true;
            var (ok, message) = await runner(fix.Arguments, lifetime);
            fixStatus.Text = ok ? L.T("Pronto. Clique em Verificar para conferir.") + (c.Id == "steamdrives" ? L.T(" Feche e abra a Steam.") : "") : message;
            fixStatus.Foreground = ok ? Palette.Success : Palette.Warn;
            apply.IsEnabled = !ok; undo.IsVisible = ok && fix.UndoArguments is not null;
        };
        undo.Click += async (_, _) =>
        {
            if (services.RunUserFix is not { } runner || fix.UndoArguments is not { } back) return;
            undo.IsEnabled = false; fixStatus.Text = L.T("Desfazendo…");
            var (ok, message) = await runner(back, lifetime);
            fixStatus.Text = ok ? "Desfeito." : message; fixStatus.Foreground = ok ? Palette.Muted : Palette.Warn;
            undo.IsEnabled = true; undo.IsVisible = !ok; apply.IsEnabled = ok;
        };
        actions.Children.Add(apply); actions.Children.Add(undo);
    }
}

/// <summary>Health ring of the game check: green for the checks in order, amber for optional warnings, red for problems.</summary>
internal sealed class StatusRing : Control
{
    private int ok, warn, bad;
    public StatusRing() { Width = 84; Height = 84; }
    public void Set(int ok, int warn, int bad) { this.ok = ok; this.warn = warn; this.bad = bad; InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        var center = new Point(42, 42); const double radius = 35, thickness = 7;
        context.DrawEllipse(null, new Pen(Palette.Hover, thickness), center, radius, radius);
        int total = ok + warn + bad;
        if (total == 0) return;
        double angle = -Math.PI / 2;
        foreach (var (count, brush) in new[] { (ok, Palette.Success), (warn, Palette.Warn), (bad, Palette.Error) })
        {
            if (count == 0) continue;
            double sweep = 2 * Math.PI * count / total;
            if (count == total) { context.DrawEllipse(null, new Pen(brush, thickness), center, radius, radius); return; }
            var start = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
            var end = new Point(center.X + radius * Math.Cos(angle + sweep), center.Y + radius * Math.Sin(angle + sweep));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open()) { g.BeginFigure(start, false); g.ArcTo(end, new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise); g.EndFigure(false); }
            context.DrawGeometry(null, new Pen(brush, thickness), geometry);
            angle += sweep;
        }
    }
}
