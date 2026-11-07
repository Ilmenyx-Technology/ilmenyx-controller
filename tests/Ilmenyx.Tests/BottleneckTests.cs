// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class BottleneckTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-bn-" + Guid.NewGuid().ToString("N"));
    public BottleneckTests() { Directory.CreateDirectory(Path.Combine(root, "proc/net")); Directory.CreateDirectory(Path.Combine(root, "sys")); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static LoadSample S(double? gpu = 50, double? cpu = 30, double? core = 40, double? vram = 50, double? ram = 50, double? gpuT = 60, double? cpuT = 60, bool cap = false, double? fps = null) =>
        new(gpu, cpu, core, "núcleo 3", vram, ram, gpuT, cpuT, cap, fps);

    [Theory]
    [InlineData(97, 99, 99, 99, 99, false, Limiter.Vram)]      // VRAM first, even with everything else maxed
    [InlineData(50, 99, 99, 50, 50, true, Limiter.Thermal)]
    [InlineData(95, 10, 10, 10, 10, false, Limiter.Gpu)]
    [InlineData(80, 10, 85, 10, 10, false, Limiter.Cpu)]       // one hot core
    [InlineData(80, 85, 10, 10, 10, false, Limiter.Cpu)]       // total CPU
    [InlineData(50, 50, 50, 10, 92, false, Limiter.Ram)]
    [InlineData(50, 50, 50, 10, 10, false, Limiter.Capped)]
    public void ClassifyFollowsTheOriginalRuleOrder(double gpu, double cpu, double core, double vram, double ram, bool cap, Limiter expected)
    {
        if (expected == Limiter.Vram) Assert.Equal(expected, S(gpu, cpu, core, vram: 97, ram: ram, cap: cap).Classify());
        else Assert.Equal(expected, S(gpu, cpu, core, vram: vram, ram: ram, cap: cap).Classify());
    }

    [Fact]
    public void ThermalBoundariesAndMissingValuesNeverTriggerALimiter()
    {
        Assert.Equal(Limiter.Thermal, S(cpuT: 95).Classify()); Assert.Equal(Limiter.Capped, S(cpuT: 94.9).Classify());
        Assert.Equal(Limiter.Capped, new LoadSample(null, null, null, null, null, null, null, null, false, null).Classify());
        Assert.Equal(Limiter.Gpu, S(gpu: 90).Classify()); Assert.Equal(Limiter.Capped, S(gpu: 89.9).Classify());
    }

    [Fact]
    public void TitlesAdviceAndBalanceTexts()
    {
        foreach (Limiter l in Enum.GetValues<Limiter>()) { Assert.NotEmpty(BottleneckAnalyzer.Title(l)); Assert.NotEmpty(BottleneckAnalyzer.Advice(l)); }
        Assert.Equal("Gargalo de CPU", BottleneckAnalyzer.Title(Limiter.Cpu)); Assert.Contains("swap", BottleneckAnalyzer.Advice(Limiter.Ram));
        Assert.Contains("muito mais forte", BottleneckAnalyzer.Balance(200, 100).Text); Assert.Contains("equilibrado", BottleneckAnalyzer.Balance(100, 100).Text);
        Assert.Contains("CPU mais forte", BottleneckAnalyzer.Balance(60, 100).Text); Assert.Contains("muito fraca", BottleneckAnalyzer.Balance(20, 100).Text);
    }

    [Fact]
    public void SessionBuildsDistributionAveragesAndProgressFromAControlledClock()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0); var session = new BottleneckSession(TimeSpan.FromMinutes(1), () => now);
        session.Add(S(gpu: 95, fps: 100)); session.Add(S(gpu: 95, fps: 120)); session.Add(S(gpu: 20, core: 90)); session.Add(S(gpu: 20, core: 10));
        Assert.Equal(4, session.Count); Assert.Equal(Limiter.Gpu, session.Main);
        var d = session.Distribution().ToDictionary(x => x.Limiter, x => x.Pct);
        Assert.Equal(50, d[Limiter.Gpu]); Assert.Equal(25, d[Limiter.Cpu]); Assert.Equal(25, d[Limiter.Capped]);
        Assert.Equal(110, session.AvgFps); Assert.Equal(57.5, session.Avg(x => x.GpuLoad)); Assert.Equal(90, session.Max(x => x.CpuMaxCore));
        Assert.False(session.Done); Assert.Equal(0, session.Progress);
        now = now.AddSeconds(30); Assert.Equal(0.5, session.Progress); now = now.AddSeconds(31); Assert.True(session.Done); Assert.Equal(1, session.Progress);
        Assert.Null(new BottleneckSession(TimeSpan.FromMinutes(1)).Main);
    }

    [Fact]
    public void CatalogEstimatesAndLookupsKeepTheOriginalBehaviour()
    {
        Assert.Equal(100, GameCatalog.GpuPerf("NVIDIA GeForce RTX 4070")); Assert.Equal(116, GameCatalog.GpuPerf("GeForce RTX 4070 SUPER")); Assert.Equal(150, GameCatalog.GpuPerf("AMD Radeon RX 7900 XT"));
        Assert.Equal(7, GameCatalog.GpuPerf("Intel Iris Xe")); Assert.Null(GameCatalog.GpuPerf("Matrox G200eR2")); // Iris Xe added to the table on 2026-11-07 Assert.Equal(100, GameCatalog.CpuPerf("AMD Ryzen 7 7700X 8-Core Processor"));
        Assert.Equal("RTX 5070", GameCatalog.ShortGpuName("NVIDIA GeForce RTX 5070")); Assert.Equal("Ryzen 7 7700X", GameCatalog.ShortCpuName("AMD Ryzen 7 7700X 8-Core Processor"));
        var cs2 = GameCatalog.Games.Single(g => g.Name == "Counter-Strike 2");
        var e = GameCatalog.Estimate(cs2, 100, 100, "1920x1080", "Alto", "Nenhum");
        Assert.Equal(340, e.Avg); Assert.Equal(Bottleneck.Gpu, e.Limiter); Assert.Equal(255, e.Low1);
        Assert.Equal(Bottleneck.Cpu, GameCatalog.Estimate(cs2, 300, 50, "1920x1080", "Alto", "Nenhum").Limiter);
        Assert.Equal(60, GameCatalog.Estimate(GameCatalog.Games.Single(g => g.Name == "Elden Ring"), 300, 300, "1920x1080", "Alto", "Nenhum").Avg);
    }

    [Fact]
    public async Task ProviderReportsTheBusiestCoreFromPerCoreDeltas()
    {
        string stat = Path.Combine(root, "proc/stat");
        void W(long c0busy, long c1busy) => File.WriteAllText(stat, $"cpu  {c0busy + c1busy + 200} 0 0 100 0 0 0 0 0 0\ncpu0 {c0busy} 0 0 100 0 0 0 0 0 0\ncpu1 {c1busy} 0 0 100 0 0 0 0 0 0\n");
        File.WriteAllText(Path.Combine(root, "proc/meminfo"), "MemTotal: 1000 kB\nMemAvailable: 500 kB\n");
        W(0, 0); var provider = new LinuxUsageProvider(Path.Combine(root, "proc"), Path.Combine(root, "sys"));
        var first = await provider.SampleAsync(); Assert.Null(first.CpuMaxCorePercent); Assert.Null(first.BusiestCore);
        File.WriteAllText(stat, "cpu  400 0 0 400 0 0 0 0 0 0\ncpu0 90 0 0 110 0 0 0 0 0 0\ncpu1 10 0 0 190 0 0 0 0 0 0\n"); // cpu0: +90 busy/+10 idle = 90 %, cpu1: 10 %
        var second = await provider.SampleAsync();
        Assert.Equal("núcleo 0", second.BusiestCore); Assert.InRange(second.CpuMaxCorePercent!.Value, 89.9, 90.1);
    }
}
