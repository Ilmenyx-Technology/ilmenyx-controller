// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
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
    private readonly Button start = UiKit.Primary("BtnBnSession", "INICIAR ANÁLISE", 18, 9);
    public BottleneckSession? Session => session;
    public Limiter? LiveLimiter { get; private set; }

    private readonly Func<FrameStats?>? frameSource;

    /// <param name="frameSource">Optional FPS source (MangoHud log written by the Ilmenyx config); null or empty means FPS stays unavailable.</param>
    public BottleneckPage(UsageViewModel usage, CancellationToken lifetime, Func<FrameStats?>? frameSource = null)
    {
        this.frameSource = frameSource; this.usage = usage; this.lifetime = lifetime; Spacing = 4;
        Children.Add(UiKit.PageHeader("Detector de gargalo", "Descobre o que está limitando o seu FPS: GPU, CPU, memória ou temperatura"));

        // Hero: the live verdict dominates the page.
        var mark = UiKit.Icon(Icons.Bottleneck, 96, Palette.Fg); mark.Opacity = 0.12; mark.VerticalAlignment = VerticalAlignment.Center; mark.Margin = new Thickness(16, 0, 6, 0);
        var liveText = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Spacing = 6,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Palette.Success, VerticalAlignment = VerticalAlignment.Center }, UiKit.Label("AGORA · AO VIVO") } },
                liveTitle,
                new TextBlock { Name = "BnLiveGame", Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Text = "Abra um jogo e deixe esta página aberta." },
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
                CardTitle(Icons.Cpu, "EQUILÍBRIO DO HARDWARE"), new Grid { ColumnDefinitions = new("*,*"), Children = { cpuName, gpuName } },
                scale, Legend(), balanceText,
                new TextBlock { Text = "Estimativa pelo desempenho relativo típico; o detector ao vivo mostra a realidade de cada jogo. Hardware fora da tabela usa valores padrão (CPU 80, GPU 50).", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }
            }
        }, new Thickness(0));

        var grid = new UniformGrid { Columns = 3 };
        foreach (var (key, label) in new[] { ("gpu", "GPU"), ("cpu", "CPU total"), ("core", "Núcleo mais carregado"), ("vram", "VRAM"), ("ram", "RAM"), ("gpuT", "Temp. GPU"), ("cpuT", "Temp. CPU"), ("fps", "FPS") })
        { var m = new MeterView(label, "Bn_" + key) { Margin = new Thickness(0, 0, 20, 12) }; meters[key] = m; grid.Children.Add(m); }
        grid.SizeChanged += (_, e) => grid.Columns = e.NewSize.Width < 3 * 240 ? 2 : 3; // long labels ("Núcleo mais carregado") need ~240 px with their value
        var live = UiKit.Card(new StackPanel { Children = { CardTitle(Icons.Sensors, "LEITURAS AO VIVO"), grid } }, new Thickness(0));
        Children.Add(Pair(balance, live, 1, 1.5, 760));

        var durations = new UiKit.ChipGroup<int>("BnDurationPanel", [("1 min", 1), ("3 min", 3), ("5 min", 5)], 1, minutes => chosen = TimeSpan.FromMinutes(minutes));
        durations.Panel.VerticalAlignment = VerticalAlignment.Center;
        start.Click += (_, _) => { if (session is { Done: false }) Finish(); else StartSession(chosen); };
        var intro = new StackPanel { Children = { CardTitle(Icons.Stress, "ANÁLISE DE SESSÃO"), new TextBlock { Text = "Clique em iniciar, abra o jogo e jogue normalmente. Ao final você recebe o diagnóstico. As amostras ficam só na memória.", Foreground = Palette.Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap } } };
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
    private static Grid Legend() { var g = new Grid { Margin = new Thickness(0, 0, 0, 0) }; g.Children.Add(new TextBlock { Text = "limita a GPU", Foreground = Palette.Faint, FontSize = 11 }); g.Children.Add(new TextBlock { Text = "equilibrado", Foreground = Palette.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }); g.Children.Add(new TextBlock { Text = "limita a CPU", Foreground = Palette.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right }); return g; }

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
            Grid.SetColumn(gpuName, 1); cpuName.Text = cpu; gpuName.Text = gpu?.Raw.Name ?? "sem GPU";
            cpuIdx = GameCatalog.CpuPerf(GameCatalog.ShortCpuName(cpu)) ?? GameCatalog.CpuPerf(cpu) ?? 80;
            gpuIdx = (gpu is { } g ? GameCatalog.GpuPerf(g.Raw.Name) : null) ?? 50;
            var (r, text) = BottleneckAnalyzer.Balance(gpuIdx, cpuIdx); ratio = r; balanceText.Text = text; PlaceDot();
        }
        catch (OperationCanceledException) { }
    }

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
        meters["fps"].Set(s.Fps is { } fps ? fps.ToString("0", CultureInfo.InvariantCulture) : "indisponível", s.Fps is { } f2 ? Math.Min(1, f2 / 240) : 0); // FPS only from a MangoHud log written with the Ilmenyx config
        var limiter = s.Classify(); LiveLimiter = limiter;
        liveTitle.Text = BottleneckAnalyzer.Title(limiter); liveAdvice.Text = BottleneckAnalyzer.Advice(limiter);
        if (session is { } sess)
        {
            sess.Add(s); sessionStatus.Text = $"Analisando… {(int)(sess.Progress * 100)} % · {sess.Count} amostra(s). Jogue normalmente.";
            if (sess.Done) Finish();
        }
    }

    public void StartSession(TimeSpan duration)
    {
        session = new BottleneckSession(duration); result.IsVisible = false; start.Content = "PARAR ANÁLISE";
        sessionStatus.Text = "Análise iniciada. Abra o jogo e jogue normalmente."; StartPolling();
    }

    private void Finish()
    {
        if (session is not { } s) return;
        start.Content = "INICIAR ANÁLISE";
        if (s.Count == 0 || s.Main is not { } main) { sessionStatus.Text = "Nenhuma amostra coletada."; session = null; return; }
        result.Children.Clear();
        var caption = UiKit.Label("DIAGNÓSTICO DA SESSÃO"); caption.Foreground = Palette.Muted; result.Children.Add(caption);
        result.Children.Add(new TextBlock { Name = "BnResultTitle", Text = BottleneckAnalyzer.Title(main), FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        string F(double? v, string u) => v is { } x ? x.ToString("0", CultureInfo.InvariantCulture) + u : "—";
        result.Children.Add(new TextBlock { Name = "BnResultSummary", Foreground = Palette.Muted, Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap,
            Text = $"{s.Count} amostra(s) · GPU média {F(s.Avg(x => x.GpuLoad), " %")} · CPU média {F(s.Avg(x => x.CpuTotal), " %")} (pico de núcleo {F(s.Max(x => x.CpuMaxCore), " %")}) · RAM média {F(s.Avg(x => x.RamPct), " %")} · FPS: não medido" });
        foreach (var (limiter, pct) in s.Distribution())
        { var m = new MeterView(BottleneckAnalyzer.Title(limiter), "BnDist_" + limiter); m.Set(pct.ToString("0", CultureInfo.InvariantCulture) + " %", pct / 100); result.Children.Add(m); }
        result.Children.Add(new TextBlock { Name = "BnResultAdvice", Text = BottleneckAnalyzer.Advice(main), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        result.IsVisible = true; sessionStatus.Text = $"Concluído às {DateTime.Now:HH:mm:ss}."; session = null;
    }
}
