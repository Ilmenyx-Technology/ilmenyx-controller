// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original bottleneck page: balance, live verdict, live meters, session analysis; live verdict as the hero since 2026-11-06): see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class BottleneckPage : StackPanel
{
    private readonly UsageViewModel usage;
    private readonly LinuxGpuCollector collector = new();
    private readonly PciIds ids = PciIds.Load();
    private readonly CancellationToken lifetime;
    private (GpuRaw Raw, GpuCard Card)? gpu;
    private double cpuIdx = 80, gpuIdx = 50;
    private readonly TextBlock basisNote = new() { Name = "BnBasisNote", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private CancellationTokenSource? poll;
    private BottleneckSession? session;
    private TimeSpan chosen = TimeSpan.FromMinutes(1);
    private readonly Dictionary<string, MeterView> meters = new();
    private readonly TextBlock cpuName = new() { Name = "BnCpuName", Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock gpuName = new() { Name = "BnGpuName", Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border dot = new() { Name = "BnBalanceDot", Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Palette.Fg, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid scale = new() { Height = 22, Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock balanceText = new() { Name = "BnBalanceText", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock liveTitle = new() { Name = "BnLiveTitle", Text = "—", FontSize = 30, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock liveAdvice = new() { Name = "BnLiveAdvice", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 13, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock sessionStatus = new() { Name = "BnSessionStatus", Foreground = Palette.Muted, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel result = new() { Name = "BnResult", IsVisible = false, Margin = new Thickness(0, 16, 0, 0) };
    private readonly Button start = UiKit.Primary("BtnBnSession", L.T("Iniciar análise"), 18, 9);
    public BottleneckSession? Session => session;
    public Limiter? LiveLimiter { get; private set; }

    private readonly Func<FrameStats?>? frameSource;

    /// <param name="frameSource">Optional FPS source (MangoHud log written by the Ilmenyx config); null or empty means FPS stays unavailable.</param>
    public BottleneckPage(UsageViewModel usage, CancellationToken lifetime, Func<FrameStats?>? frameSource = null)
    {
        this.frameSource = frameSource; this.usage = usage; this.lifetime = lifetime; Spacing = 4;
        Children.Add(UiKit.PageHeader(L.T("Detector de gargalo"), L.T("Descobre o que está limitando o seu FPS: GPU, CPU, memória ou temperatura")));

        // Hero: the live verdict dominates the page.
        var mark = UiKit.Icon(Icons.Bottleneck, 96, Palette.Fg); mark.Opacity = 0.12; mark.VerticalAlignment = VerticalAlignment.Center; mark.Margin = new Thickness(16, 0, 6, 0);
        var liveText = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Spacing = 6,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Palette.Success, VerticalAlignment = VerticalAlignment.Center }, UiKit.Label(L.T("AGORA · AO VIVO")) } },
                liveTitle,
                new TextBlock { Name = "BnLiveGame", Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Text = L.T("Abra um jogo e deixe esta página aberta.") },
                liveAdvice,
            },
        };
        var heroContent = new Grid { ColumnDefinitions = new("*,Auto"), Children = { liveText } };
        Grid.SetColumn(mark, 1); heroContent.Children.Add(mark);
        var hero = UiKit.Hero(heroContent, height: 214, name: "BnHero"); hero.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(hero);

        scale.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Palette.Line, VerticalAlignment = VerticalAlignment.Center });
        scale.Children.Add(new Border { Width = 2, Height = 10, Background = Palette.Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        dot.Background = Palette.Accent; dot.BorderBrush = Palette.Bg; dot.BorderThickness = new Thickness(2);
        scale.Children.Add(dot); scale.SizeChanged += (_, _) => PlaceDot();
        cpuName.FontSize = 12.5; gpuName.FontSize = 12.5; balanceText.FontSize = 13;
        var balance = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Cpu, L.T("EQUILÍBRIO DO HARDWARE")), new Grid { ColumnDefinitions = new("*,*"), Children = { cpuName, gpuName } },
                scale, Legend(), balanceText,
                basisNote
            }
        }, new Thickness(0));

        var grid = new UniformGrid { Columns = 3 };
        foreach (var (key, label) in new[] { ("gpu", "GPU"), ("cpu", "CPU total"), ("core", L.T("Núcleo mais carregado")), ("vram", "VRAM"), ("ram", "RAM"), ("gpuT", L.T("Temp. GPU")), ("cpuT", L.T("Temp. CPU")), ("fps", "FPS") })
        { var m = new MeterView(label, "Bn_" + key) { Margin = new Thickness(0, 0, 20, 12) }; meters[key] = m; grid.Children.Add(m); }
        grid.SizeChanged += (_, e) => grid.Columns = e.NewSize.Width < 3 * 240 ? 2 : 3; // long labels ("Núcleo mais carregado") need ~240 px with their value
        var live = UiKit.Card(new StackPanel { Children = { CardTitle(Icons.Sensors, L.T("LEITURAS AO VIVO")), grid } }, new Thickness(0));
        Children.Add(Pair(balance, live, 1, 1.5, 760));

        var durations = new UiKit.ChipGroup<int>("BnDurationPanel", [("1 min", 1), ("3 min", 3), ("5 min", 5)], 1, minutes => chosen = TimeSpan.FromMinutes(minutes));
        durations.Panel.VerticalAlignment = VerticalAlignment.Center;
        start.Click += (_, _) => { if (session is { Done: false }) Finish(); else StartSession(chosen); };
        var intro = new StackPanel { Children = { CardTitle(Icons.Stress, L.T("ANÁLISE DE SESSÃO")), new TextBlock { Text = L.T("Descobre o que segura o FPS de um jogo de verdade, olhando o PC enquanto você joga. Melhor que a leitura ao vivo para quedas que vão e voltam."), Foreground = Palette.Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap } } };
        // How to use, in three steps, and what the diagnosis can say (BottleneckAnalyzer.Limiter), so the result is never a surprise.
        var steps = new StackPanel { Name = "BnHowTo", Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
        string[] how = [L.T("Escolha a duração (3 min é um bom começo) e clique em Iniciar análise."), L.T("Abra o jogo e jogue normalmente, numa parte que costuma pesar. Deixe o Ilmenyx aberto em segundo plano."), L.T("No fim (ou ao clicar em Parar análise) aparece o diagnóstico com o que mais limitou o jogo.")];
        for (int i = 0; i < how.Length; i++)
            steps.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = {
                new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Palette.AccentContainer, VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = Palette.OnAccentContainer, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
                new TextBlock { Text = how[i], FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 720, VerticalAlignment = VerticalAlignment.Center } } });
        var finds = new StackPanel { Name = "BnFinds", Spacing = 4, Margin = new Thickness(0, 14, 0, 0), Children = { UiKit.Label(L.T("O QUE ELA CONSEGUE IDENTIFICAR")) } };
        foreach (var (title, text) in new[] {
            (L.T("Placa de vídeo no limite"), L.T("o normal em jogos pesados; mais FPS só baixando qualidade ou resolução")),
            (L.T("Processador segurando o jogo"), L.T("a placa de vídeo sobra; comum em jogos competitivos em 1080p")),
            (L.T("Memória de vídeo cheia"), L.T("causa travadas; reduza a qualidade das texturas")),
            (L.T("Memória RAM cheia"), L.T("o sistema usa o disco como memória e o jogo engasga")),
            (L.T("Calor derrubando a velocidade"), L.T("CPU ou GPU quente demais reduzem o clock para se proteger")),
            (L.T("Nada no limite"), L.T("trava de FPS, V-Sync ou o jogo em menu; normal se o FPS já está bom")) })
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Palette.Muted };
            line.Inlines!.Add(new Avalonia.Controls.Documents.Run("• " + title + ": ") { Foreground = Palette.Fg, FontWeight = FontWeight.SemiBold });
            line.Inlines.Add(new Avalonia.Controls.Documents.Run(text));
            finds.Children.Add(line);
        }
        finds.Children.Add(new TextBlock { Text = L.T("As amostras ficam só na memória e somem ao fechar o Ilmenyx. Nada é gravado nem enviado."), FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        intro.Children.Add(steps); intro.Children.Add(finds);
        var controls = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 14, LineSpacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { durations.Panel, start } };
        Children.Add(UiKit.Card(new StackPanel { Children = { intro, controls, sessionStatus } }));
        // The session diagnosis is the page's secondary highlight.
        result.Margin = new Thickness(0);
        var resultCard = UiKit.FeatureCard(result, "BnResultCard"); resultCard.IsVisible = false;
        result.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) resultCard.IsVisible = result.IsVisible; };
        Children.Add(resultCard);
        Loaded += async (_, _) => { if (IsVisible) StartPolling(); await InitAsync(); };
    }

    private static StackPanel CardTitle(string icon, string text) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10), Children = { UiKit.Icon(icon, 15, Palette.AccentText), UiKit.Label(text) },
    };

    /// <summary>Two surfaces side by side in an asymmetric ratio, stacked when the page is narrower than <paramref name="breakpoint"/>.</summary>
    private static Grid Pair(Control a, Control b, double ratioA, double ratioB, double breakpoint)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 12), Children = { a, b } };
        bool? wide = null;
        void Layout(bool w)
        {
            if (wide == w) return; wide = w;
            g.ColumnDefinitions.Clear(); g.RowDefinitions.Clear();
            if (w) { g.ColumnDefinitions.Add(new ColumnDefinition(ratioA, GridUnitType.Star)); g.ColumnDefinitions.Add(new ColumnDefinition(12, GridUnitType.Pixel)); g.ColumnDefinitions.Add(new ColumnDefinition(ratioB, GridUnitType.Star)); }
            else { g.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); g.RowDefinitions.Add(new RowDefinition(12, GridUnitType.Pixel)); g.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
            Grid.SetColumn(b, w ? 2 : 0); Grid.SetRow(b, w ? 0 : 2);
        }
        Layout(true);
        g.SizeChanged += (_, e) => Layout(e.NewSize.Width >= breakpoint);
        return g;
    }
    private static Grid Legend() { var g = new Grid { Margin = new Thickness(0, 0, 0, 0) }; g.Children.Add(new TextBlock { Text = L.T("limita a GPU"), Foreground = Palette.Faint, FontSize = 11 }); g.Children.Add(new TextBlock { Text = "equilibrado", Foreground = Palette.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }); g.Children.Add(new TextBlock { Text = L.T("limita a CPU"), Foreground = Palette.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right }); return g; }

    private double ratio = 1;
    /// <summary>Marker position as in the original: logarithmic scale of the GPU/CPU ratio (0.4x … 2.5x), centre = balanced.</summary>
    private void PlaceDot()
    {
        double t = Math.Clamp((Math.Log(Math.Max(ratio, 0.01)) - Math.Log(0.4)) / (Math.Log(2.5) - Math.Log(0.4)), 0, 1);
        dot.Margin = new Thickness(Math.Max(0, t * (scale.Bounds.Width - dot.Width)), 0, 0, 0);
    }

    private async Task InitAsync()
    {
        try
        {
            var smi = await LinuxGpuCollector.QuerySmiAsync(lifetime);
            var all = await Task.Run(() => collector.Collect(ids, smi), lifetime);
            gpu = all.Count > 0 ? all[0] : null;
            string cpu = usage.Last?.CpuModel ?? "CPU";
            Grid.SetColumn(gpuName, 1); cpuName.Text = cpu; gpuName.Text = gpu?.Raw.Name ?? L.T("sem GPU");
            double? cpuKnown = GameCatalog.CpuPerf(GameCatalog.ShortCpuName(cpu)) ?? GameCatalog.CpuPerf(cpu), gpuKnown = gpu is { } g ? GameCatalog.GpuPerf(g.Raw.Name) : null;
            cpuIdx = cpuKnown ?? 80; gpuIdx = gpuKnown ?? 50;
            basisNote.Text = BasisNote(cpuKnown, gpuKnown);
            var (r, text) = BottleneckAnalyzer.Balance(gpuIdx, cpuIdx); ratio = r; balanceText.Text = text; PlaceDot();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Which indices the balance used: the table's (scale RTX 4070 = 100, Ryzen 7 7700X = 100) or the default for a part that is not in it.</summary>
    public static string BasisNote(double? cpu, double? gpu) =>
        L.T("Estimativa pelo desempenho relativo típico; o detector ao vivo mostra a realidade de cada jogo. ") +
        (cpu is not null && gpu is not null ? L.T($"Índices da tabela: CPU {cpu:0.#}, GPU {gpu:0.#} (RTX 4070 = 100).")
         : L.T($"Fora da tabela, valor padrão: {(cpu is null ? "CPU 80" : "")}{(cpu is null && gpu is null ? " e " : "")}{(gpu is null ? "GPU 50" : "")}."));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;
        if (IsVisible) StartPolling(); else if (session is null) { poll?.Cancel(); poll = null; }
    }

    private void StartPolling()
    {
        if (poll is not null) return;
        poll = CancellationTokenSource.CreateLinkedTokenSource(lifetime); var token = poll.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                do
                {
                    GpuTelemetry? t = gpu is { } g ? await collector.TelemetryAsync(g.Raw, token) : null;
                    Dispatcher.UIThread.Post(() => { if (!token.IsCancellationRequested) Tick(t); });
                } while (await timer.WaitForNextTickAsync(token));
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public LoadSample Build(UsageSample? u, GpuTelemetry? t) => new(
        t?.LoadPercent, u?.CpuPercent, u?.CpuMaxCorePercent, u?.BusiestCore,
        t is { VramUsed: { } used, VramTotal: > 0 } ? 100.0 * used / t.VramTotal.Value : null,
        u is { MemoryTotal: > 0, MemoryUsed: { } mu } ? 100.0 * mu / u.MemoryTotal.Value : null,
        t?.TempC, u?.CpuTempC, t?.ThrottleMask is { } mask && (mask & 0x60) != 0, frameSource?.Invoke()?.Fps);

    public void Tick(GpuTelemetry? t)
    {
        var s = Build(usage.Last, t);
        void M(string key, double? v, string unit, double max) => meters[key].Set(v is { } x ? x.ToString("0", CultureInfo.InvariantCulture) + unit : "—", v is { } y ? y / max : 0);
        M("gpu", s.GpuLoad, " %", 100); M("cpu", s.CpuTotal, " %", 100);
        meters["core"].Set(s.CpuMaxCore is { } c ? $"{c.ToString("0", CultureInfo.InvariantCulture)} % ({s.BusiestCore})" : "—", (s.CpuMaxCore ?? 0) / 100);
        M("vram", s.VramPct, " %", 100); M("ram", s.RamPct, " %", 100); M("gpuT", s.GpuTemp, " °C", 100); M("cpuT", s.CpuTemp, " °C", 100);
        meters["fps"].Set(s.Fps is { } fps ? fps.ToString("0", CultureInfo.InvariantCulture) : L.T("indisponível"), s.Fps is { } f2 ? Math.Min(1, f2 / 240) : 0); // FPS only from a MangoHud log written with the Ilmenyx config
        var limiter = s.Classify(); LiveLimiter = limiter;
        liveTitle.Text = BottleneckAnalyzer.Title(limiter); liveAdvice.Text = BottleneckAnalyzer.Advice(limiter);
        if (session is { } sess)
        {
            sess.Add(s); sessionStatus.Text = L.T($"Analisando… {(int)(sess.Progress * 100)} % · {sess.Count} amostra(s). Jogue normalmente.");
            if (sess.Done) Finish();
        }
    }

    public void StartSession(TimeSpan duration)
    {
        session = new BottleneckSession(duration); result.IsVisible = false; start.Content = L.T("Parar análise");
        sessionStatus.Text = L.T("Análise iniciada. Abra o jogo e jogue normalmente."); StartPolling();
    }

    private void Finish()
    {
        if (session is not { } s) return;
        start.Content = L.T("Iniciar análise");
        if (s.Count == 0 || s.Main is not { } main) { sessionStatus.Text = L.T("Nenhuma amostra coletada."); session = null; return; }
        result.Children.Clear();
        var caption = UiKit.Label(L.T("DIAGNÓSTICO DA SESSÃO")); caption.Foreground = Palette.Muted; result.Children.Add(caption);
        result.Children.Add(new TextBlock { Name = "BnResultTitle", Text = BottleneckAnalyzer.Title(main), FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        string F(double? v, string u) => v is { } x ? x.ToString("0", CultureInfo.InvariantCulture) + u : "—";
        result.Children.Add(new TextBlock { Name = "BnResultSummary", Foreground = Palette.Muted, Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap,
            Text = L.T($"{s.Count} amostra(s) · GPU média {F(s.Avg(x => x.GpuLoad), " %")} · CPU média {F(s.Avg(x => x.CpuTotal), " %")} (pico de núcleo {F(s.Max(x => x.CpuMaxCore), " %")}) · RAM média {F(s.Avg(x => x.RamPct), " %")} · FPS: não medido") });
        foreach (var (limiter, pct) in s.Distribution())
        { var m = new MeterView(BottleneckAnalyzer.Title(limiter), "BnDist_" + limiter); m.Set(pct.ToString("0", CultureInfo.InvariantCulture) + " %", pct / 100); result.Children.Add(m); }
        result.Children.Add(new TextBlock { Name = "BnResultAdvice", Text = BottleneckAnalyzer.Advice(main), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        result.IsVisible = true; sessionStatus.Text = L.T($"Concluído às {DateTime.Now:HH:mm:ss}."); session = null;
    }
}
