// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Synthetic MangoHud logs and hardware names only; no game is run.
using System.Globalization;
using System.Text;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class FpsAccuracyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-fpsacc-" + Guid.NewGuid().ToString("N"));
    public FpsAccuracyTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    /// <summary>MangoHud CSV in the real layout (spec header, values, column header, rows every 100 ms).</summary>
    private static string Log(double seconds, Func<int, (double Fps, double Ms)> row, string gpu = "NVIDIA GeForce 940MX")
    {
        var sb = new StringBuilder("os,cpu,gpu,ram,kernel,driver,cpuscheduler\nUbuntu,Intel Core i7-7500U," + gpu + ",8000000,6.14.0,,powersave\n");
        sb.Append("fps,frametime,cpu_load,gpu_load,elapsed\n");
        int n = (int)(seconds * 10);
        for (int i = 0; i <= n; i++) { var (f, ms) = row(i); sb.Append(string.Create(CultureInfo.InvariantCulture, $"{f},{ms},20,90,{100_000_000L * (i + 1)}\n")); }
        return sb.ToString();
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", "RTX 4070", false, HardwareMatch.Desktop)]
    [InlineData("AD104 [GeForce RTX 4070]", "RTX 4070", false, HardwareMatch.Desktop)]
    [InlineData("GM108M [GeForce 940MX]", "940MX", false, HardwareMatch.Laptop)]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", "RTX 4060 Laptop", false, HardwareMatch.Laptop)]
    [InlineData("GP106M [GeForce GTX 1060 Mobile]", "GTX 1060", false, HardwareMatch.Laptop)]
    [InlineData("GeForce MX250", "MX250", false, HardwareMatch.Laptop)]
    [InlineData("HD Graphics 620", "HD Graphics 620", true, HardwareMatch.Integrated)]
    [InlineData("AMD Radeon 780M", "Radeon 780M", false, HardwareMatch.Integrated)]
    [InlineData("AMD Radeon Graphics", "Radeon Graphics", true, HardwareMatch.Generic)]
    [InlineData("Intel UHD Graphics", "UHD Graphics", true, HardwareMatch.Generic)]
    [InlineData("Navi 32 [Radeon RX 7800 XT]", "RX 7800 XT", false, HardwareMatch.Desktop)]
    public void GpuMatchKindFollowsTheNameAndKey(string name, string key, bool integrated, HardwareMatch expected)
    {
        Assert.Equal(key, GameCatalog.GpuMatch(name)!.Value.Key);
        Assert.Equal(expected, FpsAccuracy.ClassifyGpu(name, key, integrated));
    }

    [Theory]
    [InlineData("GM108M [GeForce 940MX]", "940MX")]
    [InlineData("NVIDIA GeForce RTX 4070", "RTX 4070")]
    [InlineData("Navi 32 [Radeon RX 7800 XT]", "RX 7800 XT")]
    [InlineData("HD Graphics 620", "HD Graphics 620")]
    public void GpuDisplayNameUsesTheMarketingName(string name, string expected) => Assert.Equal(expected, FpsAccuracy.DisplayGpuName(name));

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-7500U CPU @ 2.70GHz", true)]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", true)]
    [InlineData("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", true)]
    [InlineData("AMD Ryzen 7 7700X 8-Core Processor", false)]
    [InlineData("AMD Ryzen 5 5600G with Radeon Graphics", false)]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12400F", false)]
    public void LaptopCpusAreRecognised(string cpu, bool laptop) => Assert.Equal(laptop, FpsAccuracy.IsLaptopCpu(cpu));

    [Fact]
    public void ConfidenceDependsOnMatchKindAndDistanceFromTheReference()
    {
        var desk = FpsAccuracy.Assess(HardwareMatch.Desktop, 100, false, 100);
        Assert.Equal(FpsConfidence.Medium, desk.Confidence); Assert.Equal(FpsAccuracy.BaseSpread, desk.Spread, 3); Assert.Equal("confiança média", desk.ConfidenceText);
        var laptop = FpsAccuracy.Assess(HardwareMatch.Laptop, 58, true, 90);
        Assert.Equal(FpsConfidence.Low, laptop.Confidence); Assert.Contains(laptop.Reasons, r => r.StartsWith("GPU de notebook: o limite de energia/temperatura", StringComparison.Ordinal)); Assert.Contains(laptop.Reasons, r => r.StartsWith("CPU de notebook", StringComparison.Ordinal));
        Assert.True(laptop.Spread > desk.Spread);
        Assert.Equal(FpsConfidence.Low, FpsAccuracy.Assess(HardwareMatch.Desktop, 100, true, 100).Confidence); // laptop CPU alone
        Assert.Contains(FpsAccuracy.Assess(HardwareMatch.Generic, 6, false, 100).Reasons, r => r.Contains("genérico", StringComparison.Ordinal));
        // Desktop GTX 960 (18) stays medium; very far (GT 1030, 7) drops to low.
        Assert.Equal(FpsConfidence.Medium, FpsAccuracy.Assess(HardwareMatch.Desktop, 18, false, 100).Confidence);
        Assert.Equal(FpsConfidence.Low, FpsAccuracy.Assess(HardwareMatch.Desktop, 7, false, 100).Confidence);
    }

    [Fact]
    public void ThisDellLaptopGetsAWideVeryLowConfidenceRange()
    {
        // Dell Inspiron 7460: i7-7500U + GeForce 940MX (pci.ids name as read on nouveau).
        string gpu = "GM108M [GeForce 940MX]", cpu = "Intel(R) Core(TM) i7-7500U CPU @ 2.70GHz";
        var g = GameCatalog.GpuMatch(gpu)!.Value; double c = GameCatalog.CpuMatch(cpu)!.Value.Index;
        var a = FpsAccuracy.Assess(FpsAccuracy.ClassifyGpu(gpu, g.Key), g.Index, FpsAccuracy.IsLaptopCpu(cpu), c);
        Assert.Equal(FpsConfidence.VeryLow, a.Confidence); Assert.InRange(a.Spread, 0.45, FpsAccuracy.MaxSpread);
        var row = new GameFpsRow(GameCatalog.Games.Single(x => x.Name == "Counter-Strike 2"));
        row.SetEstimate(GameCatalog.Estimate(row.Game, g.Index, c, "1920x1080", "Alto", "Native"), a);
        Assert.Equal("7–21", row.FpsText); Assert.Equal("confiança muito baixa", row.LowText); Assert.Equal("ESTIMATIVA", row.Source); Assert.False(row.IsMeasured);
    }

    [Fact]
    public void RangeNeverGoesBelowOneOrAboveTheEngineCap()
    {
        var a = new FpsAssessment(FpsConfidence.VeryLow, 0.5, []);
        var elden = GameCatalog.Games.Single(x => x.Name == "Elden Ring");
        Assert.Equal("30–60", FpsAccuracy.Range(new FpsEstimate(60, 45, Bottleneck.Engine), elden, a).Text);
        Assert.Equal("1–2", FpsAccuracy.Range(new FpsEstimate(1.2, 1, Bottleneck.Gpu), elden, a).Text);
    }

    [Theory]
    [InlineData("Counter-Strike 2", "Counter-Strike 2", true)]
    [InlineData("Counter-Strike 2", "COUNTER-STRIKE™ 2", true)]
    [InlineData("GTA V", "Grand Theft Auto V Enhanced", true)]
    [InlineData("Rainbow Six Siege", "Tom Clancy's Rainbow Six® Siege", true)]
    [InlineData("God of War Ragnarök", "God of War Ragnarok", true)]
    [InlineData("Minecraft", "Minecraft Dungeons", false)]
    [InlineData("Dota 2", "Dota Underlords", false)]
    [InlineData("Dota 2", "", false)]
    public void GameNamesMatchAfterNormalisation(string catalog, string session, bool same) => Assert.Equal(same, FpsAccuracy.SameGame(catalog, session));

    [Fact]
    public void WholeSessionIsSummarisedFromTheLog()
    {
        // 60 s: 99 % of the samples at 60 FPS (16.7 ms), 1 % stutter at 20 FPS (50 ms).
        var stats = MangoHudSession.Summarize(Log(60, i => i % 100 == 0 ? (20, 50) : (60, 16.7)));
        Assert.NotNull(stats); Assert.Equal(601, stats!.Samples); Assert.Equal(60, stats.DurationSeconds, 1);
        Assert.InRange(stats.AvgFps, 59, 60); Assert.InRange(stats.Low1Fps, 19.9, 20.1); Assert.Equal("NVIDIA GeForce 940MX", stats.Gpu);
        var s = MangoHudSession.ToSession(stats, " Counter-Strike 2 ", new DateTimeOffset(2026, 11, 7, 20, 0, 0, TimeSpan.Zero), "1920x1080")!;
        Assert.Equal("Counter-Strike 2", s.Game); Assert.Equal(60, s.DurationSeconds); Assert.Equal("1920x1080", s.Resolution); Assert.True(s.IsValid);
        Assert.Null(MangoHudSession.ToSession(stats, "Game", DateTimeOffset.UtcNow, "default")!.Resolution);
    }

    [Fact]
    public void RealMangoHudLogIsTooShortToCount()
    {
        // Real glxgears log (~5.7 s): parsed, but a session shorter than 30 s is not recorded.
        var stats = MangoHudSession.Summarize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mangohud-glxgears.csv")));
        Assert.NotNull(stats); Assert.InRange(stats!.AvgFps, 55, 65); Assert.InRange(stats.DurationSeconds, 5, 6); Assert.Equal("Fixture GPU", stats.Gpu);
        Assert.Null(MangoHudSession.ToSession(stats, "glxgears", DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void EmptyOrShortOrBrokenLogsDoNotCount()
    {
        Assert.Null(MangoHudSession.Summarize(""));
        Assert.Null(MangoHudSession.Summarize("os,cpu,gpu\nx,y,z\nfps,frametime,elapsed\n"));
        Assert.Null(MangoHudSession.Summarize(Log(60, _ => (0, 16.7)))); // no frames
        Assert.Null(MangoHudSession.ToSession(MangoHudSession.Summarize(Log(29, _ => (60, 16.7))), "Game", DateTimeOffset.UtcNow, null));
        Assert.NotNull(MangoHudSession.ToSession(MangoHudSession.Summarize(Log(31, _ => (60, 16.7))), "Game", DateTimeOffset.UtcNow, null));
        Assert.Null(MangoHudSession.ToSession(MangoHudSession.Summarize(Log(60, _ => (60, 16.7))), "  ", DateTimeOffset.UtcNow, null));
        Assert.Null(MangoHudSession.Summarize(Log(60, _ => (60, 16.7), "evil\u0007gpu" + new string('x', 200)))!.Gpu);
    }

    [Fact]
    public void SettingsKeepAtMostFiftyValidSessionsNewestFirst()
    {
        var settings = new IlmenyxSettings(); var t0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 60; i++) settings.AddFpsSession(new FpsSession("Game " + i, t0.AddHours(i), 60 + i, 40, 120, null, null));
        settings.AddFpsSession(new FpsSession("Short", t0, 60, 40, 10, null, null)); // < 30 s: ignored
        Assert.Equal(FpsSession.MaxEntries, settings.FpsSessions.Count); Assert.Equal("Game 59", settings.FpsSessions[0].Game); Assert.DoesNotContain(settings.FpsSessions, s => s.Game == "Short");
        string path = Path.Combine(root, "cfg", "settings.json"); settings.Save(path);
        var back = IlmenyxSettings.Load(path); Assert.Equal(50, back.FpsSessions.Count); Assert.Equal(119, back.FpsSessions[0].AvgFps);
        File.WriteAllText(path, "{\"FpsSessions\":[{\"Game\":null,\"At\":\"2026-11-01T00:00:00+00:00\",\"AvgFps\":60,\"Low1Fps\":1,\"DurationSeconds\":99},{\"Game\":\"X\",\"At\":\"2026-11-01T00:00:00+00:00\",\"AvgFps\":-1,\"Low1Fps\":1,\"DurationSeconds\":99}]}");
        Assert.Empty(IlmenyxSettings.Load(path).FpsSessions); // hand-edited junk is dropped
    }

    [Fact]
    public void MeasuredSessionTakesPriorityOverTheEstimate()
    {
        var rows = GameCatalog.Games.Select(g => new GameFpsRow(g)).ToList();
        var at = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var sessions = new List<FpsSession>
        {
            new("Counter-Strike 2", at.AddDays(-1), 40, 22, 600, "NVIDIA GeForce 940MX", null),
            new("COUNTER-STRIKE 2", at, 47.6, 31.2, 2520, "NVIDIA GeForce 940MX", "1920x1080"),
        };
        GameFpsRow.RefreshLocal(rows, 100, 100, "1920x1080", "Alto", "Native", FpsAccuracy.Assess(HardwareMatch.Desktop, 100, false, 100), sessions);
        var cs2 = rows.Single(r => r.Name == "Counter-Strike 2");
        Assert.True(cs2.IsMeasured); Assert.Equal("MEDIDO NESTE PC", cs2.Source); Assert.Equal("48", cs2.FpsText); Assert.Equal("1% low 31", cs2.LowText);
        Assert.Equal($"última sessão {at.ToLocalTime():dd/MM} · 42 min", cs2.Detail);
        Assert.StartsWith("Medido neste PC: 48 FPS (1% low 31) · última sessão", cs2.Summary); Assert.Contains("GPU NVIDIA GeForce 940MX", cs2.Summary);
        Assert.All(rows.Where(r => r.Name != "Counter-Strike 2"), r => Assert.Equal("ESTIMATIVA", r.Source));
        // Unknown hardware: no estimate, but the measured session is still shown.
        foreach (var r in rows) r.Set(0, 0, "SEM ESTIMATIVA", "");
        GameFpsRow.ApplyMeasured(rows, sessions); Assert.Equal("48", cs2.FpsText); Assert.Equal("—", rows.Single(r => r.Name == "Dota 2").FpsText);
    }

    private static FpsContext Ctx(string vendor = "10de", string device = "134d", string driver = "nouveau", bool integrated = false, ulong vram = 0, string? igpu = "HD Graphics 620", bool? battery = false, int prime = 0, int profiles = 0) =>
        new("GM108M [GeForce 940MX]", vendor, device, driver, integrated, vram, igpu, battery, prime, profiles);

    [Fact]
    public void AlertsComeOnlyFromWhatWasRead()
    {
        // This Dell on nouveau, on AC, no PRIME profile yet: nouveau (pre-Turing, no Vulkan) + hybrid; VRAM unknown on nouveau → no VRAM alert.
        var dell = FpsAlerts.Build(Ctx());
        Assert.Equal(["Driver nouveau na GeForce", "Notebook com duas GPUs"], dell.Select(a => a.Title));
        Assert.Contains("Vulkan", dell[0].Text); Assert.Contains("HD Graphics 620", dell[1].Text); Assert.Contains("Nenhum perfil", dell[1].Text); Assert.EndsWith("para a 940MX.", dell[1].Text);
        // Proprietary driver: 2 GB from nvidia-smi → VRAM alert; on battery; one PRIME profile of three.
        var nv = FpsAlerts.Build(Ctx(driver: "nvidia", vram: 2UL << 30, battery: true, prime: 1, profiles: 3));
        Assert.Equal(["2 GB de VRAM", "Notebook na bateria", "Notebook com duas GPUs"], nv.Select(a => a.Title));
        Assert.Contains("1 de 3 perfis", nv[2].Text);
        // Turing on nouveau: slower, but the Vulkan sentence is not claimed.
        Assert.DoesNotContain("Vulkan", FpsAlerts.Build(Ctx(device: "1f95", igpu: null))[0].Text);
        // Desktop with a big card, unknown battery: nothing to warn about. Integrated GPU: no VRAM / hybrid alert.
        Assert.Empty(FpsAlerts.Build(Ctx(vendor: "1002", device: "747e", driver: "amdgpu", vram: 16UL << 30, igpu: null, battery: null)));
        Assert.Empty(FpsAlerts.Build(Ctx(vendor: "8086", device: "5916", driver: "i915", integrated: true, vram: 256UL << 20, igpu: null)));
        Assert.Equal("GPU sem driver", FpsAlerts.Build(Ctx(driver: "", igpu: null))[0].Title);
    }

    [Fact]
    public void BatteryStateFromPowerSupply()
    {
        BatteryInput B(string status) => new("BAT0", null, null, null, null, 80, status, null, null, null, null, null);
        Assert.Null(FpsAlerts.OnBattery([], true));
        Assert.True(FpsAlerts.OnBattery([B("Discharging")], null));
        Assert.True(FpsAlerts.OnBattery([B("Full")], false));
        Assert.False(FpsAlerts.OnBattery([B("Charging")], true));
        Assert.Null(FpsAlerts.OnBattery([B("Unknown")], null));
        Assert.True(FpsAlerts.NvidiaPreTuring("10de", "134d")); Assert.False(FpsAlerts.NvidiaPreTuring("10de", "2786")); Assert.False(FpsAlerts.NvidiaPreTuring("1002", "134d"));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void SessionReaderPicksTheLargestLogWrittenSinceLaunch()
    {
        var since = DateTimeOffset.UtcNow;
        string old = Path.Combine(root, "old_2026.csv"); File.WriteAllText(old, Log(600, _ => (300, 3.3)));
        File.SetLastWriteTimeUtc(old, since.UtcDateTime.AddHours(-1)); // previous session: ignored
        File.WriteAllText(Path.Combine(root, "launcher.csv"), Log(40, _ => (30, 33.3)));
        File.WriteAllText(Path.Combine(root, "game.csv"), Log(120, _ => (75, 13.3)));
        var stats = MangoHudLogReader.ReadSession(root, since);
        Assert.NotNull(stats); Assert.InRange(stats!.AvgFps, 74.9, 75.1); Assert.Equal(1201, stats.Samples);
        Assert.Null(MangoHudLogReader.ReadSession(Path.Combine(root, "missing"), since));
        Assert.Null(MangoHudLogReader.ReadSession(root, since.AddHours(1)));
    }

    [Fact]
    public void SessionOnVsyncIsMarkedCappedAndDoesNotAdjustTheEstimate()
    {
        // vsync at 60 Hz: almost every sample at 59-60, a few just above (real SuperTuxKart log: 2 % above 61.8).
        var capped = MangoHudSession.Summarize(Log(60, i => i % 50 == 0 ? (62.5, 16.0) : i % 7 == 0 ? (58.8, 17.0) : (60, 16.7)))!;
        Assert.Equal(60, capped.CappedAt);
        // GPU-bound around 60: the scene moves it between 50 and 70, so it is not a cap.
        var free = MangoHudSession.Summarize(Log(60, i => i % 2 == 0 ? (50, 20) : (70, 14.3)))!;
        Assert.Null(free.CappedAt);
        Assert.Null(MangoHudSession.Summarize(Log(60, _ => (52, 19.2)))!.CappedAt); // steady but no usual limit near 52
        var session = MangoHudSession.ToSession(capped, "SuperTuxKart", DateTimeOffset.UnixEpoch, "1920x1080", "Alto")!;
        Assert.Equal(60, session.CappedAt);
        var row = new GameFpsRow(GameCatalog.Games[0]); row.SetMeasured(session);
        Assert.Contains("limitado a 60 FPS", row.Summary);
    }
}
