// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions (layout follows the original Games page: search, options, data source with API key, game card grid; cinematic surfaces since 2026-11-06): see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// FPS estimate page. It scrolls by itself (the shell does not wrap it): the page scrolls until the "Jogos do catálogo" header reaches
/// the top, then that header stays fixed and only the catalogue (as tall as the window) scrolls; going up, the catalogue returns to its
/// own top first (<see cref="ScrollChain"/>). A round button in the bottom-right corner returns to the top.
/// </summary>
public sealed class GamesFpsPage : Grid
{
    private const double WheelStep = 50, MinCatalogHeight = 260, ToTopThreshold = 160, ToTopMs = 320;
    private readonly StackPanel top = new() { Name = "FpsTop", Spacing = 4 };
    private readonly ScrollViewer outer = new() { Name = "FpsScroll", Focusable = true, IsTabStop = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly ScrollViewer cardsScroll = new() { Name = "GameCardsScroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Grid catalogHeader = new() { Name = "FpsCatalogHeader", ColumnDefinitions = new("*,Auto"), Margin = new Thickness(2, 18, 2, 10) };
    private readonly TextBlock catalogCount = new() { Name = "FpsCatalogCount", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button toTop = new() { Name = "BtnFpsToTop" };
    private readonly DispatcherTimer anim = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private (double Outer, double Inner, DateTime Start) animFrom;
    private readonly HowManyFpsClient api;
    private readonly IlmenyxSettings settings;
    private readonly string settingsPath;
    private readonly Func<Window?> owner;
    private readonly Func<(string Cpu, string Gpu)> hardware;
    private readonly CancellationToken lifetime;
    private readonly List<GameFpsRow> rows = GameCatalog.Games.Select(g => new GameFpsRow(g)).ToList();
    private readonly WrapPanel cards = new() { Name = "GameCards", ItemSpacing = 10, LineSpacing = 10 };
    private double cardWidth = 222;
    private readonly TextBlock sub = new() { Name = "GamesSub", Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Name = "GameSearch", Width = 240, PlaceholderText = "Buscar jogo…", Padding = new Thickness(34, 7, 10, 7) };
    private readonly TextBlock sourceTitle = new() { Name = "GameSourceTitle", FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock sourceText = new() { Name = "GameSourceText", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBox keyBox = new() { Name = "ApiKeyBox", PasswordChar = '•', Width = 240, PlaceholderText = "Chave da API HowManyFPS" };
    private readonly Button saveKey = new() { Name = "BtnApiKey", Content = "SALVAR CHAVE", Padding = new Thickness(16, 8), FontSize = 12.5 };
    private readonly Button query = UiKit.Primary("BtnFpsQuery", "CONSULTAR API", 16, 8);
    private string res = "1920x1080", preset = "Alto", upscaling = "Native";
    private double gpuIdx = 50, cpuIdx = 80;
    // Unknown hardware used to fall back to fixed indexes (GPU 50 ≈ RTX 3050, CPU 80) and showed invented FPS; now it says so.
    private string? unknownHardware;
    private FpsAssessment assessment = new(FpsConfidence.Low, FpsAccuracy.LowSpread, []);
    private IReadOnlyList<FpsSession> sessions = [];
    private readonly Func<FpsContext?>? context;
    private readonly StackPanel alertList = new() { Name = "FpsAlerts", Spacing = 8 };
    private readonly Border alertCard;
    public IReadOnlyList<GameFpsRow> Rows => rows;
    public FpsAssessment Assessment => assessment;
    public IReadOnlyList<FpsAlert> Alerts { get; private set; } = [];
    public double GpuIndex => gpuIdx; public double CpuIndex => cpuIdx;
    /// <summary>Page scroll (above the catalogue) and catalogue scroll offsets, for the UI smoke test.</summary>
    public double PageOffset => outer.Offset.Y; public double CatalogOffset => cardsScroll.Offset.Y;
    public double PageMax => Math.Max(0, outer.Extent.Height - outer.Viewport.Height); public double CatalogMax => Math.Max(0, cardsScroll.Extent.Height - cardsScroll.Viewport.Height);
    public double CatalogViewport => cardsScroll.Viewport.Height;
    public bool ToTopVisible => toTop.IsVisible;

    public GamesFpsPage(HowManyFpsClient api, IlmenyxSettings settings, string settingsPath, Func<Window?> owner, Func<(string Cpu, string Gpu)> hardware, CancellationToken lifetime, Func<FpsContext?>? context = null)
    {
        this.context = context; this.api = api; this.settings = settings; this.settingsPath = settingsPath; this.owner = owner; this.hardware = hardware; this.lifetime = lifetime;
        query.IsEnabled = false;
        var icon = UiKit.Icon(Icons.Search, 15, Palette.Faint); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); icon.IsHitTestVisible = false;
        var header = UiKit.PageHeader("Estimativa de FPS");
        ((StackPanel)header.Children[0]).Children.Add(sub);
        search.TextChanged += (_, _) => Filter();
        top.Children.Add(header);
        var resChips = new UiKit.ChipGroup<string>("GameResPanel", [("1080p", "1920x1080"), ("1440p", "2560x1440"), ("4K", "3840x2160")], res, v => { res = v; RefreshLocal(); });
        var presetChips = new UiKit.ChipGroup<string>("GamePresetPanel", [("Baixo", "Baixo"), ("Médio", "Médio"), ("Alto", "Alto"), ("Ultra", "Ultra")], preset, v => { preset = v; RefreshLocal(); });
        var upChips = new UiKit.ChipGroup<string>("GameUpscalePanel", [("Nativo", "Native"), ("Qualidade", "Quality"), ("Desempenho", "Performance")], upscaling, v => { upscaling = v; RefreshLocal(); });
        Control Opt(string label, Control chips) => new StackPanel { Margin = new Thickness(0, 0, 28, 4), Children = { UiKit.Label(label), new Border { Height = 6 }, chips } };
        top.Children.Add(UiKit.Card(new WrapPanel { LineSpacing = 10, Children = { Opt("RESOLUÇÃO", resChips.Panel), Opt("GRÁFICOS", presetChips.Panel), Opt("UPSCALING (DLSS / FSR)", upChips.Panel) } }));
        AutomationProperties.SetName(keyBox, "Chave da API HowManyFPS");
        saveKey.Click += (_, _) => SaveKey(); query.Click += async (_, _) => await QueryAsync();
        var keyRow = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { keyBox, saveKey, query } };
        var srcHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UiKit.Icon(Icons.Network, 15, Palette.AccentText), sourceTitle } };
        top.Children.Add(UiKit.Card(new StackPanel { Children = { srcHead, sourceText, keyRow } }));
        var alertHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UiKit.Icon(Icons.Blocked, 15, Palette.Warn), new TextBlock { Text = "O que pode derrubar o FPS neste PC", FontWeight = FontWeight.SemiBold, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center } } };
        alertCard = UiKit.Card(new StackPanel { Spacing = 10, Children = { alertHead, alertList } }); alertCard.IsVisible = false;
        top.Children.Add(alertCard);
        // Catalogue header: it becomes the fixed bar at the top once the page above has scrolled away; the search stays next to the games.
        var title = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "Jogos do catálogo", FontSize = 14, FontWeight = FontWeight.SemiBold }, catalogCount } };
        var searchBox = new Grid { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { search, icon } }; Grid.SetColumn(searchBox, 1);
        catalogHeader.Children.Add(title); catalogHeader.Children.Add(searchBox);
        cardsScroll.Content = cards;
        // Responsive grid: as many ~220 px columns as fit, stretched to fill the row.
        cardsScroll.SizeChanged += (_, e) =>
        {
            double avail = e.NewSize.Width - 2; int cols = Math.Max(2, (int)((avail + 10) / 230));
            double w = Math.Floor((avail - 10 * (cols - 1)) / cols);
            if (Math.Abs(w - cardWidth) < 0.5) return;
            cardWidth = w; foreach (var c in cards.Children) c.Width = w;
        };
        outer.Content = new StackPanel { Margin = new Thickness(0, 0, 8, 12), Children = { top, catalogHeader, cardsScroll } };
        // The catalogue is exactly as tall as the window below its header, so when the header sticks it fills the page and is never tiny.
        outer.SizeChanged += (_, _) => FitCatalog(); catalogHeader.SizeChanged += (_, _) => FitCatalog();
        outer.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        outer.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        outer.ScrollChanged += (_, _) => UpdateToTop(); cardsScroll.ScrollChanged += (_, _) => UpdateToTop();
        BuildToTop(); anim.Tick += (_, _) => AnimateStep();
        Children.Add(outer); Children.Add(toTop);
        api.ApiKey = settings.HowManyFpsKey; keyBox.Text = api.Configured ? settings.HowManyFpsKey : ""; Loaded += (_, _) => Init();
    }

    private void FitCatalog()
    {
        var m = catalogHeader.Margin;
        double h = Math.Max(MinCatalogHeight, Math.Floor(outer.Bounds.Height - catalogHeader.Bounds.Height - m.Top - m.Bottom - 12));
        if (Math.Abs(cardsScroll.Height - h) >= 1 || double.IsNaN(cardsScroll.Height)) cardsScroll.Height = h;
    }

    private void BuildToTop()
    {
        const string arrowUp = "M13,20H11V8L5.5,13.5L4.08,12.08L12,4.16L19.92,12.08L18.5,13.5L13,8V20Z"; // mdiArrowUp
        toTop.Width = toTop.Height = 46; toTop.Padding = new Thickness(0); toTop.CornerRadius = new CornerRadius(23);
        toTop.HorizontalAlignment = HorizontalAlignment.Right; toTop.VerticalAlignment = VerticalAlignment.Bottom; toTop.Margin = new Thickness(0, 0, 26, 26);
        toTop.HorizontalContentAlignment = HorizontalAlignment.Center; toTop.VerticalContentAlignment = VerticalAlignment.Center;
        toTop.Background = Palette.Accent; toTop.Content = UiKit.Icon(arrowUp, 20, Palette.OnAccent); toTop.IsVisible = false;
        AutomationProperties.SetName(toTop, "Voltar ao topo"); ToolTip.SetTip(toTop, "Voltar ao topo");
        void Shadow() => toTop.Effect = new DropShadowEffect { OffsetY = 6, BlurRadius = 18, Color = Color.FromArgb((byte)(Palette.IsLight ? 0x40 : 0x80), 0, 0, 0) };
        Shadow(); toTop.ActualThemeVariantChanged += (_, _) => Shadow();
        toTop.Click += (_, _) => ScrollToTop();
    }

    private void UpdateToTop() => toTop.IsVisible = outer.Offset.Y + cardsScroll.Offset.Y > ToTopThreshold;

    /// <summary>Scrolls by <paramref name="delta"/> pixels (positive = down), chained: page first going down, catalogue first going up.</summary>
    public void ScrollBy(double delta)
    {
        anim.Stop();
        var (o, i) = ScrollChain.Step(outer.Offset.Y, PageMax, cardsScroll.Offset.Y, CatalogMax, delta);
        if (o != outer.Offset.Y) outer.Offset = new Vector(0, o);
        if (i != cardsScroll.Offset.Y) cardsScroll.Offset = new Vector(0, i);
    }

    /// <summary>Back to the top of the page (catalogue and page), with a short eased animation.</summary>
    public void ScrollToTop()
    {
        animFrom = (outer.Offset.Y, cardsScroll.Offset.Y, DateTime.UtcNow);
        anim.Start();
    }

    private void AnimateStep()
    {
        double t = (DateTime.UtcNow - animFrom.Start).TotalMilliseconds / ToTopMs;
        double k = 1 - ScrollChain.EaseOut(t);
        outer.Offset = new Vector(0, animFrom.Outer * k); cardsScroll.Offset = new Vector(0, animFrom.Inner * k);
        if (t >= 1) { anim.Stop(); outer.Offset = cardsScroll.Offset = default; }
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        ScrollBy(-e.Delta.Y * WheelStep); e.Handled = true;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        bool inText = e.Source is TextBox;
        double page = Math.Max(WheelStep, outer.Viewport.Height * 0.85);
        switch (e.Key)
        {
            case Key.PageDown: ScrollBy(page); break;
            case Key.PageUp: ScrollBy(-page); break;
            case Key.Down when !inText: ScrollBy(WheelStep); break;
            case Key.Up when !inText: ScrollBy(-WheelStep); break;
            // Home/End go to the very top/bottom; inside the search box only with Ctrl (plain Home/End move the text cursor there).
            case Key.Home when inText ? e.KeyModifiers == KeyModifiers.Control : e.KeyModifiers is KeyModifiers.None or KeyModifiers.Control: ScrollToTop(); break;
            case Key.End when inText ? e.KeyModifiers == KeyModifiers.Control : e.KeyModifiers is KeyModifiers.None or KeyModifiers.Control: ScrollBy(PageMax + CatalogMax + 1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Init()
    {
        var (cpu, gpu) = hardware();
        var ctx = context?.Invoke();
        var gm = GameCatalog.GpuMatch(gpu); var cm = GameCatalog.CpuMatch(cpu);
        double? g = gm?.Index, c = cm?.Index;
        gpuIdx = g ?? 50; cpuIdx = c ?? 80;
        if (gm is { } hit) assessment = FpsAccuracy.Assess(FpsAccuracy.ClassifyGpu(gpu, hit.Key, ctx?.GpuIntegrated == true), gpuIdx, FpsAccuracy.IsLaptopCpu(cpu), cpuIdx);
        // Sessions are re-read on every visit: the Launcher records them in the same settings file after a game closes.
        sessions = File.Exists(settingsPath) ? IlmenyxSettings.Load(settingsPath).FpsSessions : settings.FpsSessions;
        Alerts = ctx is null ? [] : FpsAlerts.Build(ctx);
        alertList.Children.Clear();
        foreach (var a in Alerts)
            alertList.Children.Add(new StackPanel { Spacing = 2, Children = { new TextBlock { Text = a.Title, FontWeight = FontWeight.SemiBold, FontSize = 12.5, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = a.Text, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap } } });
        alertCard.IsVisible = Alerts.Count > 0;
        var missing = new List<string>();
        if (g is null) missing.Add("a GPU " + (gpu.Length > 0 ? GameCatalog.ShortGpuName(gpu) : "(não identificada)"));
        if (c is null) missing.Add("a CPU " + (cpu.Length > 0 ? GameCatalog.ShortCpuName(cpu) : "(não identificada)"));
        unknownHardware = missing.Count == 0 ? null : string.Join(" e ", missing);
        sub.Text = $"FPS esperado no seu PC · {(gpu.Length > 0 ? FpsAccuracy.DisplayGpuName(gpu) : "GPU desconhecida")} · {(cpu.Length > 0 ? GameCatalog.ShortCpuName(cpu) : "CPU desconhecida")}";
        RefreshLocal();
    }

    public void RefreshLocal()
    {
        GameFpsRow.RefreshLocal(rows, gpuIdx, cpuIdx, res, preset, upscaling, assessment, sessions);
        if (unknownHardware is { } who)
        {
            foreach (var row in rows) row.Set(0, 0, "SEM ESTIMATIVA", $"{who} não está na tabela de referência do Ilmenyx");
            GameFpsRow.ApplyMeasured(rows, sessions);
        }
        int measured = rows.Count(r => r.IsMeasured);
        string measuredNote = measured == 0 ? " Para ver o FPS real deste PC, abra o jogo pela página Jogos com MangoHud ligado: ao fechar, a sessão aparece aqui como MEDIDO NESTE PC."
            : $" {measured} jogo(s) com sessão MEDIDO NESTE PC (MangoHud, pela página Jogos): esse valor vale nas configurações usadas no jogo, não nas opções acima.";
        string estimateNote = $" A faixa é uma estimativa, não uma medição ({assessment.ConfidenceText}): {string.Join("; ", assessment.Reasons)}.";
        if (unknownHardware is { } missingNow)
        {
            sourceTitle.Text = "Fonte: sem estimativa local para este PC";
            sourceText.Text = $"Não há referência de desempenho para {missingNow}, então o Ilmenyx não calcula um FPS (seria inventado). " + (api.Configured ? "CONSULTAR API busca FPS medido em sessões reais." : "Para FPS medido em jogo, use uma chave da API HowManyFPS (info@howmanyfps.com).") + measuredNote;
            saveKey.Content = api.Configured ? "ATUALIZAR CHAVE" : "SALVAR CHAVE"; query.IsEnabled = api.Configured;
        }
        else if (api.Configured)
        {
            sourceTitle.Text = "Fonte: estimativa local + HowManyFPS sob demanda"; sourceText.Text = "A estimativa local usa o desempenho relativo da sua GPU e CPU." + estimateNote + " Com a chave, CONSULTAR API busca FPS médio e 1% low medidos em sessões reais (respostas ficam 7 dias em cache); a API usa as configurações gráficas dela." + measuredNote;
            saveKey.Content = "ATUALIZAR CHAVE"; query.IsEnabled = true;
        }
        else
        {
            sourceTitle.Text = "Fonte: estimativa local (aproximada)"; sourceText.Text = "Calculada a partir do desempenho relativo da sua GPU e CPU." + estimateNote + measuredNote + " Para FPS medido por outros jogadores, peça uma chave da API em info@howmanyfps.com e cole ao lado. A internet só é usada quando você clicar em CONSULTAR API.";
            saveKey.Content = "SALVAR CHAVE"; query.IsEnabled = false;
        }
        Rebuild();
    }

    private void SaveKey()
    {
        settings.HowManyFpsKey = string.IsNullOrWhiteSpace(keyBox.Text) ? null : keyBox.Text.Trim(); api.ApiKey = settings.HowManyFpsKey;
        // Only the key is written; everything else in the file (sessions recorded by the Launcher, other pages) is kept as it is now.
        string? key = settings.HowManyFpsKey;
        try { settings.FpsSessions = IlmenyxSettings.Update(settingsPath, s => s.HowManyFpsKey = key).FpsSessions; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { sourceText.Text = "Não foi possível salvar a chave: " + e.Message; return; }
        RefreshLocal();
    }

    public async Task QueryAsync()
    {
        if (!api.Configured || owner() is not { } window) return;
        var (cpuFull, gpuFull) = hardware(); string cpu = GameCatalog.ShortCpuName(cpuFull), gpu = GameCatalog.ShortGpuName(gpuFull);
        int needed = rows.Count(r => !r.IsMeasured && !api.IsCached(cpu, gpu, r.Name, res, upscaling));
        if (needed > 0 && !await ConfirmDialog.AskAsync(window, "HowManyFPS", $"Isto envia até {needed} consulta(s) para {HowManyFpsClient.Endpoint} (CPU \"{cpu}\", GPU \"{gpu}\", resolução e upscaling escolhidos), usando a sua chave. As respostas ficam 7 dias em cache. Consultar agora?", "Consultar", "Cancelar")) return;
        query.IsEnabled = false; int ok = 0;
        try
        {
            foreach (var row in rows.Where(r => !r.IsMeasured)) // measured on this PC keeps priority
            {
                try { var r = await api.GetAsync(cpu, gpu, row.Name, res, upscaling, lifetime); row.Set(r.AvgFps, r.Min1Fps, GameFpsRow.SourceApi, ""); ok++; }
                catch (OperationCanceledException) { return; }
                catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
                {
                    row.Note($"API: {e.Message}");
                    if (e.Message.Contains("chave", StringComparison.Ordinal) || e.Message.Contains("cota", StringComparison.Ordinal)) { sourceText.Text = $"A API recusou a consulta ({e.Message}). Mostrando a estimativa local."; Rebuild(); return; }
                }
            }
            int measuredRows = rows.Count(r => r.IsMeasured);
            sourceText.Text = $"{ok} de {rows.Count} jogos com dados da HowManyFPS" + (measuredRows > 0 ? $"; {measuredRows} com sessão medida neste PC" : "") + "; os demais usam a estimativa local (faixa, não medição)."; Rebuild();
        }
        finally { query.IsEnabled = api.Configured; }
    }

    // A new search starts at the top of the list (otherwise a short result could sit above the visible area).
    private void Filter() { if (cardsScroll.Offset.Y > 0) cardsScroll.Offset = default; Rebuild(); }

    private static IBrush RatingBrush(double fps) => fps switch { <= 0 => Palette.Faint, >= 90 => Palette.Success, >= 60 => Palette.AccentText, >= 30 => Palette.Warn, _ => Palette.Error };

    /// <summary>Poster band of a card: a soft diagonal tint from the data palette (stable per genre), the reference's image strip without imagery.</summary>
    private static IBrush Band(string genre)
    {
        var tints = new[] { Palette.DataCpu, Palette.DataGpu, Palette.DataMemory, Palette.DataNetwork, Palette.DataDisk, Palette.DataTemperature };
        int h = 0; foreach (char c in genre) h = (h * 31 + c) & 0x7FFFFFFF;
        var c0 = ((ISolidColorBrush)tints[h % tints.Length]).Color;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb((byte)(Palette.IsLight ? 0x30 : 0x42), c0.R, c0.G, c0.B), 0), new GradientStop(Color.FromArgb(0, c0.R, c0.G, c0.B), 1) },
        };
    }

    private void Rebuild()
    {
        string q = (search.Text ?? "").Trim();
        cards.Children.Clear();
        int shown = rows.Count(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        string resText = res switch { "2560x1440" => "1440p", "3840x2160" => "4K", _ => "1080p" }, upText = upscaling switch { "Quality" => "Qualidade", "Performance" => "Desempenho", _ => "Nativo" };
        catalogCount.Text = (q.Length == 0 ? $"{rows.Count} jogos" : $"{shown} de {rows.Count} jogos") + $" · {resText} · {preset} · {upText}";
        foreach (var row in rows.Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            var tone = RatingBrush(row.Fps);
            var band = new Border
            {
                Background = Band(row.Genre), Padding = new Thickness(16, 14, 16, 10),
                Child = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = row.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = row.Genre, Foreground = Palette.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis } } },
            };
            var bar = new Grid { Height = 4, Margin = new Thickness(0, 8, 0, 8) };
            bar.Children.Add(new Border { Background = Palette.Line, CornerRadius = new CornerRadius(2) });
            bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(row.Bar, 0.0001), GridUnitType.Star)); bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - row.Bar, 0.0001), GridUnitType.Star));
            var track = (Border)bar.Children[0]; Grid.SetColumnSpan(track, 2);
            bar.Children.Add(new Border { Background = tone, CornerRadius = new CornerRadius(2), IsVisible = row.Bar > 0 });
            var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { new TextBlock { Text = row.FpsText, FontSize = 28, FontWeight = FontWeight.Bold }, new TextBlock { Text = "FPS", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) } } };
            var top = new Grid { ColumnDefinitions = new("*,Auto"), Children = { value } };
            if (row.Rating.Length > 0)
            {
                var rating = new TextBlock { Text = row.Rating, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = tone, LetterSpacing = 0.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(rating, 1); top.Children.Add(rating);
            }
            var meta = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = row.LowText, Foreground = Palette.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0) } } };
            var source = new TextBlock { Text = row.Source, Foreground = Palette.Faint, FontSize = 10.5, LetterSpacing = 0.4 }; Grid.SetColumn(source, 1); meta.Children.Add(source);
            var body = new StackPanel { Margin = new Thickness(16, 6, 16, 14), Children = { top, bar, meta, new TextBlock { Text = row.Detail, Foreground = Palette.Faint, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = row.Detail.Length > 0 } } };
            var card = new Border
            {
                Name = "Game_" + LauncherPage.SafeName(row.Name), Width = cardWidth, Background = Palette.Card, BorderBrush = row.IsMeasured ? Palette.Success : Palette.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), ClipToBounds = true, Child = new StackPanel { Children = { band, body } },
            };
            if (row.Summary.Length > 0) { ToolTip.SetTip(card, row.Summary); AutomationProperties.SetHelpText(card, row.Summary); }
            AutomationProperties.SetName(card, row.Name);
            cards.Children.Add(card);
        }
    }
}
