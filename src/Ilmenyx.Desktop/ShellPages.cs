// New Ilmenyx additions: see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed partial class ShellWindow
{
    private void BuildPages()
    {
        pages["dash"] = ("Visão geral", Overview, null);
        pages["system"] = ("Sistema", SystemPage, null);
        pages["sensors"] = ("Sensores", () => Scroll("SensorsScroll", new SensorPanel(sensors, Destination("sensors"), stop.Token)), null);
        pages["gpu"] = ("GPU", GpuPage, null);
        pages["disks"] = ("Discos", DisksPage, null);
        pages["energy"] = ("Energia", EnergyPage, null);
        pages["net"] = ("Rede", NetPage, null);
        pages["procs"] = ("Processos", () => new ProcessTable(usage), null);
        pages["launcher"] = ("Jogos", () =>
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var gpus = new LinuxGpuCollector().Collect(PciIds.Load());
            string vendor = gpus.Count > 0 ? gpus[0].Card.Vendor : "";
            return new LauncherPage(new LinuxGameLibrary(home), new SteamInfo(home), IlmenyxSettings.DefaultPath(),
                (g, p, v, status, learned, ct) => new LinuxGameLauncher().RunAsync(g, p, v, status, learned, ct), () => this, vendor, gpus.Count(x => !x.Raw.Integrated) + gpus.Count(x => x.Raw.Integrated) > 1, stop.Token) { Margin = new Thickness(0, 4, 8, 12) };
        }, null);
        pages["fps"] = ("Estimativa de FPS", () =>
        {
            string cache = Path.Combine(Path.GetDirectoryName(IlmenyxSettings.DefaultPath())!, "fps-cache.json");
            return new GamesFpsPage(new HowManyFpsClient(cache), IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()), IlmenyxSettings.DefaultPath(), () => this,
                () => (usage.Last?.CpuModel ?? "", new LinuxGpuCollector().Collect(PciIds.Load()).FirstOrDefault().Raw?.Name ?? ""), stop.Token) { Margin = new Thickness(0, 4, 8, 12) };
        }, null);
        pages["bottleneck"] = ("Detector de gargalo", () => Scroll("BottleneckScroll", new BottleneckPage(usage, stop.Token, OperatingSystem.IsLinux() ? () => MangoHudLogReader.Read(MangoHudLogReader.DefaultFolder(), DateTimeOffset.UtcNow) : null) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["stress"] = ("Stress test", () => Scroll("StressScroll", stressPage = new StressPage(new StressTester(), usage, () => this, () => usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l ? l.MemoryTotal!.Value - u : 0, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["bench"] = ("Benchmark", () => Scroll("BenchScroll", new BenchPage(OperatingSystem.IsLinux() ? BenchServices.Real() : throw new PlatformNotSupportedException(), usage, () => this, IlmenyxSettings.DefaultPath(), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["overlay"] = ("Overlay", () => Scroll("OverlayScroll", new OverlayPage(OperatingSystem.IsLinux() ? RealOverlayServices() : throw new PlatformNotSupportedException(), usage, IlmenyxSettings.DefaultPath(), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["lighting"] = ("Integração", () => Scroll("LightingScroll", new LightingPage(RealLightingServices(), usage, () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["oc"] = ("OC", () => Scroll("OcScroll", new OcPage(OperatingSystem.IsLinux() ? RealOcServices() : throw new PlatformNotSupportedException(), usage, () => this, IlmenyxSettings.DefaultPath(), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["validate"] = ("Validações reduzidas", ValidatePage, null);
        pages["makerports"] = ("Maker · Portas", () => Scroll("MakerPortsScroll", new MakerPortsPage(stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["makerdrv"] = ("Maker · Drivers seriais", () => Scroll("MakerDrvScroll", new MakerDriversPage(stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["optimize"] = ("Otimiza", () => Scroll("OptimizeScroll", new OptimizePage(new LinuxOptimizer(), new OptimizeServices(LinuxGameLauncher.PowerProfileAsync, LinuxGameLauncher.SetPowerProfileAsync), usage, IlmenyxSettings.DefaultPath(), () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["startup"] = ("Inicialização", () => Scroll("StartupScroll", new StartupPage(LinuxStartupManager.CreateDefault(), () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["apps"] = ("Instalar apps", () => Scroll("AppsScroll", new AppsPage(new LinuxAppInstaller(), LinuxAppInstaller.FlatpakAvailable, () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["driver"] = ("Drivers", () => Scroll("DriverScroll", new DriverPage(Destination("drivers"), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["report"] = ("Relatório", () => Scroll("ReportScroll", new ReportPage(GatherReportAsync, Destination("report"), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["tools"] = ("Requisitos e sobre", ToolsPage, null);
    }

    private Task<ReportData> GatherReportAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var smart = smartImport.Report is { } r ? new Dictionary<string, Ilmenyx.Contracts.SmartDiagnostic> { [r.Diagnostic.Device] = r.Diagnostic } : [];
        string version = typeof(ShellWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        return LinuxReportGatherer.GatherAsync(version, null, hardware.Snapshot?.Gpus, sensors.Snapshot?.Readings ?? [], smart, networkPage?.LastResult,
            IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).BenchRuns, ct);
    }

    private LightingServices RealLightingServices() => new(OpenRgbClient.ConnectAsync, () => null,
        (url, ct) => OperatingSystem.IsLinux() ? DriverTools.OpenUrlAsync(url, ct) : Task.CompletedTask, () => OperatingSystem.IsLinux() && BoundedProcess.Find("openrgb") is not null);

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private OverlayServices RealOverlayServices()
    {
        var collector = new LinuxGpuCollector(); var ids = PciIds.Load(); GpuRaw? raw = null;
        return new OverlayServices(async ct =>
        {
            raw ??= collector.Collect(ids).FirstOrDefault().Raw;
            return raw is null ? null : await collector.TelemetryAsync(raw, ct);
        }, now => MangoHudLogReader.Read(MangoHudLogReader.DefaultFolder(), now), MangoHudLogReader.ConfigPath(), MangoHudLogReader.DefaultFolder(), () => BoundedProcess.Find("mangohud") is not null);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private OcServices RealOcServices()
    {
        var kernel = new LinuxKernelLog();
        return new OcServices(new StressTester(), () => usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l ? l.MemoryTotal!.Value - u : LinuxMemoryInfo.AvailableBytes(),
            async ct => (await new Ilmenyx.Core.Benchmarks.CpuBenchmarkRunner().RunAsync(new Ilmenyx.Contracts.CpuBenchmarkOptions { Profile = Ilmenyx.Contracts.CpuWorkloadProfile.Full, Threads = Environment.ProcessorCount, AllowCpuLoad = true, Timeout = TimeSpan.FromMinutes(5) }, ct)).Result.ExperimentalScore,
            (since, ct) => kernel.ErrorsSinceAsync(since, ct), () => LinuxKernelLog.Board(), LinuxKernelLog.NvidiaStateAsync, () => usage.Last?.CpuModel ?? "");
    }

    private static Border CardBox(Control child, Thickness? padding = null) => new()
    {
        Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = padding ?? new Thickness(22, 18), Child = child
    };
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Wrapped(string name, string text, double size = 14, IBrush? brush = null)
    {
        var t = new TextBlock { Name = name, Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (brush is not null) t.Foreground = brush;
        return t;
    }
    private static ScrollViewer Scroll(string name, Control content) => new() { Name = name, Content = content };
    private static Button Primary(string name, string text) => new() { Name = name, Content = text, Background = Palette.Accent, Foreground = Brushes.Black, FontWeight = FontWeight.Bold, Padding = new Thickness(20, 12), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(12) };

    private static void ChipStyle(Button b, bool on) { b.Background = on ? Palette.Fg : Brushes.Transparent; b.Foreground = on ? Palette.OnFg : Palette.Muted; }
    private static StackPanel Chips<T>(string name, (string Label, T Value)[] options, T initial, Action<T> chosen)
    {
        var panel = new StackPanel { Name = name, Orientation = Orientation.Horizontal, Spacing = 6 };
        var buttons = new List<Button>();
        foreach (var (label, value) in options)
        {
            var b = new Button { Content = label, FontSize = 11, Padding = new Thickness(14, 5), CornerRadius = new CornerRadius(16), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
            ChipStyle(b, EqualityComparer<T>.Default.Equals(value, initial)); buttons.Add(b);
            var chosenValue = value;
            b.Click += (_, _) => { foreach (var o in buttons) ChipStyle(o, ReferenceEquals(o, b)); chosen(chosenValue); };
            panel.Children.Add(b);
        }
        return panel;
    }

    private Control Overview()
    {
        int range = 300; string metric = "load";
        var chart = new UsageChart { Name = "OverviewChart", Height = 210, HorizontalAlignment = HorizontalAlignment.Stretch };
        var chartLabel = Label("CPU · CARGA");
        var hero = new TextBlock { Name = "OverviewValue", Text = "—", FontSize = 44, FontWeight = FontWeight.Bold, FontFamily = new FontFamily("monospace") };
        var gpuLoad = new List<double>(); var gpuValue = new TextBlock { Name = "OverviewGpuValue", Text = "—", FontSize = 22, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right };
        var gpuLegend = new TextBlock { Name = "OverviewLegendGpu", Text = "GPU: sem contadores de uso neste monitor", FontSize = 11, Foreground = Palette.Muted };
        void Refresh()
        {
            bool temp = metric == "temp";
            chart.SetValues(temp ? usage.TempSeries : usage.CpuSeries, temp ? null : 100, range);
            chart.SetSecond(!temp && gpuLoad.Count > 0 ? gpuLoad : []);
            chartLabel.Text = temp ? "CPU · TEMPERATURA" : "CPU · CARGA";
            hero.Text = temp ? (usage.Last?.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—") : UsageViewModel.Pct(usage.Last?.CpuPercent);
        }
        usage.Updated += Refresh;
        var ranges = Chips("DashRange", [("1 MIN", 60), ("5 MIN", 300), ("15 MIN", 900)], 300, v => { range = v; Refresh(); });
        var metrics = Chips("DashMetric", [("Temperatura", "temp"), ("Carga", "load")], "load", v => { metric = v; Refresh(); });
        var power = new Button { Name = "DashMetricPower", Content = "Consumo", FontSize = 11, Padding = new Thickness(14, 5), CornerRadius = new CornerRadius(16), IsEnabled = false };
        ToolTip.SetTip(power, "Indisponível: consumo da CPU (RAPL) ainda não é lido no Linux.");
        metrics.Children.Add(power);
        var chips = new Grid { Margin = new Thickness(0, 16, 0, 8) };
        metrics.HorizontalAlignment = HorizontalAlignment.Right; chips.Children.Add(ranges); chips.Children.Add(metrics);
        var gpuTag = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Children = { Label("GPU"), gpuValue } };
        var top = new Grid { Children = { new StackPanel { Children = { chartLabel, hero } }, gpuTag } };
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Spacing = 6, Children = { new Border { Width = 14, Height = 2, Background = Palette.Accent, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "CPU", FontSize = 11, Foreground = Palette.Muted }, new Border { Width = 14, Height = 2, Background = Palette.Warn, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }, gpuLegend } };
        var chartCard = CardBox(new StackPanel { Children = { top, chips, chart, legend } }, new Thickness(24, 20));

        // Right card: performance from the local benchmark history, like the benchmark card of the original (experimental scores).
        var open = Primary("OpenBench", "ABRIR BENCHMARK");
        open.Click += (_, _) => Select("bench");
        var openValidations = new Button { Name = "OpenValidations", Content = "Validações reduzidas", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        openValidations.Click += (_, _) => Select("validate");
        var notice = new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = Palette.WarnBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Margin = new Thickness(0, 10, 0, 0), Child = new TextBlock { Name = "OverviewGpuNotice", Text = ValidationNotices.AmdNvidia, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap, FontSize = 12 } };
        var memoryRow = Wrapped("OverviewMemory", "—", 13, Palette.Muted); var procRow = Wrapped("OverviewProcesses", "—", 13, Palette.Muted); var cpuName = Wrapped("OverviewCpuName", "—", 12, Palette.Muted);
        usage.Updated += () => { memoryRow.Text = usage.MemoryLine; procRow.Text = usage.ProcessSummary; cpuName.Text = usage.CpuModel; };
        var score = new TextBlock { Name = "OverviewScore", Text = "—", FontSize = 30, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) };
        var scoreDelta = new TextBlock { Name = "OverviewScoreDelta", FontSize = 13, Foreground = Palette.Muted };
        var scoreSub = Wrapped("OverviewScoreSub", "rode o Benchmark para ter uma nota", 11, Palette.Faint);
        var scoreRows = new StackPanel { Name = "OverviewScores", Margin = new Thickness(0, 8, 0, 0) };
        void RefreshScores()
        {
            var summary = PerformanceSummary.From(IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).BenchRuns);
            score.Text = summary.CpuScore is double sc ? sc.ToString("N0", CultureInfo.InvariantCulture) : "—";
            scoreDelta.Text = summary.CpuDelta is double d ? (d >= 0 ? "▲ " : "▼ ") + Math.Abs(d).ToString("N0", CultureInfo.InvariantCulture) : "";
            scoreSub.Text = summary.CpuSubtitle + (summary.CpuScore is null ? "" : " · experimental, não comparável ao Windows");
            scoreRows.Children.Clear();
            foreach (var kv in summary.Others)
                scoreRows.Children.Add(new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = kv.Key, FontSize = 12, Foreground = Palette.Muted }, new TextBlock { Text = kv.Value, FontSize = 12, FontFamily = new FontFamily("monospace"), [Grid.ColumnProperty] = 1 } } });
        }
        overviewRefresh += RefreshScores; RefreshScores();
        var side = CardBox(new StackPanel
        {
            Children =
            {
                Label("DESEMPENHO"), score, scoreDelta, scoreSub, scoreRows,
                new Border { Height = 1, Background = Palette.Line, Margin = new Thickness(0, 14) }, Label("MEMÓRIA"), memoryRow, new Border { Height = 8 }, Label("PROCESSOS"), procRow, notice,
                new Border { Height = 14 }, open, openValidations
            }
        }, new Thickness(22, 20));
        var row1 = new Grid { ColumnDefinitions = new("*,16,330"), Margin = new Thickness(0, 6, 0, 16) };
        Grid.SetColumn(side, 2); row1.Children.Add(chartCard); row1.Children.Add(side);

        // Component table: COMPONENTE | TEMPERATURA | CARGA | CLOCK | CONSUMO / USO | VER
        var rows = new StackPanel { Name = "DashComponents" };
        var head = new Grid { ColumnDefinitions = new("2.2*,*,*,*,1.6*,56"), Margin = new Thickness(0, 4, 0, 10) };
        string[] titles = ["COMPONENTE", "TEMPERATURA", "CARGA", "CLOCK", "CONSUMO / USO", "VER"];
        for (int i = 0; i < titles.Length; i++) { var l = Label(titles[i]); Grid.SetColumn(l, i); if (i == 5) l.HorizontalAlignment = HorizontalAlignment.Right; head.Children.Add(l); }
        void Components()
        {
            rows.Children.Clear();
            var s = usage.Last;
            string mhz = s?.CpuMhz is { } m ? m.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "—";
            string tmp = s?.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—";
            AddRow("CPU", s?.CpuModel ?? "", tmp, UsageViewModel.Pct(s?.CpuPercent), mhz, "—", "system", "GoCpu");
            AddRow("GPU", "Inventário DRM/PCI", "—", "—", "—", "sem contadores", "gpu", "GoGpu");
            AddRow("Memória", usage.MemorySub, "—", s is { MemoryTotal: > 0, MemoryUsed: { } u } ? UsageViewModel.Pct(100.0 * u / s.MemoryTotal.Value) : "—", "—", usage.MemoryLine, "system", "GoMemory");
            AddRow("Discos", "Dispositivos inteiros", "—", "—", "—", usage.DiskLine, "disks", "GoDisk");
            AddRow("Rede", usage.NetSub, "—", "—", "—", usage.NetLine, "net", "GoNet");
        }
        void AddRow(string name, string model, string temp, string load, string clock, string use, string target, string goName)
        {
            var g = new Grid { ColumnDefinitions = new("2.2*,*,*,*,1.6*,56") };
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = name, FontSize = 13 }, new TextBlock { Text = model, FontSize = 11, Foreground = Palette.Faint, TextTrimming = TextTrimming.CharacterEllipsis } } };
            g.Children.Add(title);
            void Cell(int col, string text, bool muted) { var t = new TextBlock { Text = text, FontFamily = new FontFamily("monospace"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; if (muted) t.Foreground = Palette.Muted; Grid.SetColumn(t, col); g.Children.Add(t); }
            Cell(1, temp, false); Cell(2, load, false); Cell(3, clock, true); Cell(4, use, true);
            var go = new Button { Name = goName, Content = "›", Width = 30, Height = 30, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent };
            AutomationPropertiesName(go, "Abrir " + name);
            go.Click += (_, _) => Select(target);
            Grid.SetColumn(go, 5); g.Children.Add(go);
            rows.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 9), Child = g });
        }
        usage.Updated += Components; Components();
        var table = CardBox(new StackPanel { Children = { head, rows } }, new Thickness(22, 14, 22, 8));
        // Health checklist, recent events and occupancy rings.
        var healthList = new StackPanel { Name = "OverviewHealth", Spacing = 6 };
        var healthStatus = new TextBlock { Name = "OverviewHealthStatus", FontSize = 11, Foreground = Palette.Faint };
        var refreshHealth = new Button { Name = "BtnHealthRefresh", Content = "VERIFICAR AGORA", Padding = new Thickness(12, 4), FontSize = 11 };
        refreshHealth.Click += async (_, _) => await RefreshHealthAsync();
        var eventsList = new StackPanel { Name = "OverviewEvents", Spacing = 4 };
        var rings = new WrapPanel { Name = "OverviewRings" };
        async Task RefreshHealthAsync()
        {
            refreshHealth.IsEnabled = false; healthStatus.Text = "Lendo (somente leitura)…";
            try
            {
                IReadOnlyList<HealthCheck> checks = OperatingSystem.IsLinux() ? await GatherHealthAsync() : [];
                healthList.Children.Clear();
                foreach (var c in checks)
                    healthList.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = {
                        new TextBlock { Text = c.Ok == true ? "✓" : c.Ok == false ? "!" : "?", FontWeight = FontWeight.Bold, Width = 16, Foreground = c.Ok == false ? Palette.Warn : Palette.Muted },
                        new StackPanel { Children = { new TextBlock { Text = c.Title, FontSize = 13 }, new TextBlock { Text = c.Detail, FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 } } } } });
                healthStatus.Text = $"Lido às {DateTime.Now:HH:mm:ss}. \"?\" = fonte indisponível, não é sinal de problema.";
            }
            catch (OperationCanceledException) { }
            finally { refreshHealth.IsEnabled = true; }
        }
        void RefreshEvents()
        {
            eventsList.Children.Clear();
            var items = sessionEvents.Select(e => (e.At, e.Title, e.Text)).Concat(IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).BenchRuns.Take(8).Select(r => (r.At, r.Kind, r.Summary))).OrderByDescending(x => x.Item1).Take(8).ToList();
            if (items.Count == 0) eventsList.Children.Add(new TextBlock { Text = "Nenhum evento ainda. Resultados de benchmark e dispositivos conectados aparecem aqui.", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            foreach (var (at, title, text) in items)
                eventsList.Children.Add(new StackPanel { Children = { new TextBlock { Text = $"{at.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} · {title}", FontSize = 12, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = text, FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap } } });
        }
        void RefreshRings()
        {
            rings.Children.Clear();
            void Ring(string title, double fraction, string text) => rings.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 8), Spacing = 12, Children = {
                new Grid { Children = { new RingGauge(fraction), new TextBlock { Text = (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%", FontFamily = new FontFamily("monospace"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } } },
                new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Children = { Label(title), new TextBlock { Text = text, FontSize = 12, Foreground = Palette.Muted } } } } });
            if (usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l) Ring("MEMÓRIA", (double)u / l.MemoryTotal!.Value, usage.MemoryLine);
            if (OperatingSystem.IsLinux())
                try { foreach (var d in MountedDrives.Read(File.ReadAllText("/proc/self/mounts")).Take(4)) Ring("DISCO · " + d.Title, d.UsedFraction, d.Sizes); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        overviewRefresh += () => { RefreshEvents(); RefreshRings(); };
        overviewRefresh += () => { if (healthList.Children.Count == 0) _ = RefreshHealthAsync(); };
        var lower = new Grid { ColumnDefinitions = new("*,16,*"), Margin = new Thickness(0, 16, 0, 0) };
        var healthCard = CardBox(new StackPanel { Spacing = 10, Children = { Label("SAÚDE DO PC"), healthList, healthStatus, refreshHealth } }, new Thickness(22, 18));
        var eventsCard = CardBox(new StackPanel { Spacing = 10, Children = { Label("EVENTOS RECENTES"), eventsList } }, new Thickness(22, 18));
        Grid.SetColumn(eventsCard, 2); lower.Children.Add(healthCard); lower.Children.Add(eventsCard);
        var ringCard = CardBox(new StackPanel { Spacing = 8, Children = { Label("OCUPAÇÃO"), rings } }, new Thickness(22, 18)); ringCard.Margin = new Thickness(0, 16, 0, 0);
        var content = new StackPanel { Margin = new Thickness(0, 0, 8, 12), Children = { row1, table, lower, ringCard } };
        var scroll = Scroll("OverviewScroll", content);
        scroll.AttachedToVisualTree += (_, _) => overviewRefresh?.Invoke();
        var refreshTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        refreshTimer.Tick += (_, _) => { if (scroll.IsEffectivelyVisible) { RefreshScores(); RefreshEvents(); RefreshRings(); } };
        refreshTimer.Start(); stop.Token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(refreshTimer.Stop));
        // GPU load line: read-only telemetry every 2 s, only while the Overview is on screen (nvidia-smi is spawned only for NVIDIA cards).
        var gpuTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; bool gpuBusy = false; GpuRaw? gpuRaw = null; bool gpuResolved = false;
        gpuTimer.Tick += async (_, _) =>
        {
            if (gpuBusy || !scroll.IsEffectivelyVisible || !OperatingSystem.IsLinux()) return;
            gpuBusy = true;
            try
            {
                if (!gpuResolved) { gpuRaw = await Task.Run(() => new LinuxGpuCollector().Collect(PciIds.Load()).FirstOrDefault().Raw); gpuResolved = true; }
                var t = gpuRaw is null ? null : await new LinuxGpuCollector().TelemetryAsync(gpuRaw, stop.Token);
                if (t?.LoadPercent is double load) { gpuLoad.Add(load); if (gpuLoad.Count > UsageViewModel.MaxHistory) gpuLoad.RemoveAt(0); gpuValue.Text = load.ToString("0.0", CultureInfo.InvariantCulture) + "%"; gpuLegend.Text = "GPU"; Refresh(); }
            }
            catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }
            finally { gpuBusy = false; }
        };
        gpuTimer.Start(); stop.Token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(gpuTimer.Stop));
        return scroll;
    }

    private event Action? overviewRefresh;
    private readonly List<(DateTimeOffset At, string Title, string Text)> sessionEvents = [];

    /// <summary>Read-only inputs of the health checklist: sensors, kernel log, disk verdicts, power profile, GameMode.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task<IReadOnlyList<HealthCheck>> GatherHealthAsync()
    {
        var errors = await new LinuxKernelLog().ErrorsSinceAsync(DateTimeOffset.UtcNow.AddDays(-7), stop.Token);
        int good = 0, attention = 0, unknown = 0;
        try
        {
            string mounts = File.ReadAllText("/proc/self/mounts");
            foreach (var card in await Task.Run(() => new LinuxDiskHealthReader().Read(mounts, null).Select(DiskHealthEvaluator.Evaluate).ToList(), stop.Token))
                if (card.Verdict == HealthVerdict.Good) good++; else if (card.Verdict is HealthVerdict.Warning or HealthVerdict.Critical) attention++; else unknown++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        string? plan = await LinuxPowerReader.ActiveProfileAsync(stop.Token);
        bool onBattery = false;
        try { onBattery = new LinuxPowerReader().Read() is { MainsOnline: false, Batteries.Count: > 0 }; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return HealthChecklist.Build(new(usage.Last is null ? null : usage.Last.CpuTempC is not null, errors, good, attention, unknown, plan, onBattery, BoundedProcess.Find("gamemoderun") is not null));
    }

    private static void AutomationPropertiesName(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);

    private Control SystemPage() => new SystemInfoPage(hardware, Destination("hardware"), stop.Token) { Margin = new Thickness(0, 4, 0, 0) };

    private Control GpuPage() => new GpuInfoPage(stop.Token) { Margin = new Thickness(0, 4, 0, 0) };

    private Control DisksPage() => Scroll("DisksScroll", new DiskHealthPage(smartImport, Destination("smart-import"), stop.Token) { Margin = new Thickness(0, 4, 8, 12) });

    private Control EnergyPage() => Scroll("EnergyScroll", new Ilmenyx.Desktop.EnergyPage(sensors, stop.Token) { Margin = new Thickness(0, 4, 8, 12) });

    private NetworkPage? networkPage;

    private Control NetPage() => Scroll("NetScroll", networkPage = new NetworkPage(new SystemNetworkProbe(), usage, () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) });

    private Control ValidatePage()
    {
        cpuPanel = new CpuValidationPanel(cpu, Destination("cpu"), stop.Token);
        return Scroll("ValidateScroll", new StackPanel
        {
            Spacing = 12,
            Children = { cpuPanel, new MemoryValidationPanel(memory, Destination("memory"), stop.Token), new GameSimValidationPanel(simulation, Destination("sim"), stop.Token) }
        });
    }

    private Control ToolsPage()
    {
        var requirements = new Button { Name = "ShowRequirements", Content = "Requisitos" };
        var about = new Button { Name = "About", Content = "Licenças e créditos" };
        var classicButton = new Button { Name = "OpenClassic", Content = "Painel clássico de diagnósticos" };
        requirements.Click += async (_, _) => { await new RequirementsDialog().ShowDialog(this); if (IsVisible) requirements.Focus(); };
        about.Click += async (_, _) => { await new AboutDialog().ShowDialog(this); if (IsVisible) about.Focus(); };
        classicButton.Click += (_, _) => OpenClassicDiagnostics();
        var toastToggle = new CheckBox { Name = "DeviceToastsToggle", Content = "Avisar quando um pendrive/unidade ou dispositivo USB for conectado ou removido", IsChecked = appSettings.DeviceToasts };
        toastToggle.IsCheckedChanged += (_, _) =>
        {
            appSettings.DeviceToasts = toastToggle.IsChecked == true; if (toasts is not null) toasts.Enabled = appSettings.DeviceToasts;
            try { appSettings.Save(settingsPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
        return Scroll("ToolsScroll", new StackPanel
        {
            Spacing = 12,
            Children = { CardBox(new StackPanel { Spacing = 12, Children = { Label("FERRAMENTAS"), new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Children = { requirements, about, classicButton } }, toastToggle,
                Wrapped("ToolsNote", ProductIdentity.Attribution, 12, Palette.Faint) } }) }
        });
    }
}
