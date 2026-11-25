// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// "Meu PC roda isso?": compares a game's published requirements (Steam store) with this PC. No FPS estimate. Method: docs/meu-pc-roda.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>Everything the page needs from outside (injectable: the smoke test never touches the network or the real hardware).</summary>
public sealed record CanRunServices(
    Func<string, CancellationToken, Task<(string Id, string Name)?>> Find,
    Func<string, CancellationToken, Task<IReadOnlyList<(string Id, string Name)>>> Suggest,
    Func<string, CancellationToken, Task<(RequirementsFetch Result, GameRequirements? Data)>> Fetch,
    RequirementsCache Cache,
    Func<LocalPc> Hardware,
    Func<IReadOnlyList<(string Id, string Name)>> Library,
    Func<string, Avalonia.Media.Imaging.Bitmap?>? Art = null)
{
    /// <param name="art">Cover of a library game from local files only (called off the UI thread); null shows initials.</param>
    public static CanRunServices Real(Func<LocalPc> hardware, Func<IReadOnlyList<(string Id, string Name)>> library, Func<string, Avalonia.Media.Imaging.Bitmap?>? art = null)
    {
        var lookup = new SteamAppLookup(); var client = new SteamRequirementsClient();
        return new(lookup.FindAsync, async (n, ct) => await lookup.SuggestAsync(n, ct), client.FetchAsync, new RequirementsCache(RequirementsCache.DefaultFolder()), hardware, library, art);
    }
}

public sealed class CanRunPage : StackPanel
{
    private readonly CanRunServices services;
    private readonly CancellationToken lifetime;
    private readonly TextBox search = new() { Name = "CanRunSearch", PlaceholderText = L.T("Nome do jogo, AppID ou link da loja Steam"), MinWidth = 240, Padding = new Thickness(34, 7, 10, 7) };
    private readonly Button go = UiKit.Primary("BtnCanRunSearch", L.T("Buscar"), 18, 8);
    private readonly Button cancel = new() { Name = "BtnCanRunCancel", Content = L.T("Cancelar"), Padding = new Thickness(16, 8), FontSize = 12.5, IsVisible = false };
    private readonly TextBlock status = new() { Name = "CanRunStatus", Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel suggestions = new() { Name = "CanRunSuggestions", ItemSpacing = 6, LineSpacing = 6 };
    private readonly StackPanel recentBox = new() { Name = "CanRunRecentBox", Spacing = 6 };
    private readonly WrapPanel recent = new() { Name = "CanRunRecent", ItemSpacing = 6, LineSpacing = 6 };
    private readonly StackPanel libraryBox = new() { Name = "CanRunLibraryBox", Spacing = 6, IsVisible = false };
    private readonly StackPanel library = new() { Name = "CanRunLibrary", Orientation = Orientation.Horizontal, Spacing = 14 };
    private readonly StackPanel result = new() { Name = "CanRunResult", Spacing = 0 };
    private CancellationTokenSource? running;
    private GameRequirements? shown;
    private bool linuxList;
    private LocalPc? pc;

    public GameRequirements? Shown => shown;
    public CanRunReport? Report { get; private set; }
    public string StatusText => status.Text ?? "";

    public CanRunPage(CanRunServices services, CancellationToken lifetime)
    {
        this.services = services; this.lifetime = lifetime;
        Children.Add(UiKit.PageHeader(L.T("Meu PC roda isso?"), L.T("Compara os requisitos publicados de um jogo com o hardware deste PC. Não estima FPS.")));

        var icon = UiKit.Icon(Icons.Search, 15, Palette.Faint); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); icon.IsHitTestVisible = false;
        AutomationProperties.SetName(search, L.T("Jogo a consultar: nome, AppID ou link da Steam"));
        search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = SearchAsync(search.Text ?? ""); } };
        go.Click += (_, _) => _ = SearchAsync(search.Text ?? "");
        cancel.Click += (_, _) => running?.Cancel();
        var box = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8, Children = { new Grid { Children = { search, icon } } } };
        Grid.SetColumn(go, 1); Grid.SetColumn(cancel, 2); box.Children.Add(go); box.Children.Add(cancel);
        recentBox.Children.Add(UiKit.Label(L.T("CONSULTADOS ANTES (SEM REDE)"))); recentBox.Children.Add(recent);
        // Console-style shelf: covers in one row that scrolls sideways; one Tab stop, arrows move between covers.
        KeyboardNavigation.SetTabNavigation(library, KeyboardNavigationMode.Once); AutomationProperties.SetName(library, L.T("Jogos da sua biblioteca Steam"));
        library.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right) || TopLevel.GetTopLevel(library)?.FocusManager?.GetFocusedElement() is not Button f) return;
            var list = library.Children.OfType<Button>().ToList(); int i = list.IndexOf(f); if (i < 0) return;
            list[Math.Clamp(i + (e.Key == Key.Right ? 1 : -1), 0, list.Count - 1)].Focus(NavigationMethod.Directional); e.Handled = true;
        };
        library.Margin = new Thickness(8, 8, 8, 12);
        libraryBox.Children.Add(UiKit.Label(L.T("JOGOS DA SUA BIBLIOTECA STEAM")));
        libraryBox.Children.Add(new ScrollViewer { Content = library, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { box, status, suggestions, recentBox, libraryBox,
            Faint(L.T("Só o nome ou o AppID vai para a Steam, e só quando você clica em Buscar. Jogos já consultados abrem do cache local, sem rede.")) } }));
        Children.Add(result);
        Children.Add(UiKit.Card(new StackPanel { Spacing = 4, Children = { UiKit.Label(L.T("ANTES DE CONFIAR NO RESULTADO")), new TextBlock
        {
            Name = "CanRunNotice", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted,
            Text = L.T("É uma comparação de requisitos publicados pelo desenvolvedor, não uma medição: o Ilmenyx não roda o jogo nem estima FPS. A compatibilidade com Linux e Proton não é garantida; vale conferir o ProtonDB (protondb.com) na página Compatibilidade ou no navegador.")
        } } }));
        DetachedFromVisualTree += (_, _) => running?.Cancel();
        AttachedToVisualTree += async (_, _) => await LoadShortcutsAsync();
    }

    private static TextBlock Faint(string text) => new() { Text = text, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap };

    // --- shortcuts: cache (no network) and the Steam library -----------------------------------------------------------------------------------

    private async Task LoadShortcutsAsync()
    {
        RefreshRecent();
        try
        {
            var games = await Task.Run(services.Library, lifetime);
            library.Children.Clear();
            var shelf = games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Take(40).ToList();
            var faces = new List<Border>();
            foreach (var (id, name) in shelf) { var (cover, face) = Cover(id, name, () => _ = SearchAsync(id)); faces.Add(face); library.Children.Add(cover); }
            libraryBox.IsVisible = library.Children.Count > 0;
            // Covers come from local files (Steam's cache); decoded off the UI thread, then painted over the initials.
            if (services.Art is { } art)
            {
                var pictures = await Task.Run(() => shelf.Select(g => { try { return art(g.Id); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; } }).ToList(), lifetime);
                for (int i = 0; i < faces.Count && i < pictures.Count; i++)
                    if (pictures[i] is { } bmp) { faces[i].Background = new ImageBrush { Source = bmp, Stretch = Stretch.UniformToFill }; faces[i].Child = null; }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException) { }
    }

    private void RefreshRecent()
    {
        recent.Children.Clear();
        foreach (var d in services.Cache.Recent()) recent.Children.Add(Chip(d.Name.Length > 0 ? d.Name : d.AppId, () => Show(d, fromCache: true), "CanRunRecent_" + d.AppId));
        recentBox.IsVisible = recent.Children.Count > 0;
    }

    private static Button Chip(string text, Action click, string name)
    {
        var b = new Button { Name = name, Content = text, FontSize = 11.5, Padding = new Thickness(13, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
        UiKit.ChipStyle(b, false); b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A game of the shelf: 2:3 cover (initials until the art is read) with the console-style white ring on hover and focus, and its name under it.</summary>
    private static (Button Button, Border Face) Cover(string id, string name, Action click)
    {
        const double W = 112, H = 168, Ring = 3, Gap = 3;
        var face = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Palette.Card,
            Child = new TextBlock { Text = GameArt.Initials(name), FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var ring = new Border { CornerRadius = new CornerRadius(12 + Ring + Gap), BorderBrush = Palette.Fg, BorderThickness = new Thickness(Ring), Margin = new Thickness(-(Ring + Gap)), Opacity = 0, IsHitTestVisible = false,
            Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = IlmenyxTheme.Fast, Easing = IlmenyxTheme.Premium }] };
        var cover = new Grid { Width = W, Height = H, Children = { ring, face } };
        var b = new Button
        {
            Name = "CanRunLib_" + id, Width = W, Padding = new Thickness(0), CornerRadius = new CornerRadius(0), ClipToBounds = false /* buttons clip by default: the ring is drawn outside the cover */, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel { Children = { cover, new TextBlock { Text = name, FontSize = 11.5, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 10, 2, 0) } } },
        };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" }) b.Resources[key] = Brushes.Transparent;
        foreach (var key in new[] { "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" }) b.Resources[key] = Brushes.Transparent;
        AutomationProperties.SetName(b, name); ToolTip.SetTip(b, name);
        bool hover = false, focus = false; void Paint() => ring.Opacity = focus ? 1 : hover ? 0.45 : 0;
        b.PointerEntered += (_, _) => { hover = true; Paint(); }; b.PointerExited += (_, _) => { hover = false; Paint(); };
        b.GotFocus += (_, _) => { focus = true; Paint(); }; b.LostFocus += (_, _) => { focus = false; Paint(); };
        b.Click += (_, _) => click();
        return (b, face);
    }

    // --- search ---------------------------------------------------------------------------------------------------------------------------------

    private void Busy(bool on) { go.IsEnabled = !on; cancel.IsVisible = on; if (on) suggestions.Children.Clear(); }

    /// <summary>Looks the game up (cache first, the Steam only when needed) and shows the comparison. A newer search cancels the older one.</summary>
    public async Task SearchAsync(string query, bool force = false)
    {
        query = query.Trim();
        if (query.Length < 2) { status.Text = L.T("Digite o nome, o AppID ou o link da loja Steam do jogo."); return; }
        running?.Cancel();
        var cts = running = CancellationTokenSource.CreateLinkedTokenSource(lifetime); var ct = cts.Token;
        Busy(true);
        try
        {
            string? appId = GameRequirementsParser.AppIdFrom(query);
            if (appId is null && !force && services.Cache.FindByName(query) is { } byName) { Show(byName, fromCache: true); return; }
            if (appId is null)
            {
                status.Text = L.T("Procurando o jogo na loja da Steam…");
                var found = await services.Find(query, ct);
                if (found is null)
                {
                    status.Text = L.T("Não achei um jogo com esse nome exato na Steam. Confira a grafia, use o AppID ou o link da loja.");
                    var list = await services.Suggest(query, ct);
                    foreach (var (id, name) in list) suggestions.Children.Add(Chip(L.T($"Você quis dizer {name}?"), () => { search.Text = name; _ = SearchAsync(id); }, "CanRunSuggest_" + id));
                    return;
                }
                appId = found.Value.Id;
            }
            if (!force && services.Cache.Load(appId) is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < RequirementsCache.FreshFor) { Show(cached, fromCache: true); return; }
            status.Text = L.T("Buscando os requisitos na Steam…");
            var (res, data) = await services.Fetch(appId, ct);
            if (res == RequirementsFetch.Ok && data is not null) { services.Cache.Save(data); RefreshRecent(); Show(data, fromCache: false); return; }
            if (services.Cache.Load(appId) is { } old) { Show(old, fromCache: true, note: L.T(" A Steam não respondeu; mostrando a cópia salva.")); return; }
            status.Text = res switch
            {
                RequirementsFetch.NotFound => L.T("A Steam não tem um jogo com esse AppID, ou não publica requisitos para ele."),
                RequirementsFetch.TooLarge => L.T("A resposta da Steam passou do limite de tamanho e foi descartada."),
                RequirementsFetch.Invalid => L.T("AppID inválido."),
                _ => L.T("Sem conexão ou a Steam não respondeu. Tente de novo; jogos já consultados abrem sem rede."),
            };
            result.Children.Clear(); shown = null; Report = null;
        }
        catch (OperationCanceledException) { if (ReferenceEquals(running, cts)) status.Text = L.T("Busca cancelada."); }
        finally { if (ReferenceEquals(running, cts)) Busy(false); }
    }

    // --- result ---------------------------------------------------------------------------------------------------------------------------------

    private async void Show(GameRequirements d, bool fromCache, string note = "")
    {
        shown = d; linuxList = d.PcMinimum.Length == 0 && d.PcRecommended.Length == 0 && d.HasLinux;
        status.Text = (fromCache ? L.T($"{d.Name}: requisitos da cópia salva em {d.FetchedAt.ToLocalTime():dd/MM/yyyy}, sem usar a rede.") : L.T($"{d.Name}: requisitos lidos da Steam agora.")) + note;
        Busy(false);
        try { pc = await Task.Run(services.Hardware, lifetime); } catch (OperationCanceledException) { return; }
        Render();
    }

    private void Render()
    {
        result.Children.Clear();
        if (shown is not { } d || pc is not { } hw) return;
        if (!d.HasAny) { Report = null; result.Children.Add(UiKit.Card(new TextBlock { Text = L.T("A Steam não publica requisitos de sistema para este jogo."), TextWrapping = TextWrapping.Wrap })); return; }
        string minText = linuxList ? d.LinuxMinimum : d.PcMinimum, recText = linuxList ? d.LinuxRecommended : d.PcRecommended;
        var min = GameRequirementsParser.Parse(minText); var rec = GameRequirementsParser.Parse(recText);
        var report = Report = CanRunCompare.Compare(min, rec, hw);

        var (label, mark, tone) = VerdictLook(report);
        var tint = ((ISolidColorBrush)tone).Color;
        var head = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(new TextBlock { Name = "CanRunGameName", Text = d.Name, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        head.Children.Add(new TextBlock { Name = "CanRunVerdictLabel", Text = label, FontSize = 28, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Foreground = tone });
        // The full sentence from the comparison (which items are at the limit or below); not repeated when the label already says it.
        head.Children.Add(new TextBlock { Name = "CanRunVerdict", Text = report.Headline, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Fg, IsVisible = report.Headline != label });
        if (report.NotCompared > 0 && report.Verdict != CanRunVerdict.NoData)
            head.Children.Add(new TextBlock { Text = L.T($"{report.NotCompared} item(ns) não deu para comparar (veja abaixo)."), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        if (d.HasLinux && (d.PcMinimum.Length > 0 || d.PcRecommended.Length > 0))
        {
            var chips = new UiKit.ChipGroup<bool>("CanRunSourcePanel", [(L.T("Requisitos do Windows (Proton)"), false), (L.T("Requisitos do Linux (nativo)"), true)], linuxList, v => { linuxList = v; Render(); });
            AutomationProperties.SetName(chips.Panel, L.T("Lista de requisitos usada na comparação"));
            actions.Children.Add(chips.Panel);
        }
        var refresh = new Button { Name = "BtnCanRunRefresh", Content = L.T("Atualizar da Steam"), Padding = new Thickness(14, 6), FontSize = 12, CornerRadius = new CornerRadius(IlmenyxTheme.Pill) };
        refresh.Click += (_, _) => _ = SearchAsync(d.AppId, force: true);
        actions.Children.Add(refresh);
        head.Children.Add(actions);
        var badge = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Background = new SolidColorBrush(Color.FromArgb(0x33, tint.R, tint.G, tint.B)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0),
            Child = new TextBlock { Text = mark, FontSize = 34, FontWeight = FontWeight.Bold, Foreground = tone, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var verdictBlock = new Border
        {
            Name = "CanRunVerdictBlock", CornerRadius = new CornerRadius(IlmenyxTheme.ContainerRadius), Padding = new Thickness(26, 22), Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(Color.FromArgb((byte)(Palette.IsLight ? 0x1C : 0x26), tint.R, tint.G, tint.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, tint.R, tint.G, tint.B)), BorderThickness = new Thickness(1),
            Child = new DockPanel { Children = { badge, head } },
        };
        DockPanel.SetDock(badge, Dock.Left);
        AutomationProperties.SetName(verdictBlock, d.Name + ": " + report.Headline);
        result.Children.Add(verdictBlock);
        if (report.Items.Any(i => MeterOf(i) is not null))
            result.Children.Add(new TextBlock { Name = "CanRunLegend", Text = Palette.IsLight ? L.T("Marca cinza = mínimo · escura = recomendado") : L.T("Marca cinza = mínimo · branca = recomendado"), FontSize = 12, Foreground = Palette.Muted, Margin = new Thickness(4, 0, 0, 8) });

        foreach (var it in report.Items) result.Children.Add(ItemCard(it));
        var unrec = min.Unrecognised.Concat(rec.Unrecognised).Distinct().ToList();
        if (unrec.Count > 0)
            result.Children.Add(UiKit.Card(new StackPanel { Spacing = 4, Children = { UiKit.Label(L.T("NÃO DEU PARA COMPARAR")),
                new TextBlock { Name = "CanRunUnrecognised", Text = string.Join("\n", unrec), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted } } }));
        result.Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { UiKit.Label(L.T("TEXTO ORIGINAL DA STEAM (PARA CONFERIR)")), RawBlock(L.T("Mínimos"), minText, "CanRunRawMin"), RawBlock(L.T("Recomendados"), recText, "CanRunRawRec") } }));
    }

    /// <summary>The big verdict: label, mark and token colour. "No limite" when the overall result is the minimum but something is only at the limit (the comparison's own near-limit list).</summary>
    internal static (string Label, string Mark, IBrush Tone) VerdictLook(CanRunReport r) => r.Verdict switch
    {
        CanRunVerdict.Recommended => (L.T("Roda no recomendado"), "✓", Palette.Success),
        CanRunVerdict.Minimum when r.NearTitles is { Count: > 0 } => (L.T("No limite"), "≈", Palette.Warn),
        CanRunVerdict.Minimum => (L.T("Roda no mínimo"), "✓", Palette.AccentText),
        CanRunVerdict.BelowMinimum => (L.T("Abaixo do mínimo"), "✗", Palette.Error),
        _ => (L.T("Sem dados suficientes"), "?", Palette.Muted),
    };

    internal static MeterLayout? MeterOf(CanRunItem it) => it.Counts ? RequirementMeter.Layout(it.LocalValue, it.MinValue, it.RecValue) : null;

    private static Control RawBlock(string title, string html, string name)
    {
        string text = string.Join("\n", GameRequirementsParser.Lines(html));
        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12.5 },
            new SelectableTextBlock { Name = name, Text = text.Length > 0 ? text : L.T("A Steam não publica esta lista."), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted } } };
    }

    private static (string Text, string Mark, IBrush Brush) StatusLook(ReqStatus s, bool shared) => s switch
    {
        ReqStatus.MeetsRec => (shared ? L.T("Atende (compartilhada)") : L.T("Atende o recomendado"), "✓", Palette.Success),
        ReqStatus.MeetsMin => (shared ? L.T("Atende (compartilhada)") : L.T("Atende o mínimo"), "✓", Palette.AccentText),
        ReqStatus.NearRec => (shared ? L.T("No limite (compartilhada)") : L.T("No limite do recomendado"), "≈", Palette.Warn),
        ReqStatus.NearMin => (shared ? L.T("No limite (compartilhada)") : L.T("No limite do mínimo"), "≈", Palette.Warn),
        ReqStatus.Below => (L.T("Abaixo do mínimo"), "✗", Palette.Error),
        _ => (L.T("Não deu para comparar"), "?", Palette.Muted),
    };

    private static Border Pill(string text, IBrush fg)
    {
        var c = ((ISolidColorBrush)fg).Color;
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb((byte)(Palette.IsLight ? 0x1C : 0x26), c.R, c.G, c.B)), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(10, 3),
            HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = text, Foreground = fg, FontSize = 11, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
        };
    }

    private static Control Fact(string label, string value) => new StackPanel
    {
        Spacing = 1, Margin = new Thickness(0, 0, 24, 4), MaxWidth = 340,
        Children = { UiKit.Label(label), new TextBlock { Text = value.Length > 0 ? value : "—", FontSize = 12.5, TextWrapping = TextWrapping.Wrap } },
    };

    private static Border ItemCard(CanRunItem it)
    {
        var (text, mark, brush) = StatusLook(it.Status, it.Shared);
        if (it.Basis is { } basis) text += " · " + CanRunCompare.BasisLabel(basis);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new TextBlock { Text = mark, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = brush, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = it.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } } };
        var facts = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Children = { Fact(L.T("MÍNIMO"), it.Minimum), Fact(L.T("RECOMENDADO"), it.Recommended), Fact(L.T("ESTE PC"), it.Local) } };
        var pill = new Border { Margin = new Thickness(0, 6, 0, 0), Child = Pill(it.Counts ? text : L.T("Informativo"), it.Counts ? brush : Palette.Muted) };
        var meter = MeterOf(it) is { } layout ? new RequirementMeterBar(layout, brush) { Name = "CanRunMeter_" + it.Key, Margin = new Thickness(0, 10, 0, 0) } : null;
        var card = UiKit.Card(new StackPanel { Children = { title, pill, meter ?? (Control)new Panel(), facts,
            new TextBlock { Text = it.Note, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) } } }, new Thickness(0, 0, 0, 8));
        card.Name = "CanRunItem_" + it.Key;
        AutomationProperties.SetName(card, it.Counts ? $"{it.Title}: {text}" : it.Title);
        return card;
    }
}
