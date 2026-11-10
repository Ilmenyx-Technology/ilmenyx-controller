// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Calibration (C), spec fallback (B), personal factor (D), light-game exponents and the gate. Nothing here runs a benchmark.
using System.Text.Json;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class FpsCalibrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-fpscal-" + Guid.NewGuid().ToString("N"));
    public FpsCalibrationTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static GameInfo Game(string name) => GameCatalog.Games.Single(g => g.Name == name);

    [Fact]
    public void SceneFitReproducesTheTeamAnchorsAndScalesToTheReference()
    {
        // 940MX 172 → 4,5; HD 620 109 → 2,5; Radeon 610M 150 → 3 (docs/evidencias). One constant, slope 1: each within 25 %.
        foreach (var (score, index) in new[] { (172.0, 4.5), (109.0, 2.5), (150.0, 3.0) })
            Assert.InRange(FpsModels.SceneToGpuIndex(score) / index, 0.8, 1.25);
        Assert.Equal(3.94, FpsModels.SceneToGpuIndex(172));
        // An RTX 4070 (index 100) would need ~4370 points: ≈ 25× the 940MX, close to their FP32 ratio (≈ 31×).
        double score4070 = 100 / FpsModels.SceneK;
        Assert.InRange(score4070 / 172, 20, 32);
        Assert.InRange(FpsModels.Tflops(5888, 2475) / FpsModels.Tflops(384, 1242), 28, 33);
    }

    [Fact]
    public void CpuFitIsAnchoredOnTheMeasuredI7_7500U()
    {
        Assert.Equal(36, FpsModels.SimToCpuIndex(22.06));
        Assert.Equal(72, FpsModels.SimToCpuIndex(44.12)); // twice the single-thread game simulation = twice the index
    }

    [Theory]
    [InlineData("10DE:2786", "NVIDIA GeForce RTX 4070", 100)]
    [InlineData("10DE:2684", "NVIDIA GeForce RTX 4090", 210)]
    [InlineData("10DE:2882", "NVIDIA GeForce RTX 4060", 70)]
    [InlineData("10DE:2C02", "NVIDIA GeForce RTX 5080", 175)]
    [InlineData("10DE:2484", "NVIDIA GeForce RTX 3070", 86)]
    [InlineData("1002:744C", "AMD Radeon RX 7900 XTX", 170)]
    [InlineData("1002:7480", "AMD Radeon RX 7600", 70)]
    [InlineData("1002:7550", "AMD Radeon RX 9070 XT", 160)]
    [InlineData("10DE:134D", "NVIDIA GeForce 940MX", 4.5)]
    public void SpecModelReproducesTheIndexTableWithinTenPercent(string pci, string name, double tableIndex)
    {
        var spec = GpuSpecDb.Find(pci[..4], pci[5..], name)!;
        double idx = FpsModels.SpecGpuIndex(spec)!.Value;
        Assert.InRange(idx / tableIndex, 0.9, 1.1);
    }

    [Fact]
    public void SpecModelUsesALowerDriverClockButNeverAHigherOne()
    {
        var mx = GpuSpecDb.Find("10DE", "134D", "GeForce 940MX")!;
        double boost = FpsModels.SpecGpuIndex(mx)!.Value;
        Assert.True(FpsModels.SpecGpuIndex(mx, 1189) < boost); // nvidia-smi max clock on the Dell 7460
        Assert.Equal(boost, FpsModels.SpecGpuIndex(mx, 3000));  // a higher reported clock is ignored
        Assert.Null(FpsModels.SpecGpuIndex(mx with { Arch = "Desconhecida" }));
        Assert.Null(FpsModels.SpecGpuIndex(mx with { Shaders = 0 }));
    }

    [Fact]
    public void LightGamesNoLongerCollapseOnWeakGpus()
    {
        // Maintainer's observation (2026-11-08): League of Legends > 100 FPS on Low with a GeForce 940MX + i7-7500U. Before: 16–44.
        var lol = GameCatalog.Estimate(Game("League of Legends"), 4.5, 36, "1920x1080", "Baixo", "Native");
        Assert.InRange(lol.Avg, 100, 200);
        // Valorant now follows its measured curve (Notebookcheck, 1080p High): UHD 620 33,6 FPS, MX250 115, GTX 1650 Mobile 148; each within ×1,7
        // (Valorant is the noisiest fit: on laptops the CPU often limits it, so weak iGPUs are the least certain point).
        foreach (var (idx, fps) in new[] { (2.5, 33.6), (7.5, 115.0), (28.0, 148.0) })
            Assert.InRange(GameCatalog.Estimate(Game("Valorant"), idx, 100, "1920x1080", "Alto", "Native").Avg / fps, 1 / 1.7, 1.7);
        // Games without a measured table (LoL) keep the reference value at index 100.
        Assert.Equal(420, GameCatalog.Estimate(Game("League of Legends"), 100, 100, "1920x1080", "Alto", "Native").Avg);
        Assert.Equal(1.0, Game("Cyberpunk 2077").GpuExp);
        Assert.InRange(GameCatalog.Estimate(Game("Cyberpunk 2077"), 4.5, 100, "1920x1080", "Alto", "Native").Avg, 2, 8); // measured: MX130 2,6, MX250 5,0
    }

    [Fact]
    public void GateFollowsTheFingerprint()
    {
        string fp = FpsModels.Fingerprint("10de", "134d", "nvidia", "580.95.05", "Intel(R) Core(TM) i7-7500U CPU @ 2.70GHz");
        Assert.Equal(FpsGate.Uncalibrated, FpsModels.GateFor(null, fp));
        var cal = new FpsCalibration(DateTimeOffset.Now, fp, "940MX", "nvidia", "580.95.05", "i7-7500U", 172, "GeForce 940MX", 22.06);
        Assert.Equal(FpsGate.Calibrated, FpsModels.GateFor(cal, fp));
        Assert.Equal(FpsGate.Calibrated, FpsModels.GateFor(cal, FpsModels.Fingerprint("10DE", "134D", "nvidia", "580.95.05", "Core i7-7500U"))); // same hardware, other spelling
        Assert.Equal(FpsGate.Stale, FpsModels.GateFor(cal, FpsModels.Fingerprint("10de", "134d", "nvidia", "580.105.08", "i7-7500U"))); // driver update
        Assert.Equal(FpsGate.Stale, FpsModels.GateFor(cal, FpsModels.Fingerprint("10de", "134d", "nouveau", "", "i7-7500U")));
        Assert.Equal(FpsGate.Stale, FpsModels.GateFor(cal, FpsModels.Fingerprint("10de", "2786", "nvidia", "580.95.05", "i7-7500U"))); // new GPU
        Assert.Equal((3.94, 36.0), FpsModels.Indices(cal) is (double g, double c) ? (g, c) : default);
        var none = FpsModels.Indices(cal with { SceneScore = null, CpuSimFps = 0 }); Assert.Null(none.Gpu); Assert.Null(none.Cpu);
    }

    [Fact]
    public void CalibrationIsSavedLocallyAndOldFilesWithoutItStillLoad()
    {
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, """{"ThemeLight": true, "FpsSessions": [{"Game": "Dota 2", "At": "2026-11-06T21:00:00+00:00", "AvgFps": 87.4, "Low1Fps": 52, "DurationSeconds": 1800, "Gpu": null, "Resolution": "1920x1080"}]}""");
        var old = IlmenyxSettings.Load(path);
        Assert.Null(old.FpsCalibration); Assert.Null(Assert.Single(old.FpsSessions).Preset);
        var cal = new FpsCalibration(new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero), "fp", "940MX", "nvidia", "580", "i7-7500U", 172, "GeForce 940MX", 22.06, null);
        IlmenyxSettings.Update(path, s => s.FpsCalibration = cal);
        var back = IlmenyxSettings.Load(path);
        Assert.Equal(cal, back.FpsCalibration); Assert.Single(back.FpsSessions); Assert.True(back.ThemeLight);
    }

    [Fact]
    public void SessionsKeepTheInformedPresetAndUpscaling()
    {
        var stats = new MangoHudSessionStats(87.4, 52, 1800, 18000, "GeForce 940MX");
        var s = MangoHudSession.ToSession(stats, "Dota 2", DateTimeOffset.UnixEpoch, "1920x1080", "Baixo", "Quality")!;
        Assert.Equal("Baixo", s.Preset); Assert.Equal("Quality", s.Upscaling);
        var none = MangoHudSession.ToSession(stats, "Dota 2", DateTimeOffset.UnixEpoch, "default", "", "")!;
        Assert.Null(none.Preset); Assert.Null(none.Upscaling); Assert.Null(none.Resolution);
        Assert.Null(MangoHudSession.ToSession(stats, "Dota 2", DateTimeOffset.UnixEpoch, "1920x1080", "Épico", "DLAA")!.Preset); // only known values
        var json = JsonSerializer.Serialize(s);
        Assert.Equal(s, JsonSerializer.Deserialize<FpsSession>(json));
        var profile = new GameProfile(); Assert.Equal("", profile.GfxPreset); Assert.Equal("", profile.GfxUpscaling);
    }

    private static FpsSession Session(string game, double fps, string? preset = "Alto", string? res = "1920x1080", string? gpu = null, int minutesAgo = 0) =>
        new(game, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), fps, fps * 0.7, 600, gpu, res, preset, null);

    [Fact]
    public void PersonalFactorIsTheShrunkMedianRatioOfMatchingSessions()
    {
        // GPU 4,5 + CPU 100: these heavy games are GPU-limited at Alto 1080p. Measured = half the estimate in each.
        string[] heavy = ["Cyberpunk 2077", "Hogwarts Legacy", "Starfield"];
        double Est(string n) => GameCatalog.Estimate(Game(n), 4.5, 100, "1920x1080", "Alto", "Native").Avg;
        var three = heavy.Select(n => Session(n, Est(n) / 2)).ToList();
        var f3 = FpsModels.Personal(three, GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu)!;
        Assert.Equal(3, f3.Sessions); Assert.Equal(Math.Round(Math.Exp(Math.Log(0.5) * 3 / 5), 3), f3.Factor);
        var f1 = FpsModels.Personal(three.Take(1), GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu)!;
        Assert.Equal(Math.Round(Math.Exp(Math.Log(0.5) / 3), 3), f1.Factor); // one session moves only a third of the way
        // Only sessions with the same preset; none without a preset or a resolution; none from another GPU.
        Assert.Null(FpsModels.Personal(three, GameCatalog.Games, 4.5, 100, "Baixo", Bottleneck.Gpu));
        Assert.Null(FpsModels.Personal(heavy.Select(n => Session(n, 10, preset: null)), GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu));
        Assert.Null(FpsModels.Personal(heavy.Select(n => Session(n, 10, res: null)), GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu));
        Assert.Null(FpsModels.Personal(heavy.Select(n => Session(n, 10, gpu: "NVIDIA GeForce RTX 4070")), GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu, "NVIDIA GeForce 940MX"));
        Assert.NotNull(FpsModels.Personal(heavy.Select(n => Session(n, 10, gpu: "GeForce 940MX")), GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu, "NVIDIA Corporation GM108M [GeForce 940MX]"));
        // GPU-limited sessions say nothing about CPU-limited games; extreme ratios are capped to 0,5…2.
        Assert.Null(FpsModels.Personal(three, GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Cpu));
        var crazy = heavy.Select(n => Session(n, Est(n) * 50)).ToList();
        Assert.Equal(FpsModels.MaxFactor, FpsModels.Personal(crazy, GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu)!.Factor);
        var tiny = heavy.Select(n => Session(n, Math.Max(1, Est(n) / 50))).ToList();
        Assert.Equal(FpsModels.MinFactor, FpsModels.Personal(tiny, GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu)!.Factor);
        // Only the newest session of a game counts.
        var twice = new List<FpsSession> { Session("Cyberpunk 2077", Est("Cyberpunk 2077") * 2, minutesAgo: 10), Session("Cyberpunk 2077", Est("Cyberpunk 2077")) };
        Assert.Equal(1.0, FpsModels.Personal(twice, GameCatalog.Games, 4.5, 100, "Alto", Bottleneck.Gpu)!.Factor);
    }

    [Fact]
    public void ConfidenceDependsOnTheBasisAndOnPersonalSessions()
    {
        var cal = FpsModels.Assess(FpsBasis.Calibrated, HardwareMatch.Laptop, 4, true, true, 36);
        Assert.Equal(FpsConfidence.VeryLow, cal.Confidence); // far from the RTX 4070 even when measured
        Assert.Contains(cal.Reasons, r => r.StartsWith("GPU medida neste PC", StringComparison.Ordinal));
        Assert.Contains(cal.Reasons, r => r.StartsWith("CPU medida neste PC", StringComparison.Ordinal));
        Assert.DoesNotContain(cal.Reasons, r => r.StartsWith("CPU de notebook", StringComparison.Ordinal));
        var mid = FpsModels.Assess(FpsBasis.Calibrated, HardwareMatch.Desktop, 80, false, true, 90);
        Assert.Equal(FpsConfidence.Low, mid.Confidence); Assert.InRange(mid.Spread, 0.25, 0.32);
        var spec = FpsModels.Assess(FpsBasis.Specs, HardwareMatch.Desktop, 80, false, false, 100);
        Assert.Equal(FpsConfidence.VeryLow, spec.Confidence); Assert.InRange(spec.Spread, 0.45, 0.5);
        var t1 = FpsAccuracy.Assess(HardwareMatch.Desktop, 100, false, 100); var t2 = FpsModels.Assess(FpsBasis.Table, HardwareMatch.Desktop, 100, false, false, 100);
        Assert.Equal(t1.Confidence, t2.Confidence); Assert.Equal(t1.Spread, t2.Spread); Assert.Equal(t1.Reasons, t2.Reasons);
        var up = FpsModels.WithPersonal(mid, new PersonalFactor(0.8, 3, ["a", "b", "c"]));
        Assert.Equal(FpsConfidence.Medium, up.Confidence); Assert.Equal(mid.Spread * 0.8, up.Spread, 6);
        var same = FpsModels.WithPersonal(mid, new PersonalFactor(0.8, 2, ["a", "b"]));
        Assert.Equal(mid.Confidence, same.Confidence); Assert.Contains(same.Reasons, r => r.Contains("suas 2 sessões medidas", StringComparison.Ordinal));
    }

    [Fact]
    public void RowsShowProvisionalOrBasisAndApplyThePersonalFactor()
    {
        var row = new GameFpsRow(Game("Elden Ring"));
        var a = FpsModels.Assess(FpsBasis.Calibrated, HardwareMatch.Desktop, 100, false, true, 100);
        var e = GameCatalog.Estimate(row.Game, 30, 100, "1920x1080", "Alto", "Native");
        row.SetEstimate(e, a, FpsBasis.Table, true, null);
        Assert.True(row.IsProvisional); Assert.Equal("PROVISÓRIO", row.Source); Assert.Equal("provisório — calibre o PC", row.BasisText); Assert.Equal("sem confiança", row.LowText);
        Assert.StartsWith("Provisório — calibre o PC", row.Summary);
        row.SetEstimate(e, a, FpsBasis.Calibrated, false, new PersonalFactor(2, 3, ["x", "y", "z"]));
        Assert.False(row.IsProvisional); Assert.Equal("ESTIMATIVA", row.Source);
        Assert.Equal("base: benchmark deste PC · suas 3 sessões medidas", row.BasisText);
        Assert.Equal(Math.Min(60, Math.Round(e.Avg * 2)), row.Fps); // never above the engine cap
        Assert.Equal(FpsConfidence.Medium, row.Assessment!.Confidence);
        row.SetEstimate(e, a, FpsBasis.Specs, false, null);
        Assert.Equal("base: especificações", row.BasisText); Assert.Equal(e.Avg, row.Fps);
    }

    [Fact]
    public void MeasuredCurvesFollowRealBenchmarksOnWeakLaptopGpus()
    {
        // Notebookcheck CS2 (1080p): MX250 (índice 7,5) Low 75 / High 24; MX450 (11) Low 87,5 / High 34,8. Within ×1,5.
        var cs2 = Game("Counter-Strike 2");
        foreach (var (idx, preset, fps) in new[] { (7.5, "Baixo", 75.1), (7.5, "Alto", 24.3), (11.0, "Baixo", 87.5), (11.0, "Alto", 34.8) })
            Assert.InRange(GameCatalog.Estimate(cs2, idx, 100, "1920x1080", preset, "Native").Avg / fps, 1 / 1.5, 1.5);
        // A lower preset is never slower than a higher one, for every measured game and GPU strength.
        foreach (var (name, c) in GameCatalog.Measured)
            foreach (double idx in new[] { 1.0, 4.5, 30, 100, 200 })
                Assert.True(c.Fps(idx, "Baixo") >= c.Fps(idx, "Médio") && c.Fps(idx, "Médio") >= c.Fps(idx, "Alto") && c.Fps(idx, "Alto") >= c.Fps(idx, "Ultra"), name);
        Assert.All(GameCatalog.Measured.Keys, n => Assert.Contains(GameCatalog.Games, g => g.Name == n)); // every curve belongs to a catalog game
    }
}
