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
    }
}
