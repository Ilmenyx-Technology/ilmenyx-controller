// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public class OverlayCoreTests
{
    private static UsageSample Usage() => new(DateTimeOffset.UtcNow, "CPU", 37.4, 3600, 61, 16L << 30, 8L << 30, null, null, null, null, null, 100, [], 88, "cpu3");
    private static GpuTelemetry Gpu() => new(55, 66, null, null, 1500, 1800, 2UL << 30, 8UL << 30, 35.5, null, null, 1200, null, null, null, null, null, null, null);

    [Fact]
    public void CatalogKeysAreUniqueAndDefaultsAreSubset()
    {
        var keys = OverlayMetrics.All.Select(m => m.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.True(OverlayMetrics.Defaults().IsSubsetOf(OverlayMetrics.Everything()));
        Assert.True(OverlayMetrics.Minimal().IsSubsetOf(OverlayMetrics.Defaults()));
    }

    [Fact]
    public void SanitizeDropsUnknownKeysAndKeepsCatalogOrder()
    {
        Assert.Equal(["fps", "cpu_load"], OverlayMetrics.Sanitize(["cpu_load", "bogus", "fps"]));
        Assert.Equal(OverlayMetrics.Defaults().Count, OverlayMetrics.Sanitize(null).Count);
    }

    [Fact]
    public void LinesShowOnlyChosenItemsAndDashForMissingReadings()
    {
        var lines = OverlayLines.Build(new HashSet<string> { "gpu_load", "gpu_temp", "cpu_load", "cpu_power", "ram_used", "fps", "game" }, Usage(), null, null, "Meu Jogo", DateTimeOffset.UtcNow);
        Assert.Equal("— FPS", lines.Single(l => l.Label == "FPS").Value);
        Assert.Equal("—  —", lines.Single(l => l.Label == "GPU").Value);
        Assert.Equal("37%  — W", lines.Single(l => l.Label == "CPU").Value);
        Assert.Equal("8.0 GB", lines.Single(l => l.Label == "RAM").Value);
        Assert.Equal("Meu Jogo", lines.Single(l => l.Label == "").Value);
        Assert.DoesNotContain(lines, l => l.Label == "VRAM");
    }

    [Fact]
    public void LinesFormatLiveValuesWithInvariantCulture()
    {
        var frames = new FrameStats(143.6, 6.9, 98.2, [], null);
        var lines = OverlayLines.Build(OverlayMetrics.Everything(), Usage(), Gpu(), frames, null, new DateTimeOffset(2026, 10, 22, 9, 5, 0, TimeSpan.Zero));
        string Of(string label) => lines.First(l => l.Label == label).Value;
        Assert.Equal("144 FPS  6.9 ms  1% 98", Of("FPS"));
        Assert.Contains("55%", Of("GPU")); Assert.Contains("66 °C", Of("GPU")); Assert.Contains("1500 MHz", Of("GPU")); Assert.Contains("36 W", Of("GPU")); Assert.Contains("1200 RPM", Of("GPU"));
        Assert.Equal("2.0 GB  25%  —", Of("VRAM")); // VRAM temperature not in this fixture: dash, never a guess
        Assert.Contains("núcleo 88%", Of("CPU")); Assert.Contains("3.60 GHz", Of("CPU"));
        Assert.Equal("8.0 GB  50%", Of("RAM"));
    }

    [Fact]
    public void MetricsAddedInTheOriginalOverlayOf2026_10_16()
    {
        var usage = Usage() with { CpuMaxMhz = 4386 };
        var gpu = Gpu() with { CoreVoltageV = 0.81, ThrottleMask = 0x4, TempMemC = 72 };
        var lines = OverlayLines.Build(new HashSet<string> { "gpu_volt", "gpu_perfcap", "vram_temp", "cpu_clockmax" }, usage, gpu, null, null, DateTimeOffset.UtcNow);
        Assert.Equal("0.810 V  Energia", lines.Single(l => l.Label == "GPU").Value);
        Assert.Equal("72 °C", lines.Single(l => l.Label == "VRAM").Value);
        Assert.Equal("máx 4.39 GHz", lines.Single(l => l.Label == "CPU").Value);
        var missing = OverlayLines.Build(new HashSet<string> { "gpu_volt", "gpu_perfcap", "cpu_clockmax" }, Usage(), Gpu(), null, null, DateTimeOffset.UtcNow);
        Assert.Equal("— V  limitador —", missing.Single(l => l.Label == "GPU").Value); Assert.Equal("máx —", missing.Single(l => l.Label == "CPU").Value);
        var cfg = MangoHudConfig.Build(new HashSet<string> { "gpu_volt", "gpu_perfcap", "vram_temp", "cpu_clockmax" }, "top-left", 1.0, null).Split('\n');
        foreach (var key in new[] { "gpu_stats", "gpu_voltage", "throttling_status", "vram", "gpu_mem_temp", "cpu_stats", "cpu_mhz" }) Assert.Contains(key, cfg);
    }

    [Fact]
    public void MangoHudConfigFollowsSelectionAndWritesOnlyNeededSections()
    {
        string cfg = MangoHudConfig.Build(new HashSet<string> { "fps", "frametime", "cpu_temp", "ram_used" }, "top-right", 1.25, "/home/u/.local/share/ilmenyx/mangohud");
        var lines = cfg.Split('\n');
        Assert.Contains("position=top-right", lines); Assert.Contains("font_scale=1.25", lines);
        Assert.Contains("fps", lines); Assert.Contains("frametime", lines); Assert.Contains("cpu_stats", lines); Assert.Contains("cpu_temp", lines); Assert.Contains("ram", lines);
        Assert.DoesNotContain("gpu_stats", lines); Assert.DoesNotContain("vram", lines); Assert.DoesNotContain("frame_timing", lines);
        Assert.Contains("output_folder=/home/u/.local/share/ilmenyx/mangohud", lines); Assert.Contains("autostart_log=1", lines);
        Assert.DoesNotContain("output_folder", MangoHudConfig.Build(OverlayMetrics.Defaults(), "top-left", 1, null));
    }

    [Theory]
    [InlineData("middle", 1.0, null)]
    [InlineData("top-left", 0.1, null)]
    [InlineData("top-left", 5.0, null)]
    [InlineData("top-left", 1.0, "relative/path")]
    [InlineData("top-left", 1.0, "/ok\nexec=rm -rf")]
    public void MangoHudConfigRejectsUnsafeInput(string corner, double scale, string? folder) =>
        Assert.ThrowsAny<ArgumentException>(() => MangoHudConfig.Build(OverlayMetrics.Defaults(), corner, scale, folder));

    [Fact]
    public void LaunchOptionUsesTheGeneratedFileOnly() =>
        Assert.Equal("MANGOHUD_CONFIGFILE=/c/mh.conf mangohud %command%", MangoHudConfig.LaunchOption("/c/mh.conf"));

    [Fact]
    public void RealMangoHudLogIsParsed()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mangohud-glxgears.csv"));
        var stats = MangoHudLog.Parse(text, DateTimeOffset.UtcNow);
        Assert.NotNull(stats);
        Assert.InRange(stats!.Fps!.Value, 55, 66);
        Assert.InRange(stats.FrametimeMs!.Value, 15, 18);
        Assert.InRange(stats.Low1Fps!.Value, 50, 66);
        Assert.True(stats.Low1Fps <= stats.Fps + 0.001);
        Assert.NotEmpty(stats.RecentFrametimesMs);
    }

    [Fact]
    public void LogParserToleratesGarbageTruncationAndMissingHeader()
    {
        Assert.Null(MangoHudLog.Parse("", DateTimeOffset.UtcNow));
        Assert.Null(MangoHudLog.Parse("no header\n1,2,3\n", DateTimeOffset.UtcNow));
        const string text = "os,cpu\nx,y\nfps,frametime,elapsed\n60,16.6,100000000\nbad,row\n-5,16,200000000\n61,16.4,300000000\n62,16.1";
        var stats = MangoHudLog.Parse(text, DateTimeOffset.UtcNow);
        Assert.NotNull(stats);
        Assert.Equal(2, stats!.RecentFrametimesMs.Count);
    }

    [Fact]
    public void LogStatsUseOnlyTheLastFiveSeconds()
    {
        var rows = new System.Text.StringBuilder("fps,frametime,elapsed\n");
        for (int i = 1; i <= 100; i++) rows.Append($"10,100,{i * 100_000_000L}\n");        // 10 s at 10 FPS
        for (int i = 101; i <= 200; i++) rows.Append($"100,10,{i * 100_000_000L}\n");      // 10 s at 100 FPS
        var stats = MangoHudLog.Parse(rows.ToString(), DateTimeOffset.UtcNow);
        Assert.InRange(stats!.Fps!.Value, 95, 105);
    }
}

public class MangoHudLaunchTests
{
    [Fact]
    public void SteamOptionsPointMangoHudAtTheGeneratedFileOnlyWhenEnabled()
    {
        Assert.Equal("MANGOHUD_CONFIGFILE=/h/.config/ilmenyx/mangohud.conf mangohud %command%", Ilmenyx.Core.GameLaunchOptions.BuildSteamOptions(new() { MangoHud = true }, "AMD", "/h/.config/ilmenyx/mangohud.conf"));
        Assert.Equal("mangohud %command%", Ilmenyx.Core.GameLaunchOptions.BuildSteamOptions(new() { MangoHud = true }, "AMD"));
        Assert.Equal("", Ilmenyx.Core.GameLaunchOptions.BuildSteamOptions(new(), "AMD", "/cfg"));
        Assert.Equal("MANGOHUD_CONFIGFILE='/h/my dir/it'\\''s.conf' mangohud %command%", Ilmenyx.Core.GameLaunchOptions.BuildSteamOptions(new() { MangoHud = true }, "AMD", "/h/my dir/it's.conf"));
    }

    [Fact]
    public void ManualLaunchSetsTheConfigVariableOnlyWithMangoHudInstalledAndEnabled()
    {
        var cmd = Ilmenyx.Platform.Linux.LinuxGameLauncher.BuildManual("/g/game", new() { MangoHud = true }, "", "AMD", n => "/usr/bin/" + n, "/cfg/mangohud.conf");
        Assert.Contains(new KeyValuePair<string, string>("MANGOHUD_CONFIGFILE", "/cfg/mangohud.conf"), cmd.Environment);
        Assert.Empty(Ilmenyx.Platform.Linux.LinuxGameLauncher.BuildManual("/g/game", new() { MangoHud = true }, "", "AMD", _ => null, "/cfg/mangohud.conf").Environment);
        Assert.Empty(Ilmenyx.Platform.Linux.LinuxGameLauncher.BuildManual("/g/game", new(), "", "AMD", n => "/usr/bin/" + n, "/cfg/mangohud.conf").Environment);
    }
}

public class CpuClockTests
{
    [Fact]
    public void AverageAndFastestCoreFromCpuinfo()
    {
        const string cpuinfo = "processor\t: 0\ncpu MHz\t\t: 1397.000\nprocessor\t: 1\ncpu MHz\t\t: 4386.112\nprocessor\t: 2\ncpu MHz\t\t: bogus\n";
        Assert.Equal(4386.112, Ilmenyx.Platform.Linux.LinuxUsageProvider.MaxMhz(cpuinfo));
        Assert.Equal((1397 + 4386.112) / 2, Ilmenyx.Platform.Linux.LinuxUsageProvider.AverageMhz(cpuinfo)!.Value, 6);
        Assert.Null(Ilmenyx.Platform.Linux.LinuxUsageProvider.MaxMhz("processor : 0\n"));
    }
}
