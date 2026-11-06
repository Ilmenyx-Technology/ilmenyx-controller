// New Ilmenyx additions: see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Gpu.Vulkan;
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
        pages["compat"] = ("Compatibilidade", () =>
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var steam = new SteamInfo(home);
            var services = new CompatServices(new CompatDatabase(CompatPaths.Folder()), new CompatOnline(CompatPaths.Folder()), () => LinuxCompatHardware.Detect(),
                ct => Task.Run<IReadOnlyList<LibGame>>(() => new LinuxGameLibrary(home).Steam().ToList(), ct), steam.GetLaunchOptions, steam.SetLaunchOptions,
                (url, ct) => OperatingSystem.IsLinux() ? DriverTools.OpenUrlAsync(url, ct) : Task.CompletedTask);
            return Scroll("CompatScroll", new CompatPage(services, () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) });
        }, null);
        pages["fps"] = ("Estimativa de FPS", () =>
        {
            string cache = Path.Combine(Path.GetDirectoryName(IlmenyxSettings.DefaultPath())!, "fps-cache.json");
            return new GamesFpsPage(new HowManyFpsClient(cache), IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()), IlmenyxSettings.DefaultPath(), () => this,
                () => (usage.Last?.CpuModel ?? "", new LinuxGpuCollector().Collect(PciIds.Load()).FirstOrDefault().Raw?.Name ?? ""), stop.Token) { Margin = new Thickness(0, 4, 8, 12) };
        }, null);
        pages["bottleneck"] = ("Detector de gargalo", () => Scroll("BottleneckScroll", new BottleneckPage(usage, stop.Token, OperatingSystem.IsLinux() ? () => MangoHudLogReader.Read(MangoHudLogReader.DefaultFolder(), DateTimeOffset.UtcNow) : null) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["stress"] = ("Stress test", () => Scroll("StressScroll", stressPage = new StressPage(new StressTester(), usage, () => this, () => usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l ? l.MemoryTotal!.Value - u : 0, stop.Token, OperatingSystem.IsLinux() ? new VulkanGpuStress() : null, OperatingSystem.IsLinux() ? GpuTemperatureCache() : null) { Margin = new Thickness(0, 4, 8, 12) }), null);
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
        pages["blocked"] = ("Bloqueados (!)", () => Scroll("BlockedScroll", new BlockedPage(ReadMachineProbe) { Margin = new Thickness(0, 4, 8, 12) }), null);
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

    /// <summary>GPU temperature for the stress guard: latest read-only telemetry, refreshed in the background at most every 2 s.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private Func<double?> GpuTemperatureCache()
    {
        double? last = null; DateTime at = DateTime.MinValue; bool busy = false; GpuRaw? raw = null; bool resolved = false;
        return () =>
        {
            if (!busy && DateTime.Now - at > TimeSpan.FromSeconds(2))
            {
                busy = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var collector = new LinuxGpuCollector();
                        if (!resolved) { raw = collector.Collect(PciIds.Load()).FirstOrDefault().Raw; resolved = true; }
                        last = raw is null ? null : (await collector.TelemetryAsync(raw, stop.Token)).TempC;
                    }
                    catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { last = null; }
                    finally { at = DateTime.Now; busy = false; }
                });
            }
            return last;
        };
    }

    /// <summary>Passive look at this machine (sysfs and the Vulkan adapter list); nothing is tested or started.</summary>
    private static MachineProbe ReadMachineProbe()
    {
        if (!OperatingSystem.IsLinux()) return new(false, false, false, false, false);
        bool rt = false;
        try { rt = Ilmenyx.Gpu.Vulkan.VulkanDevices.List().Adapters.Any(a => !a.IsSoftware && a.SupportsRayTracingPipeline); } catch (Exception e) when (e is IOException or InvalidOperationException or DllNotFoundException) { }
        return MachineProbeReader.Read(rt, MachineProbeReader.HasGameLibrary());
    }

    /// <summary>Menu label shows how many blocked items this machine can now finish, read in the background after the window opens.</summary>
    private async Task RefreshBlockedBadgeAsync()
    {
        try
        {
            var probe = await Task.Run(ReadMachineProbe);
            int n = BlockedCatalog.AvailableCount(probe);
            if (nav.TryGetValue("blocked", out var b)) { SetNavLabel("blocked", n > 0 ? $"Bloqueados (!) · {n} p/ testar" : "Bloqueados (!)"); Avalonia.Automation.AutomationProperties.SetName(b, n > 0 ? $"Bloqueados, {n} teste(s) disponível(is) nesta máquina" : "Bloqueados"); }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException) { }
    }

    private LightingServices RealLightingServices() => new(OpenRgbClient.ConnectAsync, () => null,
        (url, ct) => OperatingSystem.IsLinux() ? DriverTools.OpenUrlAsync(url, ct) : Task.CompletedTask, () => OperatingSystem.IsLinux() && BoundedProcess.Find("openrgb") is not null,
        OperatingSystem.IsLinux() && BoundedProcess.Find("openrgb") is not null ? async (port, ct) => await OpenRgbServer.StartAsync(port, TimeSpan.FromSeconds(60), ct) : null);

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
        var kernel = new LinuxKernelLog(); var gpu = new Ilmenyx.Gpu.Vulkan.VulkanGpuStress();
        return new OcServices(new StressTester(), () => usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l ? l.MemoryTotal!.Value - u : LinuxMemoryInfo.AvailableBytes(),
            async ct => (await new Ilmenyx.Core.Benchmarks.CpuBenchmarkRunner().RunAsync(new Ilmenyx.Contracts.CpuBenchmarkOptions { Profile = Ilmenyx.Contracts.CpuWorkloadProfile.Full, Threads = Environment.ProcessorCount, AllowCpuLoad = true, Timeout = TimeSpan.FromMinutes(5) }, ct)).Result.ExperimentalScore,
            (since, ct) => kernel.ErrorsSinceAsync(since, ct), () => LinuxKernelLog.Board(), LinuxKernelLog.NvidiaStateAsync, () => usage.Last?.CpuModel ?? "",
            gpu, async ct => (await new Ilmenyx.Gpu.Vulkan.GpuScene().RunAsync(new Ilmenyx.Gpu.Vulkan.GpuSceneOptions(0, 1920, 1080, TimeSpan.FromSeconds(30), 4096, 40_000, true), ct)).Score, GpuTemperatureCache());
    }

    private static Border CardBox(Control child, Thickness? padding = null) => new()
    {
        Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = padding ?? new Thickness(22, 18), Child = child
    };
    private static TextBlock Label(string text) => UiKit.Label(text);
    private static TextBlock Wrapped(string name, string text, double size = 14, IBrush? brush = null)
    {
        var t = new TextBlock { Name = name, Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (brush is not null) t.Foreground = brush;
        return t;
    }
    private static ScrollViewer Scroll(string name, Control content) => new() { Name = name, Content = content };
    private static Button Primary(string name, string text) { var b = UiKit.Primary(name, text, 20, 10); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Center; return b; }

    private static void ChipStyle(Button b, bool on) => UiKit.ChipStyle(b, on);
    private static StackPanel Chips<T>(string name, (string Label, T Value)[] options, T initial, Action<T> chosen)
    {
        var panel = new StackPanel { Name = name, Orientation = Orientation.Horizontal, Spacing = 6 };
        var buttons = new List<Button>();
        foreach (var (label, value) in options)
        {
            var b = new Button { Content = label, FontSize = 11, Padding = new Thickness(13, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
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
        var hero = new TextBlock { Name = "OverviewValue", Text = "—", FontSize = 34, FontWeight = FontWeight.Bold };
        GpuRaw? gpuRaw = null; GpuTelemetry? gpuLast = null;
        var gpuLoad = new List<double>(); var gpuValue = new TextBlock { Name = "OverviewGpuValue", Text = "—", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right };
        var gpuLegend = new TextBlock { Name = "OverviewLegendGpu", Text = "GPU: sem contadores de uso neste monitor", FontSize = 11, Foreground = Palette.Muted };
        Button? powerChip = null;
        void Refresh()
        {
            bool temp = metric == "temp", watts = metric == "power";
            if (powerChip is { IsEnabled: true }) ChipStyle(powerChip, watts);
            chart.SetValues(temp ? usage.TempSeries : watts ? usage.PowerSeries : usage.CpuSeries, temp || watts ? null : 100, range);
            chart.SetSecond(!temp && !watts && gpuLoad.Count > 0 ? gpuLoad : []);
            chartLabel.Text = temp ? "CPU · TEMPERATURA" : watts ? "CPU · CONSUMO (RAPL)" : "CPU · CARGA";
            hero.Text = temp ? (usage.Last?.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—")
                : watts ? (usage.Last?.CpuPowerW is { } w ? w.ToString("0.0", CultureInfo.InvariantCulture) + " W" : "—") : UsageViewModel.Pct(usage.Last?.CpuPercent);
        }
        usage.Updated += Refresh;
        var ranges = Chips("DashRange", [("1 MIN", 60), ("5 MIN", 300), ("15 MIN", 900)], 300, v => { range = v; Refresh(); });
        var metrics = Chips("DashMetric", [("Temperatura", "temp"), ("Carga", "load")], "load", v => { metric = v; Refresh(); });
        // CPU power (RAPL): enabled once a sample arrives; otherwise the tooltip says why (absent counter or root-only file).
        var power = new Button { Name = "DashMetricPower", Content = "Consumo", FontSize = 11, Padding = new Thickness(13, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), IsEnabled = false, BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
        ToolTip.SetTip(power, "Indisponível: o kernel não deixa ler o consumo da CPU (RAPL) sem permissão. Veja \"Por que não aparece?\" no menu lateral.");
        power.Click += (_, _) => { metric = "power"; foreach (var c in metrics.Children.OfType<Button>()) ChipStyle(c, ReferenceEquals(c, power)); Refresh(); };
        usage.Updated += () => { if (!power.IsEnabled && usage.Last?.CpuPowerW is not null) { power.IsEnabled = true; ToolTip.SetTip(power, "Consumo do pacote da CPU (RAPL), em watts."); ChipStyle(power, false); } };
        powerChip = power; metrics.Children.Add(power);
        var chips = new Grid { Margin = new Thickness(0, 16, 0, 8) };
        metrics.HorizontalAlignment = HorizontalAlignment.Right; chips.Children.Add(ranges); chips.Children.Add(metrics);
        var gpuTag = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Children = { Label("GPU"), gpuValue } };
        var top = new Grid { Children = { new StackPanel { Children = { chartLabel, hero } }, gpuTag } };
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Spacing = 6, Children = { new Border { Width = 14, Height = 2, Background = Palette.Accent, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "CPU", FontSize = 11, Foreground = Palette.Muted }, new Border { Width = 14, Height = 2, Background = Palette.Warn, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }, gpuLegend } };
        var chartCard = CardBox(new StackPanel { Children = { top, chips, chart, legend } }, new Thickness(22, 18));

        // Featured row (reference composition): a cinematic hero of this PC and, beside it, the performance highlight card with two small tiles.
        var open = UiKit.Translucent("OpenBench", "ABRIR BENCHMARK");
        open.Click += (_, _) => Select("bench");
        var openValidations = new Button { Name = "OpenValidations", Content = "Validações reduzidas", Background = Brushes.Transparent, Foreground = Palette.Fg, FontSize = 12, Padding = new Thickness(10, 8) };
        openValidations.Click += (_, _) => Select("validate");
        var notice = new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(11, 4), Child = new TextBlock { Name = "OverviewGpuNotice", Text = ValidationNotices.AmdNvidia, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap, FontSize = 11, FontWeight = FontWeight.Medium } };
        var memoryRow = Wrapped("OverviewMemory", "—", 12.5, Palette.Fg); var procRow = Wrapped("OverviewProcesses", "—", 12.5, Palette.Fg);
        var cpuName = new TextBlock { Name = "OverviewCpuName", Text = "—", FontSize = 13, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        usage.Updated += () => { memoryRow.Text = usage.MemoryLine; procRow.Text = usage.ProcessSummary; cpuName.Text = usage.CpuModel; };
        var score = new TextBlock { Name = "OverviewScore", Text = "—", FontSize = 30, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 2, 0, 0) };
        var scoreDelta = new TextBlock { Name = "OverviewScoreDelta", FontSize = 12.5, Foreground = Palette.Fg, Opacity = 0.85 };
        var scoreSub = Wrapped("OverviewScoreSub", "rode o Benchmark para ter uma nota", 11, Palette.Fg); scoreSub.Opacity = 0.7;
        var scoreRows = new StackPanel { Name = "OverviewScores", Margin = new Thickness(0, 8, 0, 0), Spacing = 2 };
        void RefreshScores()
        {
            var summary = PerformanceSummary.From(IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).BenchRuns);
            score.Text = summary.CpuScore is double sc ? sc.ToString("N0", CultureInfo.InvariantCulture) : "—";
            scoreDelta.Text = summary.CpuDelta is double d ? (d >= 0 ? "▲ " : "▼ ") + Math.Abs(d).ToString("N0", CultureInfo.InvariantCulture) : "";
            scoreSub.Text = summary.CpuSubtitle + (summary.CpuScore is null ? "" : " · experimental, não comparável ao Windows");
            scoreRows.Children.Clear();
            foreach (var kv in summary.Others)
                scoreRows.Children.Add(new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = kv.Key, FontSize = 11.5, Foreground = Palette.Fg, Opacity = 0.75 }, new TextBlock { Text = kv.Value, FontSize = 11.5, FontFamily = IlmenyxTheme.Mono, [Grid.ColumnProperty] = 1 } } });
        }
        overviewRefresh += RefreshScores; RefreshScores();

        string model = "Este PC", distro = LinuxCompatHardware.OsName() ?? "Linux", kernel = "";
        try
        {
            if (File.Exists("/sys/class/dmi/id/product_name") && File.ReadAllText("/sys/class/dmi/id/product_name").Trim() is { Length: > 0 and < 80 } pm) model = pm.Split('_')[0];
            kernel = File.Exists("/proc/sys/kernel/osrelease") ? File.ReadAllText("/proc/sys/kernel/osrelease").Trim() : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var heroTags = new WrapPanel { Name = "OverviewHeroTags" };
        void HeroTags()
        {
            heroTags.Children.Clear();
            foreach (var t in new[] { gpuRaw?.Name is { } gn ? "GPU · " + gn : null, usage.MemorySub is { Length: > 0 } ms ? ms.Replace("Total", "RAM") : null, distro, kernel.Length > 0 ? "kernel " + kernel.Split('-')[0] : null })
                if (t is not null) heroTags.Children.Add(UiKit.Tag(t, onImage: true));
        }
        HeroTags();
        TextBlock Reading() => new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var heroCpu = Reading(); var heroGpu = Reading(); var heroRam = Reading();
        Border ReadingPill(string label, TextBlock value) => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x0E, 0x14)), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(12, 5), Margin = new Thickness(0, 0, 8, 6),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center }, value } },
        };
        void HeroReadings()
        {
            var u = usage.Last;
            heroCpu.Text = (u?.CpuTempC is { } t ? t.ToString("0", CultureInfo.InvariantCulture) + " °C · " : "") + UsageViewModel.Pct(u?.CpuPercent);
            heroGpu.Text = (gpuLast?.TempC is { } gt ? gt.ToString("0", CultureInfo.InvariantCulture) + " °C · " : "") + UsageViewModel.Pct(gpuLast?.LoadPercent);
            heroRam.Text = u is { MemoryTotal: > 0, MemoryUsed: { } mu } ? UsageViewModel.Pct(100.0 * mu / u.MemoryTotal.Value) : "—";
        }
        usage.Updated += HeroReadings;
        // Hero light: a soft accent glow on the right instead of a picture (this page has no imagery of its own).
        var accentColor = ((ISolidColorBrush)Palette.Accent).Color;
        var heroArt = new Border
        {
            Margin = new Thickness(-26, -22), IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.82, 0.35, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.82, 0.35, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.45, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0x55, accentColor.R, accentColor.G, accentColor.B), 0), new GradientStop(Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B), 1) },
            },
            Child = new Image { Source = AppIcon.Symbol, Width = 120, Height = 120, Opacity = 0.18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 34, 56, 0) },
        };
        var heroContent = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), Children = { heroArt } };
        Grid.SetRowSpan(heroArt, 4);
        heroContent.Children.Add(heroTags);
        var heroTitle = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 14), Children = {
            new TextBlock { Name = "OverviewMachine", Text = model, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap }, cpuName } };
        cpuName.Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        Grid.SetRow(heroTitle, 1); heroContent.Children.Add(heroTitle);
        var readings = new WrapPanel { Children = { ReadingPill("CPU", heroCpu), ReadingPill("GPU", heroGpu), ReadingPill("RAM", heroRam) } };
        Grid.SetRow(readings, 2); heroContent.Children.Add(readings);
        Grid.SetRow(notice, 3); notice.Margin = new Thickness(0, 4, 0, 0); heroContent.Children.Add(notice);
        var heroCard = UiKit.Hero(heroContent, height: double.NaN, name: "OverviewHero"); heroCard.MinHeight = 250; heroCard.VerticalAlignment = VerticalAlignment.Stretch;

        var feature = UiKit.FeatureCard(new StackPanel { Children = {
            new TextBlock { Text = "DESEMPENHO", FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, Foreground = Palette.Fg, Opacity = 0.75 }, score, scoreDelta, scoreSub, scoreRows,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 14, 0, 0), Children = { open, openValidations } } } }, "OverviewPerformance");
        Border SmallTile(string label, string icon, TextBlock value) => UiKit.Card(new StackPanel { Spacing = 6, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { UiKit.Icon(icon, 14, Palette.Faint), UiKit.Label(label) } }, value } }, new Thickness(0));
        var tiles = new Grid { ColumnDefinitions = new("*,10,*"), Margin = new Thickness(0, 10, 0, 0) };
        var memTile = SmallTile("MEMÓRIA", Icons.Memory, memoryRow); var procTile = SmallTile("PROCESSOS", Icons.Processes, procRow);
        Grid.SetColumn(procTile, 2); tiles.Children.Add(memTile); tiles.Children.Add(procTile);
        var side = new StackPanel { Children = { feature, tiles } };
        var row1 = new Grid { ColumnDefinitions = new("1.9*,14,*"), Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetColumn(side, 2); row1.Children.Add(heroCard); row1.Children.Add(side);

        // Components: asymmetric grid of cards (CPU larger), built once; values update in place every sample.
        var components = new Grid { Name = "DashComponents" };
        var cards = new Dictionary<string, (TextBlock Model, TextBlock Big, TextBlock Line, TextBlock Use)>();
        Button ComponentCard(string name, string icon, IBrush tint, string target, string goName)
        {
            TextBlock T(double size, IBrush brush, FontWeight weight = FontWeight.Normal) => new() { FontSize = size, Foreground = brush, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis };
            var modelText = T(11, Palette.Faint); var big = T(24, Palette.Fg, FontWeight.SemiBold); var line = T(12, Palette.Muted); var useText = T(11.5, Palette.Faint);
            big.FontFamily = IlmenyxTheme.Mono; big.FontSize = 22; big.Text = "—"; line.Text = ""; useText.Text = "";
            cards[name] = (modelText, big, line, useText);
            var head = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Children = { UiKit.Icon(icon, 16, tint) } };
            var title = new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var chevron = UiKit.Icon(Icons.ChevronRight, 14, Palette.Faint);
            Grid.SetColumn(title, 1); Grid.SetColumn(chevron, 2); head.Children.Add(title); head.Children.Add(chevron);
            var b = new Button
            {
                Name = goName, Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(16, 14),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Content = new StackPanel { Spacing = 4, Children = { head, modelText, new Border { Height = 6 }, big, line, useText } },
            };
            AutomationPropertiesName(b, "Abrir " + name);
            b.Click += (_, _) => Select(target);
            return b;
        }
        var componentCards = new[]
        {
            ComponentCard("CPU", Icons.Cpu, Palette.DataCpu, "system", "GoCpu"), ComponentCard("GPU", Icons.Gpu, Palette.DataGpu, "gpu", "GoGpu"),
            ComponentCard("Memória", Icons.Memory, Palette.DataMemory, "system", "GoMemory"), ComponentCard("Discos", Icons.Disks, Palette.DataDisk, "disks", "GoDisk"),
            ComponentCard("Rede", Icons.Network, Palette.DataNetwork, "net", "GoNet"),
        };
        foreach (var c in componentCards) components.Children.Add(c);
        bool? wideComponents = null;
        void FitComponents(double width)
        {
            bool wide = width >= 980;
            if (wideComponents == wide) return; wideComponents = wide;
            components.ColumnDefinitions.Clear(); components.RowDefinitions.Clear();
            if (wide)
            {
                // 1.9 : 1 : 1 : 1 : 1, one row (the reference's asymmetric offers row).
                foreach (var w in new[] { 1.9, 0, 1, 0, 1, 0, 1, 0, 1 }) components.ColumnDefinitions.Add(w == 0 ? new ColumnDefinition(10, GridUnitType.Pixel) : new ColumnDefinition(w, GridUnitType.Star));
                for (int i = 0; i < componentCards.Length; i++) { Grid.SetColumn(componentCards[i], i * 2); Grid.SetRow(componentCards[i], 0); Grid.SetColumnSpan(componentCards[i], 1); }
            }
            else
            {
                // Narrow window: CPU wide + GPU + memory on top, disks and network below.
                foreach (var w in new[] { 1.6, 0, 1, 0, 1 }) components.ColumnDefinitions.Add(w == 0 ? new ColumnDefinition(10, GridUnitType.Pixel) : new ColumnDefinition(w, GridUnitType.Star));
                components.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); components.RowDefinitions.Add(new RowDefinition(10, GridUnitType.Pixel)); components.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                (int Col, int Row, int Span)[] at = [(0, 0, 1), (2, 0, 1), (4, 0, 1), (0, 2, 1), (2, 2, 3)];
                for (int i = 0; i < componentCards.Length; i++) { Grid.SetColumn(componentCards[i], at[i].Col); Grid.SetRow(componentCards[i], at[i].Row); Grid.SetColumnSpan(componentCards[i], at[i].Span); }
            }
        }
        components.SizeChanged += (_, e) => FitComponents(e.NewSize.Width);
        FitComponents(1200);
        void Set(string name, string model, string big, string line, string use)
        {
            var c = cards[name]; c.Model.Text = model; c.Big.Text = big; c.Line.Text = line; c.Use.Text = use;
        }
        void Components()
        {
            var s = usage.Last;
            string mhz = s?.CpuMhz is { } m ? m.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "—";
            string tmp = s?.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—";
            Set("CPU", s?.CpuModel ?? "", UsageViewModel.Pct(s?.CpuPercent), $"{tmp} · {mhz}", s?.CpuPowerW is { } cw ? cw.ToString("0.0", CultureInfo.InvariantCulture) + " W (RAPL)" : s?.BusiestCore is { } bc ? "mais ocupado: " + bc : "");
            // GPU card from the same read-only telemetry as the load line; each missing counter stays "—" (never zero).
            var g = gpuLast;
            string gUse = g?.PowerW is { } gw ? gw.ToString("0.0", CultureInfo.InvariantCulture) + " W" + (g.PowerIsPackage ? " (pacote CPU+GPU)" : "")
                : g?.VramUsed is { } vu && g.VramTotal is > 0 ? $"{vu >> 20:N0} / {g.VramTotal.Value >> 20:N0} MB".Replace(",", ".")
                : "sem contadores";
            Set("GPU", gpuRaw?.Name ?? "Inventário DRM/PCI", UsageViewModel.Pct(g?.LoadPercent),
                (g?.TempC is { } gt ? gt.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—") + " · " + (g?.CoreMHz is { } gm ? gm.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "—"), gUse);
            Set("Memória", usage.MemorySub, s is { MemoryTotal: > 0, MemoryUsed: { } u } ? UsageViewModel.Pct(100.0 * u / s.MemoryTotal.Value) : "—", usage.MemoryLine, "");
            Set("Discos", "Dispositivos inteiros", "E/S", usage.DiskLine, "");
            Set("Rede", usage.NetSub, "Tráfego", usage.NetLine, "");
            HeroReadings();
        }
        usage.Updated += Components; Components();
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
        var lower = new Grid { ColumnDefinitions = new("*,16,*"), Margin = new Thickness(0, 14, 0, 0) };
        var healthCard = CardBox(new StackPanel { Spacing = 10, Children = { Label("SAÚDE DO PC"), healthList, healthStatus, refreshHealth } }, new Thickness(22, 18));
        var eventsCard = CardBox(new StackPanel { Spacing = 10, Children = { Label("EVENTOS RECENTES"), eventsList } }, new Thickness(22, 18));
        Grid.SetColumn(eventsCard, 2); lower.Children.Add(healthCard); lower.Children.Add(eventsCard);
        var ringCard = CardBox(new StackPanel { Spacing = 8, Children = { Label("OCUPAÇÃO"), rings } }, new Thickness(22, 18)); ringCard.Margin = new Thickness(0, 14, 0, 0);
        var content = new StackPanel { Margin = new Thickness(0, 0, 8, 12), Children = { row1, UiKit.SectionHeader("Atividade"), chartCard, UiKit.SectionHeader("Componentes"), components, lower, ringCard } };
        var scroll = Scroll("OverviewScroll", content);
        scroll.AttachedToVisualTree += (_, _) => overviewRefresh?.Invoke();
        var refreshTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        refreshTimer.Tick += (_, _) => { if (scroll.IsEffectivelyVisible) { RefreshScores(); RefreshEvents(); RefreshRings(); } };
        refreshTimer.Start(); stop.Token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(refreshTimer.Stop));
        // GPU load line: read-only telemetry every 2 s, only while the Overview is on screen (nvidia-smi is spawned only for NVIDIA cards).
        var gpuTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; bool gpuBusy = false; bool gpuResolved = false;
        gpuTimer.Tick += async (_, _) =>
        {
            if (gpuBusy || !scroll.IsEffectivelyVisible || !OperatingSystem.IsLinux()) return;
            gpuBusy = true;
            try
            {
                if (!gpuResolved) { gpuRaw = await Task.Run(() => new LinuxGpuCollector().Collect(PciIds.Load()).FirstOrDefault().Raw); gpuResolved = true; HeroTags(); }
                var t = gpuRaw is null ? null : await new LinuxGpuCollector().TelemetryAsync(gpuRaw, stop.Token);
                if (t is not null) { gpuLast = t; Components(); }
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
            Children = { UiKit.PageHeader("Requisitos e sobre"), UiKit.Card(new StackPanel { Spacing = 12, Children = { UiKit.Label("FERRAMENTAS"), new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Children = { requirements, about, classicButton } }, toastToggle,
                Wrapped("ToolsNote", ProductIdentity.Attribution, 12, Palette.Faint) } }) }
        });
    }
}
