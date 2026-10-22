// New Ilmenyx additions (layout follows the original bottleneck page: balance, live verdict, live meters, session analysis): see LICENSING.md.
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
    private readonly TextBlock liveTitle = new() { Name = "BnLiveTitle", Text = "—", FontSize = 24, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock liveAdvice = new() { Name = "BnLiveAdvice", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private readonly TextBlock sessionStatus = new() { Name = "BnSessionStatus", Foreground = Palette.Muted, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel result = new() { Name = "BnResult", IsVisible = false, Margin = new Thickness(0, 16, 0, 0) };
    private readonly Button start = new() { Name = "BtnBnSession", Content = "INICIAR ANÁLISE", Background = Palette.Accent, Foreground = Brushes.Black, FontWeight = FontWeight.Bold, Padding = new Thickness(18, 9) };
    public BottleneckSession? Session => session;
    public Limiter? LiveLimiter { get; private set; }

    public BottleneckPage(UsageViewModel usage, CancellationToken lifetime)
    {
        this.usage = usage; this.lifetime = lifetime; Spacing = 4;
        Children.Add(new StackPanel { Margin = new Thickness(0, 4, 0, 20), Children = { new TextBlock { Text = "Detector de gargalo", FontSize = 26, FontWeight = FontWeight.Bold }, new TextBlock { Text = "Descobre o que está limitando o seu FPS: GPU, CPU, memória ou temperatura", Foreground = Palette.Muted } } });

        var balance = Card(new StackPanel
        {
            Children =
            {
                Label("EQUILÍBRIO DO HARDWARE"), new Grid { Margin = new Thickness(0, 4, 0, 0), ColumnDefinitions = new("*,*"), Children = { cpuName, gpuName } },
                scale, Legend(), balanceText,
                new TextBlock { Text = "Estimativa pelo desempenho relativo típico; o detector ao vivo mostra a realidade de cada jogo. Hardware fora da tabela usa valores padrão (CPU 80, GPU 50).", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }
            }
        });
        scale.Children.Add(new Border { Height = 4, Background = Palette.Line, VerticalAlignment = VerticalAlignment.Center });
        scale.Children.Add(new Border { Width = 2, Height = 10, Background = Palette.Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        scale.Children.Add(dot); scale.SizeChanged += (_, _) => PlaceDot();
        var liveCard = Card(new StackPanel { Children = { Label("AGORA"), liveTitle, new TextBlock { Name = "BnLiveGame", Foreground = Palette.Muted, Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap, Text = "Abra um jogo e deixe esta página aberta." }, liveAdvice } });
        var top = new Grid { ColumnDefinitions = new("*,14,*"), Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetColumn(liveCard, 2); top.Children.Add(balance); top.Children.Add(liveCard);
        Children.Add(top);

        var grid = new UniformGrid { Columns = 3 };
        foreach (var (key, label) in new[] { ("gpu", "GPU"), ("cpu", "CPU total"), ("core", "Núcleo mais carregado"), ("vram", "VRAM"), ("ram", "RAM"), ("gpuT", "Temp. GPU"), ("cpuT", "Temp. CPU"), ("fps", "FPS") })
        { var m = new MeterView(label, "Bn_" + key) { Margin = new Thickness(0, 0, 24, 12) }; meters[key] = m; grid.Children.Add(m); }
        Children.Add(new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 16, 18, 6), Margin = new Thickness(0, 0, 0, 14), Child = new StackPanel { Children = { Label("LEITURAS AO VIVO"), new Border { Height = 4 }, grid } } });

        var durations = new StackPanel { Name = "BnDurationPanel", Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var chips = new List<Button>();
        foreach (var minutes in new[] { 1, 3, 5 })
        {
            var chip = new Button { Content = minutes + " min", FontSize = 11, Padding = new Thickness(14, 5), CornerRadius = new CornerRadius(16), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
            void Style(bool on) { chip.Background = on ? Palette.Fg : Brushes.Transparent; chip.Foreground = on ? Brushes.Black : Palette.Muted; }
            Style(minutes == 1); chips.Add(chip);
            chip.Click += (_, _) => { chosen = TimeSpan.FromMinutes(minutes); foreach (var c in chips) { bool on = ReferenceEquals(c, chip); c.Background = on ? Palette.Fg : Brushes.Transparent; c.Foreground = on ? Brushes.Black : Palette.Muted; } };
            durations.Children.Add(chip);
        }
        start.Click += (_, _) => { if (session is { Done: false }) Finish(); else StartSession(chosen); };
        var intro = new StackPanel { Children = { Label("ANÁLISE DE SESSÃO"), new TextBlock { Text = "Clique em iniciar, abra o jogo e jogue normalmente. Ao final você recebe o diagnóstico. As amostras ficam só na memória.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { durations, start } };
        Children.Add(new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 18), Child = new StackPanel { Children = { intro, controls, sessionStatus, result } } });
        Loaded += async (_, _) => { if (IsVisible) StartPolling(); await InitAsync(); };
    }

    private static TextBlock Label(string t) => new() { Text = t, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, Margin = new Thickness(0, 0, 0, 4) };
    private static Border Card(Control child) => new() { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 18), Child = child };
    private static Grid Legend() { var g = new Grid(); g.Children.Add(new TextBlock { Text = "limita a GPU", Foreground = Palette.Faint, FontSize = 11 }); g.Children.Add(new TextBlock { Text = "equilibrado", Foreground = Palette.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }); g.Children.Add(new TextBlock { Text = "limita a CPU", Foreground = Palette.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right }); return g; }

    private double ratio = 1;
    private void PlaceDot()
    {
        double pos = Math.Clamp(0.5 + 0.25 * Math.Log2(Math.Max(ratio, 0.01)), 0.03, 0.97);
        dot.Margin = new Thickness(Math.Max(0, scale.Bounds.Width * pos - 7), 0, 0, 0);
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
        t?.TempC, u?.CpuTempC, t?.ThrottleMask is { } mask && (mask & 0x60) != 0, null);

    public void Tick(GpuTelemetry? t)
    {
        var s = Build(usage.Last, t);
        void M(string key, double? v, string unit, double max) => meters[key].Set(v is { } x ? x.ToString("0", CultureInfo.InvariantCulture) + unit : "—", v is { } y ? y / max : 0);
        M("gpu", s.GpuLoad, " %", 100); M("cpu", s.CpuTotal, " %", 100);
        meters["core"].Set(s.CpuMaxCore is { } c ? $"{c.ToString("0", CultureInfo.InvariantCulture)} % ({s.BusiestCore})" : "—", (s.CpuMaxCore ?? 0) / 100);
        M("vram", s.VramPct, " %", 100); M("ram", s.RamPct, " %", 100); M("gpuT", s.GpuTemp, " °C", 100); M("cpuT", s.CpuTemp, " °C", 100);
        meters["fps"].Set("indisponível", 0); // FPS needs a frame source (MangoHud/gamescope); added with the Overlay module.
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
        result.Children.Add(new TextBlock { Name = "BnResultTitle", Text = BottleneckAnalyzer.Title(main), FontSize = 22, FontWeight = FontWeight.SemiBold });
        string F(double? v, string u) => v is { } x ? x.ToString("0", CultureInfo.InvariantCulture) + u : "—";
        result.Children.Add(new TextBlock { Name = "BnResultSummary", Foreground = Palette.Muted, Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap,
            Text = $"{s.Count} amostra(s) · GPU média {F(s.Avg(x => x.GpuLoad), " %")} · CPU média {F(s.Avg(x => x.CpuTotal), " %")} (pico de núcleo {F(s.Max(x => x.CpuMaxCore), " %")}) · RAM média {F(s.Avg(x => x.RamPct), " %")} · FPS: não medido" });
        foreach (var (limiter, pct) in s.Distribution())
        { var m = new MeterView(BottleneckAnalyzer.Title(limiter), "BnDist_" + limiter); m.Set(pct.ToString("0", CultureInfo.InvariantCulture) + " %", pct / 100); result.Children.Add(m); }
        result.Children.Add(new TextBlock { Name = "BnResultAdvice", Text = BottleneckAnalyzer.Advice(main), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        result.IsVisible = true; sessionStatus.Text = $"Concluído às {DateTime.Now:HH:mm:ss}."; session = null;
    }
}
