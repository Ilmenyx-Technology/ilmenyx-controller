// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Games page: search, options, data source, game card grid; cinematic surfaces since 2026-11-06;
// the HowManyFPS API key and query were removed on 2026-11-08, the page is fully local; calibration gate, spec fallback and personal
// factor since 2026-11-08, docs/fps-estimativa.md): see LICENSING.md.
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
/// <summary>Short benchmarks used to calibrate the estimate on this PC (injectable: the smoke test never runs real load).</summary>
public sealed record FpsCalibrator(Func<IProgress<(string Phase, double Fraction)>, CancellationToken, Task<FpsCalibrationMeasurement>> Run, Func<double?> CpuTemp, Func<double?> GpuTemp);

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
    private readonly IlmenyxSettings settings;
    private readonly string settingsPath;
    private readonly Func<(string Cpu, string Gpu)> hardware;
    private readonly List<GameFpsRow> rows = GameCatalog.Games.Select(g => new GameFpsRow(g)).ToList();
    private readonly WrapPanel cards = new() { Name = "GameCards", ItemSpacing = 10, LineSpacing = 10 };
    private double cardWidth = 222;
    private readonly TextBlock sub = new() { Name = "GamesSub", Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Name = "GameSearch", Width = 240, PlaceholderText = "Buscar jogo…", Padding = new Thickness(34, 7, 10, 7) };
    private readonly TextBlock sourceTitle = new() { Name = "GameSourceTitle", FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock sourceText = new() { Name = "GameSourceText", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    private string res = "1920x1080", preset = "Alto", upscaling = "Native";
    private double gpuIdx = 50, cpuIdx = 80;
    // Unknown hardware used to fall back to fixed indexes (GPU 50 ≈ RTX 3050, CPU 80) and showed invented FPS; now it says so.
    private string? unknownHardware;
    private FpsAssessment assessment = new(FpsConfidence.Low, FpsAccuracy.LowSpread, []);
    private IReadOnlyList<FpsSession> sessions = [];
    // Calibration gate (method C), spec fallback (B) and personal factor (D): docs/fps-estimativa.md.
    private readonly FpsCalibrator? calibrator;
    private FpsCalibration? calibration;
    private string fingerprint = "", gpuName = "", cpuName = "";
    private FpsContext? ctx;
    private (string Key, double Index)? gpuTable; private double? cpuTable, specIdx;
    private HardwareMatch tableMatch = HardwareMatch.Desktop; private bool laptopCpu, cpuMeasured, hasIndices;
    private FpsBasis basis = FpsBasis.Table;
    private PersonalFactor? personalGpu, personalCpu;
    private readonly TextBlock gateTitle = new() { Name = "FpsGateTitle", FontWeight = FontWeight.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock gateText = new() { Name = "FpsGateText", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button calibrate = UiKit.Primary("BtnFpsCalibrate", "CALIBRAR AGORA (≈1 min)", 18, 9);
    private readonly Button cancelCal = new() { Name = "BtnFpsCalibrateCancel", Content = "CANCELAR", Padding = new Thickness(16, 8), FontSize = 12.5, IsVisible = false };
    private readonly ProgressBar gateProgress = new() { Name = "FpsGateProgress", Minimum = 0, Maximum = 1, Height = 6, MinHeight = 6, Margin = new Thickness(0, 10, 0, 0), IsVisible = false };
    private readonly Border gateCard;
    private readonly PathIconHolder gateIcon = new();
    private CancellationTokenSource? calCts;
    private string? calAbort;
    public FpsGate Gate { get; private set; } = FpsGate.Uncalibrated;
    public FpsCalibration? Calibration => calibration;
    public FpsBasis Basis => basis;
    public PersonalFactor? PersonalGpu => personalGpu;
    public string? CalibrationMessage { get; private set; }
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

    /// <summary>Everything on this page is computed on this PC: no network use.</summary>
    public GamesFpsPage(IlmenyxSettings settings, string settingsPath, Func<(string Cpu, string Gpu)> hardware, Func<FpsContext?>? context = null, FpsCalibrator? calibrator = null)
    {
        this.context = context; this.settings = settings; this.settingsPath = settingsPath; this.hardware = hardware; this.calibrator = calibrator;
        var icon = UiKit.Icon(Icons.Search, 15, Palette.Faint); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); icon.IsHitTestVisible = false;
        var header = UiKit.PageHeader("Estimativa de FPS");
        ((StackPanel)header.Children[0]).Children.Add(sub);
        search.TextChanged += (_, _) => Filter();
        top.Children.Add(header);
        // Calibration gate: until this PC is calibrated the estimates are provisional and this card says so at the top.
        calibrate.Click += async (_, _) => await CalibrateAsync(); cancelCal.Click += (_, _) => calCts?.Cancel();
        var gateHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { gateIcon.Host, gateTitle } };
        gateCard = UiKit.Card(new StackPanel { Children = { gateHead, gateText, gateProgress, new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { calibrate, cancelCal } } } });
        gateCard.Name = "FpsGateCard";
        top.Children.Add(gateCard);
        DetachedFromVisualTree += (_, _) => calCts?.Cancel();
        var resChips = new UiKit.ChipGroup<string>("GameResPanel", [("1080p", "1920x1080"), ("1440p", "2560x1440"), ("4K", "3840x2160")], res, v => { res = v; RefreshLocal(); });
        var presetChips = new UiKit.ChipGroup<string>("GamePresetPanel", [("Baixo", "Baixo"), ("Médio", "Médio"), ("Alto", "Alto"), ("Ultra", "Ultra")], preset, v => { preset = v; RefreshLocal(); });
        var upChips = new UiKit.ChipGroup<string>("GameUpscalePanel", [("Nativo", "Native"), ("Qualidade", "Quality"), ("Desempenho", "Performance")], upscaling, v => { upscaling = v; RefreshLocal(); });
        Control Opt(string label, Control chips) => new StackPanel { Margin = new Thickness(0, 0, 28, 4), Children = { UiKit.Label(label), new Border { Height = 6 }, chips } };
        top.Children.Add(UiKit.Card(new WrapPanel { LineSpacing = 10, Children = { Opt("RESOLUÇÃO", resChips.Panel), Opt("GRÁFICOS", presetChips.Panel), Opt("UPSCALING (DLSS / FSR)", upChips.Panel) } }));
        var srcHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UiKit.Icon(Icons.About, 15, Palette.AccentText), sourceTitle } };
        top.Children.Add(UiKit.Card(new StackPanel { Children = { srcHead, sourceText } }));
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
        Loaded += (_, _) => Init();
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
        var (cpu, gpu) = hardware(); gpuName = gpu; cpuName = cpu;
        ctx = context?.Invoke();
        gpuTable = GameCatalog.GpuMatch(gpu); cpuTable = GameCatalog.CpuMatch(cpu)?.Index;
        tableMatch = gpuTable is { } hit ? FpsAccuracy.ClassifyGpu(gpu, hit.Key, ctx?.GpuIntegrated == true) : HardwareMatch.Desktop;
        laptopCpu = FpsAccuracy.IsLaptopCpu(cpu);
        specIdx = ctx is { } c && GpuSpecDb.Find(c.GpuVendorId, c.GpuDeviceId, c.GpuName) is { } spec ? FpsModels.SpecGpuIndex(spec, c.GpuMaxCoreMHz) : null;
        fingerprint = ctx is { } k ? FpsModels.Fingerprint(k.GpuVendorId, k.GpuDeviceId, k.GpuDriver, k.GpuDriverVersion, cpu) : FpsModels.Fingerprint(gpu, "", "", "", cpu);
        // Sessions and calibration are re-read on every visit: the Launcher records sessions in the same settings file after a game closes.
        var saved = File.Exists(settingsPath) ? IlmenyxSettings.Load(settingsPath) : settings;
        sessions = saved.FpsSessions; calibration = saved.FpsCalibration;
        Alerts = ctx is null ? [] : FpsAlerts.Build(ctx);
        alertList.Children.Clear();
        foreach (var a in Alerts)
            alertList.Children.Add(new StackPanel { Spacing = 2, Children = { new TextBlock { Text = a.Title, FontWeight = FontWeight.SemiBold, FontSize = 12.5, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = a.Text, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap } } });
        alertCard.IsVisible = Alerts.Count > 0;
        sub.Text = $"FPS esperado no seu PC · {(gpu.Length > 0 ? FpsAccuracy.DisplayGpuName(gpu) : "GPU desconhecida")} · {(cpu.Length > 0 ? GameCatalog.ShortCpuName(cpu) : "CPU desconhecida")}";
        ComputeIndices();
        RefreshLocal();
    }

    /// <summary>Gate state and indices: calibration of this exact hardware/driver first, then the table, then the specs (GPU only).</summary>
    private void ComputeIndices()
    {
        bool current = calibration is { } c0 && c0.Fingerprint == fingerprint;
        if (Gate != FpsGate.Running) Gate = FpsModels.GateFor(calibration, fingerprint);
        var (cg, cc) = current ? FpsModels.Indices(calibration!) : (null, null);
        double? g; unknownHardware = null; var missing = new List<string>();
        if (cg is double x) { g = x; basis = FpsBasis.Calibrated; }
        else if (gpuTable is { } t) { g = t.Index; basis = FpsBasis.Table; }
        else if (specIdx is double sp) { g = sp; basis = FpsBasis.Specs; }
        else { g = null; missing.Add("a GPU " + (gpuName.Length > 0 ? GameCatalog.ShortGpuName(gpuName) : "(não identificada)")); }
        double? c = cc ?? cpuTable; cpuMeasured = cc is not null;
        if (c is null) missing.Add("a CPU " + (cpuName.Length > 0 ? GameCatalog.ShortCpuName(cpuName) : "(não identificada)"));
        hasIndices = missing.Count == 0;
        if (!hasIndices) unknownHardware = string.Join(" e ", missing);
        gpuIdx = g ?? 50; cpuIdx = c ?? 80;
        assessment = hasIndices ? FpsModels.Assess(basis, tableMatch, gpuIdx, laptopCpu, cpuMeasured, cpuIdx) : new(FpsConfidence.Low, FpsAccuracy.LowSpread, []);
        UpdateGate();
    }

    /// <summary>Invariant culture with a decimal comma (pt-BR numbers without depending on ICU data).</summary>
    private static readonly CultureInfo PtBr = FpsModels.DecimalComma;

    private void UpdateGate()
    {
        var inv = PtBr;
        bool running = Gate == FpsGate.Running;
        calibrate.IsVisible = !running; cancelCal.IsVisible = running; gateProgress.IsVisible = running;
        calibrate.IsEnabled = calibrator is not null;
        gateCard.BorderBrush = Gate == FpsGate.Calibrated ? Palette.Line : Palette.Warn;
        switch (Gate)
        {
            case FpsGate.Running:
                gateTitle.Text = "Calibrando este PC…"; gateIcon.Set(Icons.Bench, Palette.AccentText); gateText.Foreground = Palette.Muted;
                break;
            case FpsGate.Calibrated when calibration is { } c:
                var (g, cpu) = FpsModels.Indices(c);
                gateTitle.Text = "PC calibrado"; gateIcon.Set(Icons.Validate, Palette.Success); gateText.Foreground = Palette.Muted;
                gateText.Text = $"Calibrado em {c.At.ToLocalTime().ToString("dd/MM/yyyy HH:mm", inv)}"
                    + (g is double gi ? $" · GPU {c.Adapter}: nota {c.SceneScore!.Value.ToString("0", inv)} na cena de teste → índice {gi.ToString("0.0#", inv)}" : " · GPU não medida" + (c.Note is { Length: > 0 } n ? $" ({n})" : ""))
                    + (cpu is double ci ? $" · CPU: {c.CpuSimFps!.Value.ToString("0.0", inv)} FPS na simulação de uma thread → índice {ci.ToString("0", inv)}" : " · CPU não medida")
                    + (c.Driver.Length > 0 ? $" · driver {c.Driver} {c.DriverVersion}".TrimEnd() : "")
                    + ". Os índices são da escala do Ilmenyx (RTX 4070 = 100, Ryzen 7 7700X = 100). Se trocar a GPU, o driver ou a CPU, o Ilmenyx pede uma nova calibração."
                    + (CalibrationMessage is { } m0 ? " " + m0 : "");
                calibrate.Content = "RECALIBRAR (≈1 min)";
                break;
            default:
                bool stale = Gate == FpsGate.Stale;
                gateTitle.Text = stale ? "A GPU, o driver ou a CPU mudou: as estimativas não são confiáveis até recalibrar" : "As estimativas ainda não são confiáveis: este PC não foi calibrado";
                gateIcon.Set(Icons.Blocked, Palette.Warn); gateText.Foreground = Palette.Muted;
                gateText.Text = (stale ? $"A calibração de {calibration!.At.ToLocalTime().ToString("dd/MM/yyyy", inv)} foi feita com outro hardware ou driver. " : "")
                    + $"A calibração mede esta máquina com dois testes curtos: uma cena de jogo na GPU ({FpsModels.SceneSeconds} s) e uma simulação de jogo em uma thread da CPU ({FpsModels.SimSeconds} s). "
                    + $"Leva cerca de 1 minuto, esquenta o PC por pouco tempo e para sozinha se a CPU ou a GPU chegar a {FpsModels.AbortTempC:0} °C. Nada é enviado pela internet. "
                    + "Até calibrar, os números dos cartões ficam cinza e marcados \"provisório\"." + (CalibrationMessage is { } m1 ? " " + m1 : "")
                    + (calibrator is null ? " (Calibração indisponível neste sistema.)" : "");
                calibrate.Content = "CALIBRAR AGORA (≈1 min)";
                break;
        }
    }

    /// <summary>Runs the calibration after confirmation, aborting at <see cref="FpsModels.AbortTempC"/>; only a finished run is saved.</summary>
    public async Task CalibrateAsync()
    {
        if (calibrator is null || Gate == FpsGate.Running) return;
        if (TopLevel.GetTopLevel(this) is Window w && !await ConfirmDialog.AskAsync(w, "Calibrar este PC",
            $"Isto roda uma cena de jogo na GPU por {FpsModels.SceneSeconds} s e uma simulação de jogo na CPU por {FpsModels.SimSeconds} s (cerca de 1 minuto). O PC esquenta um pouco; o teste para sozinho se a CPU ou a GPU chegar a {FpsModels.AbortTempC:0} °C. Deixe o notebook na tomada e feche jogos e programas pesados. Calibrar agora?", "Calibrar", "Cancelar")) return;
        double? hot = Hottest();
        if (hot >= FpsModels.AbortTempC) { CalibrationMessage = $"O PC já está a {hot:0} °C: espere esfriar e tente de novo."; UpdateGate(); return; }
        var previous = Gate; Gate = FpsGate.Running; CalibrationMessage = null; calAbort = null; gateProgress.Value = 0;
        gateText.Text = "Preparando…"; UpdateGate();
        calCts = new CancellationTokenSource();
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        guard.Tick += (_, _) => { if (Hottest() is double t && t >= FpsModels.AbortTempC && calAbort is null) { calAbort = $"Calibração interrompida: {t:0} °C (limite {FpsModels.AbortTempC:0} °C). Nada foi salvo; espere esfriar e tente de novo."; calCts.Cancel(); } };
        guard.Start();
        FpsCalibrationMeasurement? m = null;
        try { m = await calibrator.Run(new Progress<(string Phase, double Fraction)>(p => { if (Gate == FpsGate.Running) { gateText.Text = p.Phase; gateProgress.Value = Math.Clamp(p.Fraction, 0, 1); } }), calCts.Token); }
        catch (OperationCanceledException) { }
        finally { guard.Stop(); }
        bool cancelled = calCts.IsCancellationRequested; calCts.Dispose(); calCts = null;
        Gate = previous;
        if (calAbort is not null || cancelled || m is null) { CalibrationMessage = calAbort ?? "Calibração cancelada: nada foi salvo."; ComputeIndices(); RefreshLocal(); return; }
        if (m.CpuSimFps is null && m.SceneScore is null) { CalibrationMessage = "A calibração não mediu nada" + (m.Reason is { } r ? ": " + r : ".") + " Nada foi salvo."; ComputeIndices(); RefreshLocal(); return; }
        var cal = new FpsCalibration(DateTimeOffset.Now, fingerprint, gpuName, ctx?.GpuDriver ?? "", ctx?.GpuDriverVersion ?? "", cpuName, m.SceneScore, m.Adapter, m.CpuSimFps, m.Reason);
        try { settings.FpsSessions = IlmenyxSettings.Update(settingsPath, s => s.FpsCalibration = cal).FpsSessions; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { CalibrationMessage = "Não foi possível salvar a calibração: " + e.Message; ComputeIndices(); RefreshLocal(); return; }
        calibration = cal; settings.FpsCalibration = cal;
        CalibrationMessage = m.SceneScore is null ? $"A GPU não foi medida ({m.Reason ?? "sem Vulkan"}); a GPU usa a tabela ou as especificações." : null;
        ComputeIndices(); RefreshLocal();
    }

    private double? Hottest()
    {
        double? a = calibrator?.CpuTemp(), b = calibrator?.GpuTemp();
        return a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
    }

    public void RefreshLocal()
    {
        bool provisional = Gate != FpsGate.Calibrated;
        personalGpu = personalCpu = null;
        if (hasIndices)
        {
            if (!provisional)
            {
                personalGpu = FpsModels.Personal(sessions, GameCatalog.Games, gpuIdx, cpuIdx, preset, Bottleneck.Gpu, gpuName);
                personalCpu = FpsModels.Personal(sessions, GameCatalog.Games, gpuIdx, cpuIdx, preset, Bottleneck.Cpu, gpuName);
            }
            foreach (var row in rows)
            {
                var e = GameCatalog.Estimate(row.Game, gpuIdx, cpuIdx, res, preset, upscaling);
                row.SetEstimate(e, assessment, basis, provisional, e.Limiter == Bottleneck.Gpu ? personalGpu : e.Limiter == Bottleneck.Cpu ? personalCpu : null);
                if (sessions is { Count: > 0 } && FpsAccuracy.LatestSession(sessions, row.Name) is { } s) row.SetMeasured(s);
            }
        }
        else
        {
            foreach (var row in rows) row.Set(0, 0, "SEM ESTIMATIVA", $"{unknownHardware} não está na tabela de referência do Ilmenyx");
            GameFpsRow.ApplyMeasured(rows, sessions);
        }
        int measured = rows.Count(r => r.IsMeasured);
        string measuredNote = measured == 0 ? " Para ver o FPS real deste PC, abra o jogo pela página Jogos com MangoHud ligado: ao fechar, a sessão aparece aqui como MEDIDO NESTE PC."
            : $" {measured} jogo(s) com sessão MEDIDO NESTE PC (MangoHud, pela página Jogos): esse valor vale nas configurações usadas no jogo, não nas opções acima.";
        string estimateNote = $" A faixa é uma estimativa, não uma medição ({assessment.ConfidenceText}): {string.Join("; ", assessment.Reasons)}.";
        string personalNote = (personalGpu ?? personalCpu) is { } pf
            ? $" Ajuste pessoal: {pf.Text} na qualidade {preset} corrigem as estimativas de jogos com o mesmo gargalo (fator {(personalGpu ?? pf).Factor.ToString("0.00", PtBr)})."
            : provisional ? "" : $" Ajuste pessoal: nenhuma sessão medida na qualidade {preset} com a resolução informada (informe a qualidade gráfica no perfil do jogo, página Jogos).";
        if (unknownHardware is { } missingNow)
        {
            sourceTitle.Text = "Fonte: sem estimativa para este PC";
            sourceText.Text = $"Não há referência de desempenho para {missingNow}, então o Ilmenyx não calcula um FPS (seria inventado)."
                + (Gate != FpsGate.Calibrated ? " Calibre o PC: a medição dá uma estimativa mesmo para peças fora da tabela." : "") + measuredNote;
        }
        else if (provisional)
        {
            sourceTitle.Text = "Fonte: provisória (PC ainda não calibrado)";
            sourceText.Text = $"Por enquanto os números usam a base \"{FpsModels.BasisText(basis)}\" (pelo nome ou ID da GPU e pelo nome da CPU), sem medir esta máquina e sem usar a internet: não confie neles antes de calibrar." + measuredNote;
        }
        else
        {
            sourceTitle.Text = "Fonte: estimativa calibrada neste PC";
            sourceText.Text = $"Calculada neste PC, sem usar a internet. GPU: base {FpsModels.BasisText(basis)} (índice {gpuIdx.ToString("0.0#", PtBr)}); CPU: base {(cpuMeasured ? "benchmark deste PC" : "tabela de referência")} (índice {cpuIdx.ToString("0", PtBr)})."
                + estimateNote + personalNote + measuredNote;
        }
        Rebuild();
    }

    private sealed class PathIconHolder
    {
        public Border Host { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
        public void Set(string path, IBrush brush) => Host.Child = UiKit.Icon(path, 15, brush);
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
            // Before calibration the estimate is greyed out and labelled provisional (the measured value of a session is never greyed).
            var tone = row.IsProvisional ? Palette.Faint : RatingBrush(row.Fps);
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
            var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { new TextBlock { Text = row.FpsText, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = row.IsProvisional ? Palette.Faint : Palette.Fg, FontStyle = row.IsProvisional ? FontStyle.Italic : FontStyle.Normal }, new TextBlock { Text = "FPS", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) } } };
            var top = new Grid { ColumnDefinitions = new("*,Auto"), Children = { value } };
            if (row.Rating.Length > 0 && !row.IsProvisional)
            {
                var rating = new TextBlock { Text = row.Rating, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = tone, LetterSpacing = 0.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(rating, 1); top.Children.Add(rating);
            }
            var meta = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = row.LowText, Foreground = Palette.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0) } } };
            var source = new TextBlock { Text = row.Source, Foreground = Palette.Faint, FontSize = 11, LetterSpacing = 0.4 }; Grid.SetColumn(source, 1); meta.Children.Add(source);
            var body = new StackPanel { Margin = new Thickness(16, 6, 16, 14), Children = { top, bar, meta, new TextBlock { Text = row.Detail, Foreground = Palette.Faint, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = row.Detail.Length > 0 },
                new TextBlock { Name = "Basis_" + LauncherPage.SafeName(row.Name), Text = row.IsMeasured ? "" : row.BasisText, Foreground = row.IsProvisional ? Palette.Warn : Palette.Faint, FontSize = 11, FontWeight = row.IsProvisional ? FontWeight.SemiBold : FontWeight.Normal, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, IsVisible = !row.IsMeasured && row.BasisText.Length > 0 } } };
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
