// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// New Ilmenyx additions (layout follows the original RESET BENCH page: test selector, options, start/stop, result, history): see LICENSING.md.
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Gpu.Vulkan;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// Benchmarks (CPU render, RAM, GameSim, GPU scene, GPU ray tracing, disk). Every run is a real load: it starts only after INICIAR and a visible confirmation,
/// is bounded by a timeout, can be stopped with PARAR, and is cut at the CPU temperature limit when a sensor exists.
/// Scores are experimental and are never presented as comparable with the Windows original.
/// </summary>
public sealed class BenchPage : StackPanel
{
    public enum Kind { Cpu, Memory, Sim, Gpu, Rt, Disk }
    private enum CpuMode { Both, Multi, Single }

    private readonly BenchServices services;
    private readonly UsageViewModel usage;
    private readonly Func<Window?> owner;
    private readonly IlmenyxSettings settings;
    private readonly string settingsPath;
    private readonly CancellationToken lifetime;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Button start = UiKit.Primary("BtnBench", "INICIAR", 24, 12);
    private readonly TextBlock hint = new() { Name = "BenchHint", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock phase = new() { Name = "BenchPhase", Text = "Parado", FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar bar = new() { Name = "BenchProgress", Minimum = 0, Maximum = 1, Height = 6, MinHeight = 6, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock headline = new() { Name = "BenchHeadline", Text = "—", FontSize = 40, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock lastRun = new() { FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock kindTag = new() { FontSize = 11, FontWeight = FontWeight.Medium, Foreground = Palette.Muted };
    private readonly Border details = new();
    private readonly TextBlock result = new() { Name = "BenchResultText", TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace"), FontSize = 12 };
    private readonly TextBlock status = new() { Name = "BenchStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Image render = new() { Name = "BenchImage", MaxHeight = 270, Margin = new Thickness(0, 6, 0, 0), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
    private readonly StackPanel options = new() { Name = "BenchOptions", Spacing = 10 };
    private readonly StackPanel historyRows = new() { Name = "BenchHistory" };
    private StackPanel kindPanel = null!;
    private readonly TextBox tempLimit = new() { Name = "BenchTempLimit", Text = "95", Width = 80 };
    private readonly TextBox diskDirectory = new() { Name = "BenchDiskDir", MinWidth = 320 };
    private readonly TextBlock diskFs = new() { Name = "BenchDiskFs", Foreground = Palette.Muted, FontSize = 11 };
    private Kind kind = Kind.Cpu;
    private CpuMode cpuMode = CpuMode.Both;
    private bool sustained;
    private int simThreads = 1, simSeconds = 3, diskMegabytes = 512, gpuSeconds = 60;
    private GpuBenchInfo? gpuInfo;
    private GpuRtInfo? rtInfo;
    private Button? rtTile;
    private string rtPreset = "alta", rtResolution = "1440p";
    private bool rtQuick = true;
    private CancellationTokenSource? cts;
    private string? abortReason;
    private DateTime startedAt;
    private double progressValue;
    private string phaseText = "";
    private double cpuTempLimit = 95;
    public bool IsRunning => cts is not null;
    public string StatusText => status.Text ?? "";

    public BenchPage(BenchServices services, UsageViewModel usage, Func<Window?> owner, string settingsPath, CancellationToken lifetime)
    {
        this.services = services; this.usage = usage; this.owner = owner; this.settingsPath = settingsPath; this.lifetime = lifetime; Spacing = 4;
        settings = IlmenyxSettings.Load(settingsPath);
        Children.Add(UiKit.PageHeader("Benchmark", "CPU, memória, GameSim, GPU, ray tracing e disco · cada teste só começa após sua confirmação"));

        var kinds = new UiKit.ChipGroup<Kind>("BenchKindPanel", [("CPU", Kind.Cpu), ("MEMÓRIA", Kind.Memory), ("GAMESIM", Kind.Sim), ("GPU", Kind.Gpu), ("RAY TRACING", Kind.Rt), ("DISCO", Kind.Disk)], Kind.Cpu, k => { kind = k; StyleTiles(); BuildOptions(); });
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        diskDirectory.Text = home; AutomationProperties.SetName(diskDirectory, "Pasta autorizada para o arquivo temporário do teste de disco");
        AutomationProperties.SetName(tempLimit, "Limite de temperatura da CPU em graus Celsius");
        tempLimit.HorizontalAlignment = diskDirectory.HorizontalAlignment = HorizontalAlignment.Left;
        diskDirectory.TextChanged += (_, _) => UpdateFs();
        start.MinWidth = 160; start.HorizontalContentAlignment = HorizontalAlignment.Center; start.Click += async (_, _) => { if (cts is not null) cts.Cancel(); else await StartAsync(); };

        // Hero: the latest score dominates; progress and the single start/stop action sit with it.
        hint.MaxWidth = 220; hint.Margin = new Thickness(0, 8, 0, 0); hint.TextAlignment = TextAlignment.Right; hint.HorizontalAlignment = HorizontalAlignment.Right;
        var tags = new WrapPanel { Children = { new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(11, 4), Margin = new Thickness(0, 0, 6, 6), Child = kindTag }, UiKit.Tag("EXPERIMENTAL") } };
        var score = new StackPanel { Spacing = 2, Children = { tags, new Border { Height = 4 }, UiKit.Label("ÚLTIMO RESULTADO"), headline, lastRun, status } };
        var action = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 0), Children = { start, hint } };
        var progress = new StackPanel { Margin = new Thickness(0, 18, 0, 0), Children = { phase, bar } };
        var heroGrid = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("*,Auto") };
        Grid.SetColumn(action, 1); Grid.SetRow(progress, 1); Grid.SetColumnSpan(progress, 2);
        heroGrid.Children.Add(score); heroGrid.Children.Add(action); heroGrid.Children.Add(progress);
        Children.Add(Themed(UiKit.Hero(heroGrid, height: double.NaN, name: "BenchHero")));

        // Tests: an asymmetric row of tiles (the main CPU test is wider), same chip group and names as before.
        kindPanel = kinds.Panel; kindPanel.Spacing = 10;
        foreach (var b in kindPanel.Children.OfType<Button>()) MakeTile(b);
        // The ray tracing tile stays disabled (with the reason) until the adapter check says the GPU has inline ray tracing.
        rtTile = kindPanel.Children.OfType<Button>().First(b => (b.Content as string) == "RAY TRACING");
        rtTile.Name = "BenchRtTile"; rtTile.IsEnabled = false; ToolTip.SetShowOnDisabled(rtTile, true); ToolTip.SetTip(rtTile, "Verificando suporte a ray tracing…");
        var tilesHost = new Border { Child = kindPanel };
        tilesHost.SizeChanged += (_, e) => SizeTiles(e.NewSize.Width);
        Children.Add(UiKit.SectionHeader("Testes"));
        Children.Add(tilesHost);
        StyleTiles();

        Children.Add(UiKit.SectionHeader("Opções"));
        Children.Add(UiKit.Card(options));
        details.Child = UiKit.Card(new StackPanel { Spacing = 4, Children = { UiKit.Label("DETALHES DO RESULTADO"), result, render } }, new Thickness(0, 12, 0, 0));
        details.IsVisible = false;
        result.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) details.IsVisible = !string.IsNullOrEmpty(result.Text); };
        Children.Add(details);

        var clear = new Button { Name = "BtnClearBenchHistory", Content = "Limpar histórico", FontSize = 11.5, Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center, Background = Palette.Card, Foreground = Palette.Muted, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius) };
        clear.Click += async (_, _) =>
        {
            if (owner() is not { } w || settings.BenchRuns.Count == 0) return;
            if (!await ConfirmDialog.AskAsync(w, "Limpar histórico", "Apaga somente a lista local de benchmarks deste aplicativo. Continuar?", "Limpar", "Cancelar")) return;
            Save(s => s.BenchRuns.Clear()); BuildHistory();
        };
        Children.Add(UiKit.SectionHeader("Histórico local", clear));
        Children.Add(UiKit.Card(new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Fica só neste computador; nunca é enviado.", FontSize = 11.5, Foreground = Palette.Faint }, historyRows } }));
        timer.Tick += (_, _) => Tick();
        BuildOptions(); BuildHistory(); UpdateFs();
        lifetime.Register(() => Dispatcher.UIThread.Post(() => cts?.Cancel()));
        _ = DetectRtAsync();
    }

    /// <summary>Adapter check off the UI thread (instance + enumeration only; nothing is rendered).</summary>
    private async Task DetectRtAsync()
    {
        GpuRtInfo info;
        try { info = services.RtInfo is { } detect ? await Task.Run(detect) : new GpuRtInfo("Teste de ray tracing indisponível nesta instalação.", -1, "", false); }
        catch (Exception e) when (e is IOException or InvalidOperationException or DllNotFoundException) { info = new GpuRtInfo("Vulkan indisponível: " + e.Message, -1, "", false); }
        rtInfo = info;
        if (rtTile is not null)
        {
            rtTile.IsEnabled = info.Reason is null;
            ToolTip.SetTip(rtTile, info.Reason is { } why ? "Ray tracing indisponível: " + why : $"Path tracing com ray query em {info.AdapterName}");
            AutomationProperties.SetHelpText(rtTile, info.Reason ?? "");
        }
        if (kind == Kind.Rt) BuildOptions();
    }

    /// <summary>
    /// UiKit.HeroBrush is computed once for the current theme; this re-fills the hero when the theme is toggled on a page that is already built.
    /// Candidate for UiKit.Hero itself (shared by the Benchmark, OC, Stress and Overlay heroes).
    /// </summary>
    internal static Border Themed(Border hero)
    {
        hero.ActualThemeVariantChanged += (_, _) => hero.Background = UiKit.HeroBrush();
        return hero;
    }

    private static readonly Dictionary<string, (string Icon, string Note)> Tiles = new()
    {
        ["CPU"] = (Icons.Cpu, "Render path tracing multi-core e single-core · teste principal"),
        ["MEMÓRIA"] = (Icons.Memory, "Latência e banda"),
        ["GAMESIM"] = (Icons.Games, "Simulação de jogo · FPS"),
        ["GPU"] = (Icons.Gpu, "Cena Vulkan 1080p"),
        ["RAY TRACING"] = (Icons.Sun, "Path tracing Vulkan · ray query"),
        ["DISCO"] = (Icons.Disks, "Sequencial e 4K"),
    };

    /// <summary>Turns a chip of the kind group into a test tile. Content stays the plain label (the smoke test and the chip group use it).</summary>
    private static void MakeTile(Button b)
    {
        b.CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius); b.Padding = new Thickness(14, 12); b.MinHeight = 96;
        b.VerticalAlignment = VerticalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Stretch; b.VerticalContentAlignment = VerticalAlignment.Top;
        b.ContentTemplate = new FuncDataTemplate<string>((text, _) =>
        {
            var (icon, note) = Tiles.TryGetValue(text ?? "", out var t) ? t : (Icons.Bench, "");
            var glyph = UiKit.Icon(icon, 20); glyph.HorizontalAlignment = HorizontalAlignment.Left;
            glyph.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, new Binding(nameof(Button.Foreground)) { Source = b });
            return new StackPanel { Spacing = 6, Children = {
                glyph,
                new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) },
                new TextBlock { Text = note, FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap } } };
        });
    }

    private void StyleTiles()
    {
        foreach (var b in kindPanel.Children.OfType<Button>())
        {
            bool on = (b.Content as string) == KindLabel(kind);
            b.Background = on ? Palette.Hover : Palette.Card; b.Foreground = on ? Palette.AccentText : Palette.Muted;
            b.BorderBrush = on ? Palette.Accent : Palette.Line; b.BorderThickness = new Thickness(1);
        }
        kindTag.Text = KindLabel(kind);
    }

    private static string KindLabel(Kind k) => k switch { Kind.Cpu => "CPU", Kind.Memory => "MEMÓRIA", Kind.Sim => "GAMESIM", Kind.Gpu => "GPU", Kind.Rt => "RAY TRACING", _ => "DISCO" };

    /// <summary>Asymmetric widths (first tile ~1.9x the others), recomputed with the page width.</summary>
    private void SizeTiles(double width)
    {
        var tiles = kindPanel.Children.OfType<Button>().ToList();
        if (tiles.Count == 0 || width <= 0) return;
        double unit = Math.Max(80, (width - kindPanel.Spacing * (tiles.Count - 1)) / (tiles.Count + 0.9));
        for (int i = 0; i < tiles.Count; i++) tiles[i].Width = Math.Floor(i == 0 ? unit * 1.9 : unit);
    }

    private static Control Field(string label, Control c) => new StackPanel { Margin = new Thickness(0, 0, 28, 4), Children = { UiKit.Label(label), new Border { Height = 4 }, c } };

    private void BuildOptions()
    {
        options.Children.Clear();
        foreach (Control shared in new Control[] { tempLimit, diskDirectory, diskFs }) (shared.Parent as Panel)?.Children.Remove(shared);
        var wrap = new WrapPanel();
        switch (kind)
        {
            case Kind.Cpu:
                wrap.Children.Add(Field("NÚCLEOS", new UiKit.ChipGroup<CpuMode>("BenchModePanel", [("MULTI + SINGLE", CpuMode.Both), ("MULTI", CpuMode.Multi), ("SINGLE", CpuMode.Single)], cpuMode, m => cpuMode = m).Panel));
                wrap.Children.Add(Field("DURAÇÃO", new UiKit.ChipGroup<bool>("BenchDurationPanel", [("NORMAL", false), ("SUSTENTADO 10 MIN", true)], sustained, v => sustained = v).Panel));
                wrap.Children.Add(Field("LIMITE °C", tempLimit));
                options.Children.Add(wrap);
                options.Children.Add(Note("Renderiza uma cena com path tracing 960×540 a 1024 amostras por pixel (algoritmo do RESET BENCH). Carga de 100% em todas as threads."));
                break;
            case Kind.Memory:
                options.Children.Add(Note("Latência (cadeia de ponteiros em 256 MiB) e banda de leitura/cópia (buffers de 1 GiB). Aloca cerca de 2,3 GiB e recusa rodar sem 1 GiB livre de reserva."));
                break;
            case Kind.Sim:
                var threads = new[] { 1, 2, 4 }.Select(n => ($"{n} THREAD" + (n > 1 ? "S" : ""), n)).ToArray();
                wrap.Children.Add(Field("WORKERS", new UiKit.ChipGroup<int>("BenchSimThreads", threads, simThreads, n => simThreads = n).Panel));
                wrap.Children.Add(Field("MEDIÇÃO", new UiKit.ChipGroup<int>("BenchSimSeconds", [("3 S", 3), ("5 S", 5), ("10 S", 10)], simSeconds, n => simSeconds = n).Panel));
                wrap.Children.Add(Field("LIMITE °C", tempLimit));
                options.Children.Add(wrap);
                options.Children.Add(Note("Simulação de jogo com 30.000 entidades: aquecimento de 2 s e medição de FPS médio, 1% low e tempos de frame. Experimental."));
                break;
            case Kind.Gpu:
                gpuInfo ??= services.GpuInfo();
                if (gpuInfo.UnavailableReason is { } gwhy) { options.Children.Add(Note("Benchmark de GPU indisponível: " + gwhy)); break; }
                wrap.Children.Add(Field("DURAÇÃO", new UiKit.ChipGroup<int>("BenchGpuSeconds", [("30 S", 30), ("60 S", 60), ("120 S", 120)], gpuSeconds, n => gpuSeconds = n).Panel));
                wrap.Children.Add(Field("LIMITE °C", tempLimit));
                options.Children.Add(wrap);
                options.Children.Add(Note($"Cena de jogo em Vulkan em {gpuInfo.AdapterName}: terreno procedural, 40.000 árvores instanciadas, sombras 4096² com PCF, água e HDR a 1920×1080, câmera em trajeto fixo, sem V-Sync. Carga máxima na GPU." + (gpuInfo.SharesCpuThermals ? " Esta GPU divide o chip com a CPU: vale o limite de temperatura da CPU." : "")));
                break;
            case Kind.Rt:
                if (rtInfo is null) { options.Children.Add(Note("Verificando suporte a ray tracing…")); break; }
                if (rtInfo.Reason is { } rwhy) { options.Children.Add(Note("Ray tracing indisponível: " + rwhy)); break; }
                wrap.Children.Add(Field("PRESET", new UiKit.ChipGroup<string>("BenchRtPreset", [("NORMAL", "normal"), ("ALTA", "alta"), ("EXTREMA", "extrema")], rtPreset, v => { rtPreset = v; BuildOptions(); }).Panel));
                wrap.Children.Add(Field("RESOLUÇÃO", new UiKit.ChipGroup<string>("BenchRtResolution", [("1080P", "1080p"), ("1440P", "1440p"), ("4K", "4k")], rtResolution, v => { rtResolution = v; BuildOptions(); }).Panel));
                wrap.Children.Add(Field("MODO", new UiKit.ChipGroup<bool>("BenchRtMode", [("RÁPIDO", true), ("COMPLETO", false)], rtQuick, v => { rtQuick = v; BuildOptions(); }).Panel));
                wrap.Children.Add(Field("LIMITE °C", tempLimit));
                options.Children.Add(wrap);
                var ro = RtOptions(); var q = GpuRtPresets.Quality(ro.Preset)!.Value;
                options.Children.Add(Note($"Path tracing em {rtInfo.AdapterName} com raios inline (VK_KHR_ray_query): cena procedural fixa com {GpuRtBench.ObjectCount:N0} objetos (esferas, toros, colunas, caixas) em materiais fosco, metal e espelho, luzes emissivas, sombra suave, oclusão de ambiente e rebatidas; estrutura de aceleração refeita a cada quadro. " +
                    $"{ro.Width}×{ro.Height}, {q.Spp} amostras e {q.Bounces} rebatidas por pixel. Aquecimento de {N(ro.EffectiveWarmup.TotalSeconds)} s e {ro.Passes} passadas de {N(ro.EffectivePass.TotalSeconds)} s; nota = FPS mediano × 10. Carga máxima na GPU." +
                    (rtInfo.SharesCpuThermals ? " Esta GPU divide o chip com a CPU: vale o limite de temperatura da CPU." : "")));
                break;
            case Kind.Disk:
                wrap.Children.Add(Field("PASTA DO ARQUIVO TEMPORÁRIO", new StackPanel { Children = { diskDirectory, diskFs } }));
                wrap.Children.Add(Field("TAMANHO", new UiKit.ChipGroup<int>("BenchDiskSize", [("256 MB", 256), ("512 MB", 512), ("1 GB", 1024), ("2 GB", 2048)], diskMegabytes, n => diskMegabytes = n).Panel));
                options.Children.Add(wrap);
                options.Children.Add(Note("Sequencial de 1 MiB e acesso aleatório de 4 KiB (1 e 8 em paralelo) com E/S direta. Cria um arquivo novo numa subpasta nova e o apaga ao final; nunca usa dispositivos. Em SSD a escrita causa desgaste proporcional ao tamanho."));
                break;
        }
    }

    /// <summary>Original RESET BENCH modes: quick = 5 s warm-up + 3 × 10 s, full = 20 s + 3 × 30 s.</summary>
    private GpuRtOptions RtOptions() => new(rtInfo?.AdapterIndex ?? 0, rtPreset, rtResolution, TimeSpan.FromSeconds(rtQuick ? 5 : 20), TimeSpan.FromSeconds(rtQuick ? 10 : 30), 3, true);

    /// <summary>The run reads the GPU's own sensor only for a GPU that does not share the chip with the CPU.</summary>
    private bool GpuSensor => kind == Kind.Gpu && gpuInfo is { SharesCpuThermals: false } || kind == Kind.Rt && rtInfo is { SharesCpuThermals: false };

    private double? GuardTemp() => GpuSensor
        ? services.GpuTemp() ?? (kind == Kind.Rt && rtInfo is { VendorId: > 0 } ri ? LinuxThermalProbe.GpuTempC(ri.VendorId, ri.DeviceId) : null)
        : usage.Last?.CpuTempC;

    private static TextBlock Note(string text) => new() { Text = text, FontSize = 12, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap };

    private void UpdateFs()
    {
        string dir = diskDirectory.Text ?? "";
        string? type = Path.IsPathRooted(dir) ? LinuxDiskBench.FilesystemTypeOf(dir, services.MountsText()) : null;
        diskFs.Text = type is null ? "Sistema de arquivos: desconhecido" : "Sistema de arquivos: " + type + (LinuxDiskBench.MemoryBackedReason(dir, services.MountsText()) is not null ? " (memória — não serve para medir disco)" : "");
    }

    private void SetPhase(string text, double fraction) { phaseText = text; progressValue = Math.Clamp(fraction, 0, 1); }

    /// <summary>Periodic tick (progress text and temperature guard). Public so a host can drive it deterministically.</summary>
    public void Tick()
    {
        if (cts is null) return;
        phase.Text = $"{phaseText} · {DateTime.Now - startedAt:mm\\:ss}"; bar.Value = progressValue;
        if (kind is Kind.Cpu or Kind.Sim or Kind.Gpu or Kind.Rt && abortReason is null && GuardTemp() is double t && t >= cpuTempLimit)
        {
            abortReason = string.Create(CultureInfo.InvariantCulture, $"ABORTADO: {(GpuSensor ? "GPU" : "CPU")} a {t:0.#} °C, limite {cpuTempLimit:0} °C");
            cts.Cancel();
        }
    }

    private void Busy(bool on)
    {
        start.Content = on ? "PARAR" : "INICIAR";
        tempLimit.IsEnabled = diskDirectory.IsEnabled = !on;
        foreach (var name in new[] { "BenchKindPanel", "BenchModePanel", "BenchDurationPanel", "BenchSimThreads", "BenchSimSeconds", "BenchGpuSeconds", "BenchRtPreset", "BenchRtResolution", "BenchRtMode", "BenchDiskSize" })
            if (this.GetLogicalDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Name == name) is { } panel) panel.IsEnabled = !on;
    }

    public async Task StartAsync()
    {
        if (cts is not null) return;
        hint.Text = ""; status.Text = "";
        if (owner() is not { } window) return;
        string? confirm;
        if (kind is Kind.Cpu or Kind.Sim or Kind.Gpu or Kind.Rt)
        {
            if (!double.TryParse(tempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out cpuTempLimit) && !double.TryParse(tempLimit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out cpuTempLimit) || cpuTempLimit is < 50 or > 105)
            { hint.Text = "Limite de temperatura de 50 a 105 °C."; return; }
        }
        string sensorName = GpuSensor ? "GPU" : "CPU";
        string guard = kind is Kind.Cpu or Kind.Sim or Kind.Gpu or Kind.Rt
            ? GuardTemp() is null
                ? $"\n\nATENÇÃO: nenhum sensor de temperatura da {sensorName} está sendo lido, então o corte automático NÃO funcionará."
                : string.Create(CultureInfo.InvariantCulture, $"\n\nO teste é interrompido se a {sensorName} chegar a {cpuTempLimit:0} °C.")
            : "";
        switch (kind)
        {
            case Kind.Cpu:
                confirm = $"Isto vai usar 100% da CPU ({Environment.ProcessorCount} threads) por vários minutos" + (sustained ? ", repetindo a cena por no mínimo 10 minutos" : "") + ". O sistema pode ficar lento. Salve seu trabalho antes." + guard;
                break;
            case Kind.Sim:
                confirm = $"Isto vai gerar carga de CPU com {simThreads} worker(s): 2 s de aquecimento e {simSeconds} s de medição. Salve seu trabalho antes." + guard;
                break;
            case Kind.Gpu:
                gpuInfo ??= services.GpuInfo();
                if (gpuInfo.UnavailableReason is { } gpuWhy) { hint.Text = gpuWhy; return; }
                confirm = $"Isto vai renderizar a cena de jogo em Vulkan em {gpuInfo.AdapterName} por {gpuSeconds} s com a GPU no máximo (1920×1080, sombras 4096², 40.000 árvores). O sistema pode ficar menos fluido durante o teste. Salve seu trabalho antes." + guard;
                break;
            case Kind.Rt:
                if (rtInfo is null || services.GpuRt is null) { hint.Text = "Ainda verificando o suporte a ray tracing."; return; }
                if (rtInfo.Reason is { } rtWhy) { hint.Text = rtWhy; return; }
                var rto = RtOptions();
                confirm = $"Isto vai renderizar o teste de ray tracing (path tracing Vulkan) em {rtInfo.AdapterName} com a GPU no máximo: {GpuRtPresets.Label(rto.Preset)} a {rto.Width}×{rto.Height}, " +
                    $"{N(rto.EffectiveWarmup.TotalSeconds)} s de aquecimento e {rto.Passes} passadas de {N(rto.EffectivePass.TotalSeconds)} s (cerca de {N(rto.TotalDuration.TotalSeconds)} s). O sistema pode ficar menos fluido durante o teste. Salve seu trabalho antes." + guard;
                break;
            case Kind.Memory:
                long available = services.AvailableMemory();
                if (MemoryBench.Check(available) is { } why) { hint.Text = why; return; }
                confirm = $"Isto vai alocar cerca de 2,3 GiB de RAM (há {available >> 20} MiB disponíveis; 1 GiB fica sempre livre) e usar todas as threads por alguns segundos.";
                break;
            default:
                string dir = (diskDirectory.Text ?? "").Trim();
                if (LinuxDiskBench.MemoryBackedReason(dir, services.MountsText()) is { } mem) { hint.Text = mem; return; }
                long free;
                try { free = Directory.Exists(dir) ? new DriveInfo(Path.GetFullPath(dir)).AvailableFreeSpace : 0; } catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { free = 0; }
                var opts = new DiskBenchOptions(dir, diskMegabytes);
                if (DiskBenchOptions.Validate(opts, free) is { } bad) { hint.Text = bad; return; }
                confirm = $"Isto vai criar um arquivo temporário de {diskMegabytes} MB em {dir}, escrever e ler nele (cerca de {diskMegabytes * 2 + 100} MB de E/S, mais ~12 s de acesso aleatório) e apagá-lo ao final. Em SSD isso consome vida útil proporcional. Nenhum dispositivo é acessado diretamente.";
                break;
        }
        if (!await ConfirmDialog.AskAsync(window, "Iniciar benchmark", confirm + "\n\nIniciar agora?", "Iniciar", "Cancelar")) return;
        await RunAsync();
    }

    private async Task RunAsync()
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime); var token = cts.Token;
        abortReason = null; startedAt = DateTime.Now; SetPhase("Preparando", 0);
        headline.Text = "—"; result.Text = ""; render.IsVisible = false; Busy(true); timer.Start();
        BenchRunEntry? entry = null; string finalStatus;
        try
        {
            entry = kind switch { Kind.Cpu => await RunCpuAsync(token), Kind.Memory => await RunMemoryAsync(token), Kind.Sim => await RunSimAsync(token), Kind.Gpu => await RunGpuAsync(token), Kind.Rt => await RunRtAsync(token), _ => await RunDiskAsync(token) };
            finalStatus = entry is not null ? $"Concluído em {DateTime.Now - startedAt:mm\\:ss}" : abortReason ?? (token.IsCancellationRequested ? "Cancelado: nenhum valor é publicado." : status.Text ?? "Não concluído.");
        }
        catch (OperationCanceledException) { finalStatus = abortReason ?? "Cancelado: nenhum valor é publicado."; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { finalStatus = "Falha: " + e.Message; }
        timer.Stop(); cts.Dispose(); cts = null; Busy(false); SetPhase("Parado", entry is null ? 0 : 1); phase.Text = "Parado"; bar.Value = progressValue;
        status.Text = finalStatus;
        if (entry is not null) { Save(s => s.AddBenchRun(entry)); BuildHistory(); }
    }

    private static string N(double v, string format = "0") => v.ToString(format, CultureInfo.InvariantCulture);

    private async Task<BenchRunEntry?> RunCpuAsync(CancellationToken ct)
    {
        int threads = Environment.ProcessorCount; double? mt = null, st = null; CpuRenderOutput? last = null;
        var phases = cpuMode switch { CpuMode.Both => new[] { threads, 1 }, CpuMode.Multi => [threads], _ => [1] };
        for (int i = 0; i < phases.Length; i++)
        {
            int n = phases[i], index = i;
            string label = n == 1 ? "Single-core · 1 thread" : $"Multi-core · {n} threads";
            SetPhase(label, (double)index / phases.Length);
            var progress = new Progress<CpuBenchmarkProgress>(p => SetPhase(label, (index + (p.TotalTiles == 0 ? 0 : (double)p.CompletedTiles / p.TotalTiles)) / phases.Length));
            var run = await services.Cpu(new CpuBenchmarkOptions { Profile = CpuWorkloadProfile.Full, Threads = n, AllowCpuLoad = true, MinimumDuration = sustained ? TimeSpan.FromMinutes(10) : TimeSpan.Zero, Timeout = sustained ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5) }, ct, progress);
            if (run.Result.Status != CpuRunStatus.Completed || run.Result.ExperimentalScore is null)
            { status.Text = run.Result.Status == CpuRunStatus.TimedOut ? "Tempo limite atingido: nenhum valor é publicado." : "Cancelado: nenhum valor é publicado."; return null; }
            last = run;
            if (n == 1 && phases.Length >= 1 && (cpuMode == CpuMode.Single || index == 1)) st = run.Result.ExperimentalScore; else mt = run.Result.ExperimentalScore;
        }
        if (last is not null) ShowImage(last);
        headline.Text = mt is double m ? N(m, "N0") + " pts" : N(st!.Value, "N0") + " pts";
        string lines = (mt is double a ? $"Multi-core ({threads} threads): {N(a, "N0")} pts\n" : "") + (st is double b ? $"Single-core: {N(b, "N0")} pts\n" : "");
        if (mt is double mm && st is double ss && ss > 0) lines += $"Multi ÷ single: {N(mm / ss, "0.0")}x com {threads} threads\n";
        result.Text = lines + "Pontuação experimental do port Linux: não é comparável a scores do Windows.";
        return new(DateTimeOffset.Now, "CPU", (mt is double x ? $"multi {N(x, "N0")}" : "") + (st is double y ? (mt is not null ? " · " : "") + $"single {N(y, "N0")}" : "") + (sustained ? " · sustentado" : ""), mt ?? st);
    }

    private void ShowImage(CpuRenderOutput output)
    {
        var w = output.Result.Workload; int[] pixels = output.CopyPixels();
        if (pixels.Length != w.Width * w.Height) return;
        var bitmap = new WriteableBitmap(new PixelSize(w.Width, w.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = bitmap.Lock()) Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
        render.Source = bitmap; render.IsVisible = true;
    }

    private async Task<BenchRunEntry?> RunMemoryAsync(CancellationToken ct)
    {
        var progress = new Progress<(string Phase, double Fraction)>(p => SetPhase(p.Phase, p.Fraction));
        var r = await services.Memory(services.AvailableMemory(), TimeSpan.FromSeconds(120), ct, progress);
        if (r.Status != BenchRunStatus.Completed) { status.Text = r.Reason ?? "Não concluído."; return null; }
        headline.Text = N(r.LatencyNs!.Value, "0.0") + " ns";
        result.Text = $"Latência da RAM: {N(r.LatencyNs.Value, "0.0")} ns\nLeitura: {N(r.ReadGBs!.Value, "0.0")} GB/s\nCópia: {N(r.CopyGBs!.Value, "0.0")} GB/s\nThreads: {r.Threads} · alocado: {r.AllocatedBytes >> 20} MiB\nMedição do port Linux; sem score.";
        return new(DateTimeOffset.Now, "Memória", $"{N(r.LatencyNs.Value, "0.0")} ns · {N(r.ReadGBs.Value, "0.0")} GB/s leitura · {N(r.CopyGBs.Value, "0.0")} GB/s cópia", null);
    }

    private async Task<BenchRunEntry?> RunSimAsync(CancellationToken ct)
    {
        SetPhase($"GameSim · {simThreads} worker(s)", 0.1);
        var r = await services.Sim(new GameSimBenchmarkOptions { Threads = simThreads, Duration = TimeSpan.FromSeconds(simSeconds), Timeout = TimeSpan.FromSeconds(simSeconds + 10), AllowCpuLoad = true }, ct);
        if (r.Status != GameSimBenchmarkStatus.Completed || r.AverageFps is null) { status.Text = $"Não concluído ({r.Status}): nenhum FPS é publicado."; return null; }
        headline.Text = N(r.AverageFps.Value, "0") + " FPS";
        result.Text = $"FPS médio: {N(r.AverageFps.Value, "0.0")}\n1% low: {(r.Low1Fps is double l ? N(l, "0.0") : "—")}\nThread principal: {(r.MainThreadMs is double m ? N(m, "0.00") : "—")} ms · paralelo: {(r.ParallelMs is double p ? N(p, "0.00") : "—")} ms\nQuadros: {r.MeasuredFrames} · workers: {r.Threads}\nPontuação experimental: {(r.ExperimentalScore is double s ? N(s, "N0") : "—")}, não comparável ao Windows.";
        return new(DateTimeOffset.Now, "GameSim", $"{N(r.AverageFps.Value, "0")} FPS · {r.Threads} worker(s)", r.ExperimentalScore);
    }

    private async Task<BenchRunEntry?> RunGpuAsync(CancellationToken ct)
    {
        var progress = new Progress<GpuSceneProgress>(p => SetPhase($"Cena de GPU · {N(p.Fps, "0")} FPS", p.Fraction));
        var r = await services.GpuScene(new GpuSceneOptions(0, 1920, 1080, TimeSpan.FromSeconds(gpuSeconds), 4096, 40_000, true), ct, progress);
        if (r.Status != GpuRunStatus.Completed || r.AverageFps is null) { status.Text = r.Status == GpuRunStatus.Cancelled ? "Cancelado: nenhum valor é publicado." : r.Reason ?? "Não concluído."; return null; }
        headline.Text = N(r.AverageFps.Value, "0") + " FPS";
        result.Text = $"{r.Adapter}\n{r.Width}×{r.Height} · {r.Frames} quadros em {N(r.Seconds, "0.0")} s\nFPS médio: {N(r.AverageFps.Value, "0.0")} · 1% low: {(r.Low1Fps is double l ? N(l, "0.0") : "—")}\nNota experimental: {N(r.Score ?? 0, "N0")}\n{GpuSceneResult.Comparability}";
        return new(DateTimeOffset.Now, "GPU", $"{N(r.AverageFps.Value, "0")} FPS · 1% {(r.Low1Fps is double l2 ? N(l2, "0") : "—")} · {r.Width}×{r.Height}", r.Score);
    }

    private async Task<BenchRunEntry?> RunRtAsync(CancellationToken ct)
    {
        var o = RtOptions();
        var progress = new Progress<GpuRtProgress>(p => SetPhase($"Ray tracing · {p.Phase} · {N(p.Fps, "0.0")} FPS", p.Fraction));
        var r = await services.GpuRt!(o, ct, progress);
        if (r.Status != GpuRunStatus.Completed || r.Passes.Count == 0) { status.Text = r.Status == GpuRunStatus.Cancelled ? "Cancelado: nenhum valor é publicado." : r.Reason ?? "Não concluído."; return null; }
        string label = GpuRtPresets.Label(r.Preset);
        headline.Text = N(r.Score, "N0") + " pts";
        var lines = new List<string>
        {
            r.Adapter,
            $"Nota: {N(r.Score, "N0")} pts · FPS (mediana): {N(r.MedianFps, "0.0")} · 1% low (mediana): {N(r.MedianLow1, "0.0")}",
            $"Variação entre passadas: {N(r.VariationPct, "0.0")}%",
            r.Reliable ? $"Resultado confiável: as {r.Passes.Count} passadas variaram só {N(r.VariationPct, "0.0")}%."
                : $"Variação de {N(r.VariationPct, "0.0")}% entre as passadas (acima de 3%) ou menos de 3 passadas. Algo atrapalhou: programa em segundo plano, temperatura ou limite de energia. Feche outros programas e rode de novo antes de comparar.",
            $"Raios por segundo (estimado): {N(r.GigaRaysPerSec, "0.00")} bilhões",
            $"Preset · resolução: {label} · {r.Width}×{r.Height}",
        };
        for (int i = 0; i < r.Passes.Count; i++) lines.Add($"Passada {i + 1}: {N(r.Passes[i].AvgFps, "0.0")} FPS · 1% low {N(r.Passes[i].Low1Fps, "0.0")}");
        lines.Add(GpuRtResult.Comparability);
        result.Text = string.Join("\n", lines);
        return new(DateTimeOffset.Now, "RT", $"{N(r.Score, "N0")} pts · {N(r.MedianFps, "0.0")} FPS · {label} {r.Height}p" + (r.Reliable ? "" : " · instável"), r.Score);
    }

    private async Task<BenchRunEntry?> RunDiskAsync(CancellationToken ct)
    {
        var progress = new Progress<(string Phase, double Fraction)>(p => SetPhase(p.Phase, p.Fraction));
        var r = await services.Disk(new DiskBenchOptions((diskDirectory.Text ?? "").Trim(), diskMegabytes), ct, progress);
        if (r.Status != BenchRunStatus.Completed) { status.Text = r.Reason ?? "Não concluído."; return null; }
        headline.Text = $"{N(r.SeqReadMBs!.Value)} MB/s";
        result.Text = $"Sequencial leitura: {N(r.SeqReadMBs.Value)} MB/s · escrita: {N(r.SeqWriteMBs!.Value)} MB/s\n4 KiB aleatório (1 por vez): leitura {N(r.ReadIopsQ1!.Value)} IOPS ({N(r.ReadLatencyUs!.Value, "0.0")} µs) · escrita {N(r.WriteIopsQ1!.Value)} IOPS ({N(r.WriteLatencyUs!.Value, "0.0")} µs)\n4 KiB aleatório (8 em paralelo): leitura {N(r.ReadIopsQ8!.Value)} IOPS\nNota do histórico: {N(r.Score!.Value)} · arquivo de {r.FileMegabytes} MB removido.";
        return new(DateTimeOffset.Now, "Disco", $"{N(r.SeqReadMBs.Value)}/{N(r.SeqWriteMBs.Value)} MB/s seq. · 4K Q1 {N(r.ReadIopsQ1.Value)}/{N(r.WriteIopsQ1.Value)} IOPS", r.Score);
    }

    /// <summary>Applies the history change to the current file (atomic, keeps what other pages saved) and refreshes this page's copy.</summary>
    private void Save(Action<IlmenyxSettings> change)
    {
        try { settings.BenchRuns = IlmenyxSettings.Update(settingsPath, change).BenchRuns; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { change(settings); status.Text += " · Histórico não salvo: " + e.Message; }
    }

    private void BuildHistory()
    {
        historyRows.Children.Clear();
        var latest = settings.BenchRuns.FirstOrDefault(r => r.Kind != "OC");
        lastRun.Text = latest is null ? "Nenhum teste concluído ainda. Escolha um teste abaixo e clique em INICIAR." : $"{latest.Kind} · {latest.At.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {latest.Summary}";
        if (latest is not null && cts is null && headline.Text == "—") headline.Text = latest.Kind == "CPU" && latest.Score is double sc ? N(sc, "N0") + " pts" : latest.Summary.Split(" · ")[0];
        if (settings.BenchRuns.Count == 0) { historyRows.Children.Add(new TextBlock { Text = "Nenhum teste concluído ainda.", Foreground = Palette.Muted, FontSize = 12 }); return; }
        bool first = true;
        foreach (var run in settings.BenchRuns.Take(20))
        {
            var row = new Grid { ColumnDefinitions = new("118,84,*") };
            var date = new TextBlock { Text = run.At.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), FontSize = 12, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center };
            var kindText = new TextBlock { Text = run.Kind, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center };
            var summary = new TextBlock { Text = run.Summary, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(kindText, 1); Grid.SetColumn(summary, 2);
            row.Children.Add(date); row.Children.Add(kindText); row.Children.Add(summary);
            historyRows.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(0, 8), Child = row });
            first = false;
        }
    }
}
