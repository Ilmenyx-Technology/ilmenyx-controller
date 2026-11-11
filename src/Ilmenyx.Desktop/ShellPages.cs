// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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
        pages["dash"] = (L.T("Visão geral"), Overview, null);
        pages["system"] = (L.T("Sistema"), SystemPage, null);
        pages["sensors"] = (L.T("Sensores"), () => Scroll("SensorsScroll", new SensorPanel(sensors, Destination("sensors"), stop.Token)), null);
        pages["gpu"] = ("GPU", GpuPage, null);
        pages["disks"] = (L.T("Discos"), DisksPage, null);
        pages["energy"] = (L.T("Energia"), EnergyPage, null);
        pages["net"] = (L.T("Rede"), NetPage, null);
        pages["procs"] = (L.T("Processos"), () => new ProcessTable(usage), null);
        pages["launcher"] = (L.T("Jogos"), () =>
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var gpus = new LinuxGpuCollector().Collect(PciIds.Load());
            // PRIME variables depend on the driver, not only the vendor: NVIDIA on nouveau is offloaded with DRI_PRIME like Mesa GPUs.
            string vendor = gpus.Count > 0 ? (gpus[0].Card.Vendor == "NVIDIA" && gpus[0].Raw.Driver != "nvidia" ? "NVIDIA-Mesa" : gpus[0].Card.Vendor) : "";
            bool multi = gpus.Count(x => !x.Raw.Integrated) + gpus.Count(x => x.Raw.Integrated) > 1;
            // Manual games: the experimental Proton options (when they apply) and, with the Otimiza option on, the bigger shader cache variables.
            return new LauncherPage(new LinuxGameLibrary(home), new SteamInfo(home), IlmenyxSettings.DefaultPath(),
                (g, p, v, status, learned, ct) => new LinuxGameLauncher().RunAsync(g, p, v, status, learned, ct,
                    LinuxGameExtras.ManualContext(g, p, v, multi, LinuxGameExtras.UmuProtonName(home)), new LinuxShaderCache(home).IsOn), () => this, vendor, multi, stop.Token) { Margin = new Thickness(0, 4, 8, 12) };
        }, null);
        pages["compat"] = (L.T("Compatibilidade"), () =>
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var steam = new SteamInfo(home);
            var services = new CompatServices(new CompatDatabase(CompatPaths.Folder()), new CompatOnline(CompatPaths.Folder()), () => LinuxCompatHardware.Detect(),
                ct => Task.Run<IReadOnlyList<LibGame>>(() => new LinuxGameLibrary(home).Steam().ToList(), ct), steam.GetLaunchOptions, steam.SetLaunchOptions,
                (url, ct) => OperatingSystem.IsLinux() ? DriverTools.OpenUrlAsync(url, ct) : Task.CompletedTask)
            { Profile = id => IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).GameProfiles.GetValueOrDefault("Steam:" + id) };
            return Scroll("CompatScroll", new CompatPage(services, () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) });
        }, null);
        pages["fps"] = (L.T("Estimativa de FPS"), () => new GamesFpsPage(IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()), IlmenyxSettings.DefaultPath(),
            () => (usage.Last?.CpuModel ?? "", new LinuxGpuCollector().Collect(PciIds.Load()).FirstOrDefault().Raw?.Name ?? ""), FpsContextNow,
            OperatingSystem.IsLinux() ? FpsCalibratorReal.Create(FpsContextNow, () => usage.Last?.CpuTempC, GpuTemperatureCache()) : null) { Margin = new Thickness(0, 4, 0, 0) }, null); // scrolls by itself (sticky catalogue)
        pages["bottleneck"] = (L.T("Detector de gargalo"), () => Scroll("BottleneckScroll", new BottleneckPage(usage, stop.Token, OperatingSystem.IsLinux() ? () => MangoHudLogReader.Read(MangoHudLogReader.DefaultFolder(), DateTimeOffset.UtcNow) : null) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["stress"] = ("Stress test", () => Scroll("StressScroll", stressPage = new StressPage(new StressTester(), usage, () => this, () => usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l ? l.MemoryTotal!.Value - u : 0, stop.Token, OperatingSystem.IsLinux() ? new VulkanGpuStress() : null, OperatingSystem.IsLinux() ? GpuTemperatureCache() : null) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["bench"] = ("Benchmark", () => Scroll("BenchScroll", new BenchPage(OperatingSystem.IsLinux() ? BenchServices.Real() : throw new PlatformNotSupportedException(), usage, () => this, IlmenyxSettings.DefaultPath(), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["overlay"] = ("Overlay", () => Scroll("OverlayScroll", new OverlayPage(OperatingSystem.IsLinux() ? RealOverlayServices() : throw new PlatformNotSupportedException(), usage, IlmenyxSettings.DefaultPath(), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["lighting"] = (L.T("Integração"), () => Scroll("LightingScroll", new LightingPage(RealLightingServices(), usage, () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["oc"] = ("OC", () => Scroll("OcScroll", new OcPage(OperatingSystem.IsLinux() ? RealOcServices() : throw new PlatformNotSupportedException(), usage, () => this, IlmenyxSettings.DefaultPath(), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["validate"] = (L.T("Validações reduzidas"), ValidatePage, null);
        pages["makerports"] = (L.T("Maker · Portas"), () => Scroll("MakerPortsScroll", new MakerPortsPage(stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["makerdrv"] = (L.T("Maker · Drivers seriais"), () => Scroll("MakerDrvScroll", new MakerDriversPage(stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["cleanup"] = (L.T("Liberar espaço"), () => Scroll("CleanupScroll", new CleanupPage(() => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["optimize"] = (L.T("Otimiza"), () => Scroll("OptimizeScroll", new OptimizePage(new LinuxOptimizer(), new OptimizeServices(LinuxGameLauncher.PowerProfileAsync, LinuxGameLauncher.SetPowerProfileAsync) { SteamRoot = () => new SteamInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Root() }, usage, IlmenyxSettings.DefaultPath(), () => this, stop.Token,
            OperatingSystem.IsLinux() ? new ZramServices(new ZramProbe().CollectAsync, NvidiaDriverRunner.Available ? NvidiaDriverRunner.RunAsync : null) : null) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["gamecheck"] = (L.T("Jogo não abre?"), () => Scroll("GameCheckScroll", new GameCheckPage(OperatingSystem.IsLinux() ? new GameCheckServices(new GameLaunchProbe().CollectAsync, GameUserFixRunner.RunAsync, Select) : throw new PlatformNotSupportedException(), () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["startup"] = (L.T("Inicialização"), () => Scroll("StartupScroll", new StartupPage(LinuxStartupManager.CreateDefault(), () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["apps"] = (L.T("Instalar apps"), () => Scroll("AppsScroll", new AppsPage(new LinuxAppInstaller(), LinuxAppInstaller.FlatpakAvailable, () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["driver"] = ("Drivers", () => Scroll("DriverScroll", new DriverPage(Destination("drivers"), stop.Token, openNvidiaTab: () => Select("nvdriver")) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["nvdriver"] = (L.T("Driver NVIDIA"), () => Scroll("NvDriverScroll", new NvidiaDriverPage(OperatingSystem.IsLinux() ? NvidiaDriverServices.Real() : throw new PlatformNotSupportedException(), () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["report"] = (L.T("Relatório"), () => Scroll("ReportScroll", new ReportPage(GatherReportAsync, Destination("report"), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["blocked"] = (L.T("Bloqueados (!)"), () => Scroll("BlockedScroll", new BlockedPage(ReadMachineProbe) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["tools"] = (L.T("Requisitos e sobre"), ToolsPage, null);
        pages["help"] = (help[HelpKeys.HelpNav], HelpPage, null);
    }

    /// <summary>Local facts for the FPS page alerts: the GPU games are estimated for, a second (integrated) GPU, battery state and PRIME in the game profiles. Read-only.</summary>
    private static FpsContext? FpsContextNow()
    {
        if (!OperatingSystem.IsLinux()) return null;
        var gpus = new LinuxGpuCollector().Collect(PciIds.Load());
        if (gpus.Count == 0) return null;
        var main = gpus[0].Raw;
        var power = new LinuxPowerReader().Read();
        var profiles = IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).GameProfiles.Values;
        return new FpsContext(main.Name, main.VendorId, main.DeviceId, main.Driver, main.Integrated, main.VramBytes,
            gpus.Skip(1).Select(g => g.Raw).FirstOrDefault(r => r.Integrated)?.Name, FpsAlerts.OnBattery(power.Batteries, power.MainsOnline),
            profiles.Count(p => p.GpuHigh), profiles.Count, main.DriverVersion ?? "", main.MaxCoreMHz);
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
        bool rt = false; // what is still blocked for the RT benchmark is a dedicated GPU (the APU path was validated)
        try { rt = Ilmenyx.Gpu.Vulkan.VulkanDevices.List().Adapters.Any(a => !a.IsSoftware && a.SupportsRayQuery && a.Kind == L.T("GPU dedicada")); } catch (Exception e) when (e is IOException or InvalidOperationException or DllNotFoundException) { }
        return MachineProbeReader.Read(rt, MachineProbeReader.HasGameLibrary());
    }

    /// <summary>Menu label shows how many blocked items this machine can now finish, read in the background after the window opens.</summary>
    private async Task RefreshBlockedBadgeAsync()
    {
        try
        {
            var probe = await Task.Run(ReadMachineProbe);
            int n = BlockedCatalog.AvailableCount(probe);
            if (nav.TryGetValue("blocked", out var b)) { SetNavLabel("blocked", n > 0 ? L.T($"Bloqueados (!) · {n} p/ testar") : L.T("Bloqueados (!)")); Avalonia.Automation.AutomationProperties.SetName(b, n > 0 ? L.T($"Bloqueados, {n} teste(s) disponível(is) nesta máquina") : L.T("Bloqueados")); }
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
            gpu, async ct => (await new Ilmenyx.Gpu.Vulkan.GpuScene().RunAsync(new Ilmenyx.Gpu.Vulkan.GpuSceneOptions(0, 1920, 1080, TimeSpan.FromSeconds(30), 4096, 40_000, true), ct)).Score, GpuTemperatureCache(), () => LinuxKernelLog.IsNotebook());
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
        var chartLabel = Label(L.T("CPU · CARGA"));
        var hero = new TextBlock { Name = "OverviewValue", Text = "—", FontSize = 34, FontWeight = FontWeight.Bold };
        GpuRaw? gpuRaw = null; GpuTelemetry? gpuLast = null;
        var gpuLoad = new List<double>(); var gpuValue = new TextBlock { Name = "OverviewGpuValue", Text = "—", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right };
        var gpuLegend = new TextBlock { Name = "OverviewLegendGpu", Text = L.T("GPU: sem contadores de uso neste monitor"), FontSize = 11, Foreground = Palette.Muted };
        Button? powerChip = null;
        void Refresh()
        {
            bool temp = metric == "temp", watts = metric == "power";
            if (metric is "mem" or "disk" or "net")
            {
                // Memory, disk and network history: the same 15 min the monitor keeps, now visible like on the System Monitor.
                bool mem = metric == "mem";
                chart.SetValues(mem ? usage.MemorySeries : metric == "disk" ? usage.DiskSeries : usage.NetSeries, mem ? 100 : null, range);
                chart.SetSecond([]);
                chartLabel.Text = mem ? L.T("MEMÓRIA · USO") : metric == "disk" ? L.T("DISCOS · LEITURA + ESCRITA") : L.T("REDE · RECEBIDO + ENVIADO");
                var l = usage.Last;
                hero.Text = mem ? (l is { MemoryTotal: > 0, MemoryUsed: { } mu } ? UsageViewModel.Pct(100.0 * mu / l.MemoryTotal.Value) : "—")
                    : metric == "disk" ? (l?.DiskReadBps is { } dr && l.DiskWriteBps is { } dw ? UsageViewModel.Rate(dr + dw) : "—")
                    : (l?.NetRxBps is { } nr && l.NetTxBps is { } nt ? UsageViewModel.Rate(nr + nt) : "—");
                return;
            }
            if (powerChip is { IsEnabled: true }) ChipStyle(powerChip, watts);
            chart.SetValues(temp ? usage.TempSeries : watts ? usage.PowerSeries : usage.CpuSeries, temp || watts ? null : 100, range);
            chart.SetSecond(!temp && !watts && gpuLoad.Count > 0 ? gpuLoad : []);
            chartLabel.Text = temp ? L.T("CPU · TEMPERATURA") : watts ? L.T("CPU · CONSUMO (RAPL)") : L.T("CPU · CARGA");
            hero.Text = temp ? (usage.Last?.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—")
                : watts ? (usage.Last?.CpuPowerW is { } w ? w.ToString("0.0", CultureInfo.InvariantCulture) + " W" : "—") : UsageViewModel.Pct(usage.Last?.CpuPercent);
        }
        usage.Updated += UiKit.OnScreen(chart, Refresh);
        var ranges = Chips("DashRange", [("1 min", 60), ("5 min", 300), ("15 min", 900)], 300, v => { range = v; Refresh(); });
        var metrics = Chips("DashMetric", [(L.T("Temperatura"), "temp"), (L.T("Carga"), "load"), (L.T("Memória"), "mem"), (L.T("Disco"), "disk"), (L.T("Rede"), "net")], "load", v => { metric = v; Refresh(); });
        // CPU power (RAPL): enabled once a sample arrives; otherwise the tooltip says why (absent counter or root-only file).
        var power = new Button { Name = "DashMetricPower", Content = L.T("Consumo"), FontSize = 11, Padding = new Thickness(13, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), IsEnabled = false, BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
        ToolTip.SetTip(power, L.T("Indisponível: o kernel não deixa ler o consumo da CPU (RAPL) sem permissão. Veja \"Por que não aparece?\" no menu lateral."));
        power.Click += (_, _) => { metric = "power"; foreach (var c in metrics.Children.OfType<Button>()) ChipStyle(c, ReferenceEquals(c, power)); Refresh(); };
        usage.Updated += () => { if (!power.IsEnabled && usage.Last?.CpuPowerW is not null) { power.IsEnabled = true; ToolTip.SetTip(power, L.T("Consumo do pacote da CPU (RAPL), em watts.")); ChipStyle(power, false); } };
        powerChip = power; metrics.Children.Add(power);
        var chips = new Grid { Margin = new Thickness(0, 16, 0, 8) };
        metrics.HorizontalAlignment = HorizontalAlignment.Right; chips.Children.Add(ranges); chips.Children.Add(metrics);
        var gpuTag = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Children = { Label("GPU"), gpuValue } };
        var top = new Grid { Children = { new StackPanel { Children = { chartLabel, hero } }, gpuTag } };
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Spacing = 6, Children = { new Border { Width = 14, Height = 2, Background = Palette.Accent, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "CPU", FontSize = 11, Foreground = Palette.Muted }, new Border { Width = 14, Height = 2, Background = Palette.Warn, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }, gpuLegend } };
        var chartCard = CardBox(new StackPanel { Children = { top, chips, chart, legend } }, new Thickness(22, 18));

        // Featured row (reference composition): a cinematic hero of this PC and, beside it, the performance highlight card with two small tiles.
        var open = UiKit.Translucent("OpenBench", L.T("Abrir Benchmark"));
        open.Click += (_, _) => Select("bench");
        var openValidations = new Button { Name = "OpenValidations", Content = L.T("Validações reduzidas"), Background = Brushes.Transparent, Foreground = Palette.Fg, FontSize = 12, Padding = new Thickness(10, 8) };
        openValidations.Click += (_, _) => Select("validate");
        string? gpuNotice = ValidationNotices.For(DrmGpuInfoProvider.Vendors());
        var notice = new Border { IsVisible = gpuNotice is not null, HorizontalAlignment = HorizontalAlignment.Left, Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(11, 4), Child = new TextBlock { Name = "OverviewGpuNotice", Text = gpuNotice ?? "", Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap, FontSize = 11, FontWeight = FontWeight.Medium } };
        var memoryRow = Wrapped("OverviewMemory", "—", 12.5, Palette.Fg); var procRow = Wrapped("OverviewProcesses", "—", 12.5, Palette.Fg);
        var cpuName = new TextBlock { Name = "OverviewCpuName", Text = "—", FontSize = 13, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        usage.Updated += UiKit.OnScreen(memoryRow, () => { memoryRow.Text = usage.MemoryLine; procRow.Text = usage.ProcessSummary; cpuName.Text = usage.CpuModel; });
        var score = new TextBlock { Name = "OverviewScore", Text = "—", FontSize = 30, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 2, 0, 0) };
        var scoreDelta = new TextBlock { Name = "OverviewScoreDelta", FontSize = 12.5, Foreground = Palette.Fg, Opacity = 0.85 };
        var scoreSub = Wrapped("OverviewScoreSub", L.T("rode o Benchmark para ter uma nota"), 11, Palette.Fg); scoreSub.Opacity = 0.9;
        var scoreRows = new StackPanel { Name = "OverviewScores", Margin = new Thickness(0, 8, 0, 0), Spacing = 2 };
        void RefreshScores()
        {
            var summary = PerformanceSummary.From(IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).BenchRuns);
            score.Text = summary.CpuScore is double sc ? sc.ToString("N0", CultureInfo.InvariantCulture) : "—";
            scoreDelta.Text = summary.CpuDelta is double d ? (d >= 0 ? "▲ " : "▼ ") + Math.Abs(d).ToString("N0", CultureInfo.InvariantCulture) : "";
            scoreSub.Text = summary.CpuSubtitle + (summary.CpuScore is null ? "" : L.T(" · experimental, não comparável ao Windows"));
            scoreRows.Children.Clear();
            foreach (var kv in summary.Others)
                scoreRows.Children.Add(new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = kv.Key, FontSize = 11.5, Foreground = Palette.Fg, Opacity = 0.75 }, new TextBlock { Text = kv.Value, FontSize = 11.5, FontFamily = IlmenyxTheme.Mono, [Grid.ColumnProperty] = 1 } } });
        }
        overviewRefresh += RefreshScores; RefreshScores();

        string model = L.T("Este PC"), distro = LinuxCompatHardware.OsName() ?? "Linux", kernel = "";
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
            foreach (var t in new[] { gpuRaw?.Name is { } gn ? "GPU · " + GameCatalog.ShortGpuName(MarketingName(gn)) : null, usage.MemorySub is { Length: > 0 } ms ? ms.Replace("Total", "RAM") : null, distro, kernel.Length > 0 ? "kernel " + kernel.Split('-')[0] : null })
                if (t is not null) heroTags.Children.Add(UiKit.Tag(t, Palette.Fg));
        }
        HeroTags();
        TextBlock Reading() => new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.Fg, VerticalAlignment = VerticalAlignment.Center };
        var heroCpu = Reading(); var heroGpu = Reading(); var heroRam = Reading();
        Border ReadingPill(string label, TextBlock value) => new()
        {
            Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(12, 5), Margin = new Thickness(0, 0, 8, 6),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = label, FontSize = 11, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center }, value } },
        };
        void HeroReadings()
        {
            var u = usage.Last;
            heroCpu.Text = (u?.CpuTempC is { } t ? t.ToString("0", CultureInfo.InvariantCulture) + " °C · " : "") + UsageViewModel.Pct(u?.CpuPercent);
            heroGpu.Text = (gpuLast?.TempC is { } gt ? gt.ToString("0", CultureInfo.InvariantCulture) + " °C · " : "") + UsageViewModel.Pct(gpuLast?.LoadPercent);
            heroRam.Text = u is { MemoryTotal: > 0, MemoryUsed: { } mu } ? UsageViewModel.Pct(100.0 * mu / u.MemoryTotal.Value) : "—";
        }
        usage.Updated += UiKit.OnScreen(chart, HeroReadings);
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
            new TextBlock { Name = "OverviewMachine", Text = model, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Palette.Fg, TextWrapping = TextWrapping.Wrap }, cpuName } };
        Grid.SetRow(heroTitle, 1); heroContent.Children.Add(heroTitle);
        var readings = new WrapPanel { Children = { ReadingPill("CPU", heroCpu), ReadingPill("GPU", heroGpu), ReadingPill("RAM", heroRam) } };
        Grid.SetRow(readings, 2); heroContent.Children.Add(readings);
        Grid.SetRow(notice, 3); notice.Margin = new Thickness(0, 4, 0, 0); heroContent.Children.Add(notice);
        var heroCard = UiKit.Hero(heroContent, height: double.NaN, name: "OverviewHero"); heroCard.MinHeight = 250; heroCard.VerticalAlignment = VerticalAlignment.Stretch;

        var feature = UiKit.FeatureCard(new StackPanel { Children = {
            new TextBlock { Text = L.T("DESEMPENHO"), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, Foreground = Palette.Fg, Opacity = 0.75 }, score, scoreDelta, scoreSub, scoreRows,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 14, 0, 0), Children = { open, openValidations } } } }, "OverviewPerformance");
        Border SmallTile(string label, string icon, TextBlock value) => UiKit.Card(new StackPanel { Spacing = 6, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { UiKit.Icon(icon, 14, Palette.Faint), UiKit.Label(label) } }, value } }, new Thickness(0));
        var tiles = new Grid { ColumnDefinitions = new("*,10,*"), Margin = new Thickness(0, 10, 0, 0) };
        var memTile = SmallTile(L.T("MEMÓRIA"), Icons.Memory, memoryRow); var procTile = SmallTile(L.T("PROCESSOS"), Icons.Processes, procRow);
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
            AutomationPropertiesName(b, L.T("Abrir ") + name);
            b.Click += (_, _) => Select(target);
            return b;
        }
        var componentCards = new[]
        {
            ComponentCard("CPU", Icons.Cpu, Palette.DataCpu, "system", "GoCpu"), ComponentCard("GPU", Icons.Gpu, Palette.DataGpu, "gpu", "GoGpu"),
            ComponentCard(L.T("Memória"), Icons.Memory, Palette.DataMemory, "system", "GoMemory"), ComponentCard(L.T("Discos"), Icons.Disks, Palette.DataDisk, "disks", "GoDisk"),
            ComponentCard(L.T("Rede"), Icons.Network, Palette.DataNetwork, "net", "GoNet"),
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
            Set("CPU", s?.CpuModel ?? "", UsageViewModel.Pct(s?.CpuPercent), $"{tmp} · {mhz}", s?.CpuPowerW is { } cw ? cw.ToString("0.0", CultureInfo.InvariantCulture) + " W (RAPL)" : s?.BusiestCore is { } bc ? L.T("mais ocupado: ") + bc : "");
            // GPU card from the same read-only telemetry as the load line; each missing counter stays "—" (never zero).
            var g = gpuLast;
            string gUse = g?.PowerW is { } gw ? gw.ToString("0.0", CultureInfo.InvariantCulture) + " W" + (g.PowerIsPackage ? L.T(" (pacote CPU+GPU)") : "")
                : g?.VramUsed is { } vu && g.VramTotal is > 0 ? $"{vu >> 20:N0} / {g.VramTotal.Value >> 20:N0} MB".Replace(",", ".")
                : L.T("sem contadores");
            Set("GPU", gpuRaw?.Name is { } rawName ? MarketingName(rawName) : L.T("Inventário DRM/PCI"), UsageViewModel.Pct(g?.LoadPercent),
                (g?.TempC is { } gt ? gt.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—") + " · " + (g?.CoreMHz is { } gm ? gm.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "—"), gUse);
            Set(L.T("Memória"), usage.MemorySub, s is { MemoryTotal: > 0, MemoryUsed: { } u } ? UsageViewModel.Pct(100.0 * u / s.MemoryTotal.Value) : "—", usage.MemoryLine, usage.SwapLine);
            Set(L.T("Discos"), L.T("Dispositivos inteiros"), "E/S", usage.DiskLine, "");
            Set(L.T("Rede"), usage.NetAddresses, L.T("Tráfego"), usage.NetLine, usage.NetPerInterface);
            HeroReadings();
        }
        usage.Updated += UiKit.OnScreen(components, Components); Components();
        // Health checklist, recent events and occupancy rings.
        var healthList = new StackPanel { Name = "OverviewHealth", Spacing = 6 };
        var healthStatus = new TextBlock { Name = "OverviewHealthStatus", FontSize = 11, Foreground = Palette.Faint };
        var refreshHealth = new Button { Name = "BtnHealthRefresh", Content = L.T("Verificar agora"), Padding = new Thickness(12, 4), FontSize = 11 };
        refreshHealth.Click += async (_, _) => await RefreshHealthAsync();
        var eventsList = new StackPanel { Name = "OverviewEvents", Spacing = 4 };
        var rings = new WrapPanel { Name = "OverviewRings" };
        async Task RefreshHealthAsync()
        {
            refreshHealth.IsEnabled = false; healthStatus.Text = L.T("Lendo (somente leitura)…");
            try
            {
                IReadOnlyList<HealthCheck> checks = OperatingSystem.IsLinux() ? await GatherHealthAsync() : [];
                healthList.Children.Clear();
                foreach (var c in checks)
                    healthList.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = {
                        new TextBlock { Text = c.Ok == true ? "✓" : c.Ok == false ? "!" : "?", FontWeight = FontWeight.Bold, Width = 16, Foreground = c.Ok == false ? Palette.Warn : Palette.Muted },
                        new StackPanel { Children = { new TextBlock { Text = c.Title, FontSize = 13 }, new TextBlock { Text = c.Detail, FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 } } } } });
                healthStatus.Text = L.T($"Lido às {DateTime.Now:HH:mm:ss}. \"?\" = fonte indisponível, não é sinal de problema.");
            }
            catch (OperationCanceledException) { }
            finally { refreshHealth.IsEnabled = true; }
        }
        void RefreshEvents()
        {
            eventsList.Children.Clear();
            var items = sessionEvents.Select(e => (e.At, e.Title, e.Text)).Concat(IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).BenchRuns.Take(8).Select(r => (r.At, r.Kind, r.Summary))).OrderByDescending(x => x.Item1).Take(8).ToList();
            if (items.Count == 0) eventsList.Children.Add(new TextBlock { Text = L.T("Nenhum evento ainda. Resultados de benchmark e dispositivos conectados aparecem aqui."), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            foreach (var (at, title, text) in items)
                eventsList.Children.Add(new StackPanel { Children = { new TextBlock { Text = $"{at.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} · {title}", FontSize = 12, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = text, FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap } } });
        }
        void RefreshRings()
        {
            rings.Children.Clear();
            void Ring(string title, double fraction, string text) => rings.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 8), Spacing = 12, Children = {
                new Grid { Children = { new RingGauge(fraction), new TextBlock { Text = (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%", FontFamily = new FontFamily("monospace"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } } },
                new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Children = { Label(title), new TextBlock { Text = text, FontSize = 12, Foreground = Palette.Muted } } } } });
            if (usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l) Ring(L.T("MEMÓRIA"), (double)u / l.MemoryTotal!.Value, usage.MemoryLine);
            if (usage.Last is { SwapTotal: > 0, SwapUsed: { } su } sl) Ring("SWAP", (double)su / sl.SwapTotal!.Value, usage.SwapLine);
            if (OperatingSystem.IsLinux())
                try { foreach (var d in MountedDrives.Read(File.ReadAllText("/proc/self/mounts")).Take(4)) Ring(L.T("DISCO · ") + d.Title, d.UsedFraction, d.Sizes); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        overviewRefresh += () => { RefreshEvents(); RefreshRings(); };
        overviewRefresh += () => { if (healthList.Children.Count == 0) _ = RefreshHealthAsync(); };
        var lower = new Grid { ColumnDefinitions = new("*,16,*"), Margin = new Thickness(0, 14, 0, 0) };
        var healthCard = CardBox(new StackPanel { Spacing = 10, Children = { Label(L.T("SAÚDE DO PC")), healthList, healthStatus, refreshHealth } }, new Thickness(22, 18));
        var eventsCard = CardBox(new StackPanel { Spacing = 10, Children = { Label(L.T("EVENTOS RECENTES")), eventsList } }, new Thickness(22, 18));
        Grid.SetColumn(eventsCard, 2); lower.Children.Add(healthCard); lower.Children.Add(eventsCard);
        var ringCard = CardBox(new StackPanel { Spacing = 8, Children = { Label(L.T("OCUPAÇÃO")), rings } }, new Thickness(22, 18)); ringCard.Margin = new Thickness(0, 14, 0, 0);
        var full = new StackPanel { Name = "OverviewFull", Children = { row1, UiKit.SectionHeader(L.T("Atividade")), chartCard, UiKit.SectionHeader(L.T("Componentes")), components, lower, ringCard } };
        // Simple mode ("Seu PC agora") is the first screen for whoever only wants to know whether the PC is fine; the full view is one click away.
        bool simpleOn = IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).SimpleOverview ?? true;
        var simple = new SimpleOverview(usage, k => { if (pages.ContainsKey(k)) Select(k); }, () => this, canOpen: pages.ContainsKey) { IsVisible = simpleOn };
        full.IsVisible = !simpleOn;
        var modeSwitch = new ToggleSwitch { Name = "OverviewSimpleToggle", IsChecked = simpleOn, OnContent = L.T("Modo simples"), OffContent = L.T("Modo simples"), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right };
        Avalonia.Automation.AutomationProperties.SetName(modeSwitch, L.T("Modo simples"));
        modeSwitch.IsCheckedChanged += (_, _) =>
        {
            bool on = modeSwitch.IsChecked == true; simple.IsVisible = on; full.IsVisible = !on;
            try { IlmenyxSettings.Update(IlmenyxSettings.DefaultPath(), st => st.SimpleOverview = on); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
        var content = new StackPanel { Margin = new Thickness(0, 0, 8, 12), Spacing = 6, Children = { modeSwitch, simple, full } };
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

    /// <summary>pci.ids names put the marketing name in brackets ("GM108M [GeForce 940MX]"): show that part.</summary>
    internal static string MarketingName(string name) =>
        name.IndexOf('[') is var open and >= 0 && name.IndexOf(']', open) is var close and > 0 && close > open + 1 ? name[(open + 1)..close].Split('/')[0].Trim() : name;

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

    private Control DisksPage() => Scroll("DisksScroll", new DiskHealthPage(() => this, stop.Token, Select) { Margin = new Thickness(0, 4, 8, 12) });

    private Control EnergyPage() => Scroll("EnergyScroll", new Ilmenyx.Desktop.EnergyPage(sensors, stop.Token) { Margin = new Thickness(0, 4, 8, 12) });

    private NetworkPage? networkPage;

    private Control NetPage() => Scroll("NetScroll", networkPage = new NetworkPage(new SystemNetworkProbe(), usage, () => this, stop.Token) { Margin = new Thickness(0, 4, 8, 12) });

    private Control ValidatePage()
    {
        cpuPanel = new CpuValidationPanel(cpu, Destination("cpu"), stop.Token) { Margin = new Thickness(0, 0, 6, 0) };
        var memoryPanel = new MemoryValidationPanel(memory, Destination("memory"), stop.Token) { Margin = new Thickness(6, 0, 0, 12) };
        var simPanel = new GameSimValidationPanel(simulation, Destination("sim"), stop.Token) { Margin = new Thickness(6, 0, 0, 0) };
        // Asymmetric grid: the CPU renderer (the main validation) takes the wider column; memory and GameSim stack beside it. Tab order stays CPU, memory, GameSim.
        Grid.SetRowSpan(cpuPanel, 2); Grid.SetColumn(memoryPanel, 1); Grid.SetColumn(simPanel, 1); Grid.SetRow(simPanel, 1);
        var grid = new Grid { ColumnDefinitions = new("1.35*,*"), RowDefinitions = new("Auto,*"), Children = { cpuPanel, memoryPanel, simPanel } };
        return Scroll("ValidateScroll", new StackPanel
        {
            Margin = new Thickness(0, 4, 8, 12),
            Children = { UiKit.PageHeader(L.T("Validações reduzidas"), L.T("Execuções curtas e determinísticas que conferem os algoritmos do CPU, da memória e do GameSim · não medem desempenho nem saúde do hardware")), grid }
        });
    }

    /// <summary>
    /// What Ilmenyx released on this PC (found by its own files under /etc), each with DESFAZER: the first-run screen and the sensor
    /// dialog offer the undo only right after the change, so the way back has to stay reachable here.
    /// </summary>
    private Control? GrantsCard()
    {
        var grants = SensorHelp.Granted(File.Exists);
        if (grants.Count == 0) return null;
        var list = new StackPanel { Spacing = 8, Children = { UiKit.Label(L.T("PERMISSÕES DADAS PELO ILMENYX")) } };
        for (int i = 0; i < grants.Count; i++)
        {
            var g = grants[i];
            var status = new TextBlock { Name = "GrantStatus" + i, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var undo = new Button { Name = "GrantUndo" + i, Content = L.T("Desfazer"), FontSize = 12, Padding = new Thickness(14, 7), IsEnabled = PrivilegedFix.Available };
            if (!PrivilegedFix.Available) status.Text = L.T("O pedido de senha do sistema (pkexec/polkit) não está instalado.");
            undo.Click += async (_, _) =>
            {
                if (!await ConfirmDialog.AskAsync(this, g.Title, L.T("Desfazer?\n\nO sistema vai abrir a janela dele pedindo a sua senha de administrador."), L.T("Desfazer"), L.T("Cancelar"))) return;
                undo.IsEnabled = false; status.Text = L.T("Aguardando a sua senha na janela do sistema…"); status.Foreground = Palette.Muted;
                var (ok, message) = await PrivilegedFix.RunAsync(g.Undo, stop.Token);
                status.Text = ok ? g.Done : message; status.Foreground = ok ? Palette.Success : Palette.Warn;
                undo.IsEnabled = !ok;
            };
            list.Children.Add(new WrapPanel { ItemSpacing = 10, LineSpacing = 6, Children = { new TextBlock { Text = g.Title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }, undo, status } });
        }
        return UiKit.Card(list);
    }

    private Control ToolsPage()
    {
        var requirements = new Button { Name = "ShowRequirements", Content = L.T("Requisitos") };
        var about = new Button { Name = "About", Content = L.T("Licenças e créditos") };
        var classicButton = new Button { Name = "OpenClassic", Content = L.T("Painel clássico de diagnósticos") };
        requirements.Click += async (_, _) => { await new RequirementsDialog().ShowDialog(this); if (IsVisible) requirements.Focus(); };
        about.Click += async (_, _) => { await new AboutDialog().ShowDialog(this); if (IsVisible) about.Focus(); };
        classicButton.Click += (_, _) => OpenClassicDiagnostics();
        var toastToggle = new CheckBox { Name = "DeviceToastsToggle", Content = L.T("Avisar quando um pendrive/unidade ou dispositivo USB for conectado ou removido"), IsChecked = appSettings.DeviceToasts };
        toastToggle.IsCheckedChanged += (_, _) =>
        {
            appSettings.DeviceToasts = toastToggle.IsChecked == true; if (toasts is not null) toasts.Enabled = appSettings.DeviceToasts;
            bool on = appSettings.DeviceToasts;
            try { IlmenyxSettings.Update(settingsPath, s => s.DeviceToasts = on); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
        var page = new StackPanel
        {
            Spacing = 12,
            Children = { UiKit.PageHeader(L.T("Requisitos e sobre")), UiKit.Card(new StackPanel { Spacing = 12, Children = { UiKit.Label(L.T("FERRAMENTAS")), new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Children = { requirements, about, classicButton } },
                Wrapped("ToolsNote", ProductIdentity.Attribution, 12, Palette.Faint) } }) }
        };
        page.Children.Add(PreferencesCard(toastToggle));
        if (GrantsCard() is { } grants) page.Children.Add(grants);
        return Scroll("ToolsScroll", page);
    }
}
