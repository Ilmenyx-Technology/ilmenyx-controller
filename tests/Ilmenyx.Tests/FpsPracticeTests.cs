// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Synthetic MangoHud logs and settings files (shapes copied from real ones, names anonymised); no game is run.
using System.Globalization;
using System.Text;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class FpsPracticeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-practice-" + Guid.NewGuid().ToString("N"));
    public FpsPracticeTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    /// <summary>Real layout, a row every 100 ms; <paramref name="row"/> gets the elapsed second.</summary>
    private static string Log(double seconds, Func<double, (double Fps, double Ms)> row)
    {
        var sb = new StringBuilder("os,cpu,gpu,ram,kernel,driver,cpuscheduler\nLinux,Some CPU,Mesa Test GPU ,16000000,7.3.0,,powersave\nfps,frametime,cpu_load,elapsed\n");
        for (int i = 1; i <= (int)(seconds * 10); i++) { var (f, ms) = row(i / 10.0); sb.Append(string.Create(CultureInfo.InvariantCulture, $"{f},{ms},10,{100_000_000L * i}\n")); }
        return sb.ToString();
    }

    private static (double, double) Play(double t) => t < 20 ? (20, 50) : (60, 16.7); // loading at 20 FPS, then 60

    [Fact]
    public void LoadingAndTheLastSecondsDoNotCount()
    {
        var s = MangoHudPlay.Summarize(Log(100, Play))!;
        Assert.Equal(60, s.TypicalFps, 1); Assert.Equal("Mesa Test GPU", s.Gpu);
        Assert.InRange(s.ValidSeconds, 76, 77.1); // 100 s - 20 s - 3 s
        Assert.Equal(0, s.Stalls); Assert.Equal(60, s.CappedAt);
    }

    [Fact]
    public void MenuAtHugeFpsDoesNotMoveTheMedian_AndStallsAreCountedApart()
    {
        // 10 s menu at 500 FPS inside 100 s of play at 60, and one 400 ms loading stutter (two rows).
        var s = MangoHudPlay.Summarize(Log(130, t => t is >= 50 and < 60 ? (500, 2) : t is >= 80 and < 80.2 ? (2.5, 400) : (60, 16.7)))!;
        Assert.Equal(60, s.TypicalFps, 1); Assert.Equal(1, s.Stalls);
        Assert.True(s.P90Fps <= 60.1); // simple mean would be 66.7
        Assert.InRange(s.Low1Fps, 59, 61); // the stutter is out of the 1% low too
    }

    [Fact]
    public void SessionNeedsSixtyValidSeconds()
    {
        Assert.Null(MangoHudPlay.Summarize(Log(20 + 55 + 3, Play))); // 55 s of play
        Assert.NotNull(MangoHudPlay.Summarize(Log(20 + 65 + 3, Play)));
        Assert.Null(MangoHudPlay.Summarize(""));
        var s = MangoHudPlay.ToSession(MangoHudPlay.Summarize(Log(120, Play)), " Game ", DateTimeOffset.UnixEpoch, "default", "Alto", null, "Unity nível 2")!;
        Assert.Equal("Game", s.Game); Assert.Null(s.Resolution); Assert.Equal("Alto", s.Preset); Assert.Equal(97, s.ValidSeconds); Assert.True(s.IsValid); Assert.Equal(60, s.AvgFps);
    }

    [Fact]
    public void LogChoiceFollowsTheProcessName()
    {
        Assert.Equal("steamwebhelper", MangoHudPlay.LogPrefix("steamwebhelper_2026-11-10_00-22-20.csv"));
        Assert.True(MangoHudPlay.LogMatches("Hollow Knight.exe_2026-11-10_00-22-20.csv", "Hollow Knight"));
        Assert.True(MangoHudPlay.LogMatches("hollowknight_2026-11-10_00-22-20.csv", "HollowKnight.exe"));
        Assert.True(MangoHudPlay.LogMatches("verylongprocessn_2026-11-10_00-22-20.csv", "VeryLongProcessName")); // comm is cut at 15
        Assert.False(MangoHudPlay.LogMatches("steam_2026-11-10_00-22-20.csv", "hollowknight"));
        Assert.False(MangoHudPlay.LogMatches("nodate.csv", "nodate"));
    }

    [Fact]
    public void ReadPlayIgnoresOtherProcessesAndFallsBackToTheLongest()
    {
        var since = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(root, "steamwebhelper_2026-11-10_00-22-20.csv"), Log(300, _ => (144, 6.9))); // longer, but the launcher
        File.WriteAllText(Path.Combine(root, "supertuxkart_2026-11-10_00-26-50.csv"), Log(120, Play));
        Assert.Equal(60, MangoHudLogReader.ReadPlay(root, since, "SuperTuxKart")!.TypicalFps, 1);
        Assert.Equal(144, MangoHudLogReader.ReadPlay(root, since, "other")!.TypicalFps, 1); // no match: longest
        Assert.Equal(144, MangoHudLogReader.ReadPlay(root, since, null)!.TypicalFps, 1);
        Assert.Null(MangoHudLogReader.ReadPlay(Path.Combine(root, "missing"), since, "x"));
    }

    private static FpsSession S(double fps, double secs, int hoursAgo, string? res = "1920x1080", string? preset = "Alto", string? gpu = "GPU A", double p10 = 0, double p90 = 0, string? note = null) =>
        new("Game", DateTimeOffset.UnixEpoch.AddHours(-hoursAgo), fps, fps / 2, secs, gpu, res, preset, null, null, p10 == 0 ? fps * 0.8 : p10, p90 == 0 ? fps * 1.2 : p90, secs, 0, note);

    [Fact]
    public void HistoryIsAWeightedMedianPerGroupOfTheCurrentGpu()
    {
        Assert.Equal(60, FpsHistory.WeightedMedian([(30, 1), (60, 1), (90, 1)]));
        Assert.Equal(90, FpsHistory.WeightedMedian([(30, 100), (90, 1000)])); // long session weighs more
        Assert.InRange(FpsHistory.WeightedMedian([(102, 166), (63, 162)]), 80, 86); // two similar sessions: between them, not the larger (real case, 2026-11-23)
        var sessions = new List<FpsSession>
        {
            S(100, 600, 1), S(40, 100, 2), S(41, 100, 3), // newest first: 100 FPS for 10 min dominates two short ones
            S(200, 900, 4, gpu: "GPU B"), // old GPU: out
            S(300, 900, 5, res: "2560x1440"), // another group
            new("Game", DateTimeOffset.UnixEpoch, 999, 5, 900, "GPU A", "1920x1080", "Alto"), // old simple-average session: out
            new("Other", DateTimeOffset.UnixEpoch, 5, 5, 900, "GPU A", "1920x1080", "Alto", null, null, 5, 5, 900, 0),
        };
        var g = FpsHistory.Build(sessions, "Game");
        Assert.Equal(2, g.Count);
        Assert.Equal(("1920x1080", "Alto", 100, 3), (g[0].Resolution, g[0].Quality, g[0].TypicalFps, g[0].Sessions));
        Assert.Equal(800.0 / 3600, g[0].Hours, 4); Assert.Equal("2560x1440", g[1].Resolution);
        Assert.Single(FpsHistory.Build(sessions, "Game", "GPU B"));
        Assert.Empty(FpsHistory.Build(sessions, "Nope"));
        var many = Enumerable.Range(0, 15).Select(i => S(i < 10 ? 50 : 500, 300, i)).ToList(); // only the 10 newest
        Assert.Equal((50, 10), (FpsHistory.Build(many, "Game")[0].TypicalFps, FpsHistory.Build(many, "Game")[0].Sessions));
        var note = FpsHistory.Build([S(60, 300, 1, preset: null, note: "Unity nível 2"), S(30, 300, 2, preset: null, note: "Unity nível 0")], "Game");
        Assert.Equal(2, note.Count);
    }

    [Fact]
    public void OldSettingsKeepLoadingAndNewFieldsRoundTrip()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<FpsSession>("""{"Game":"X","At":"2026-11-01T00:00:00+00:00","AvgFps":60,"Low1Fps":1,"DurationSeconds":99}""")!;
        Assert.Null(old.ValidSeconds);
        var s = S(60, 300, 1); Assert.Equal(s, System.Text.Json.JsonSerializer.Deserialize<FpsSession>(System.Text.Json.JsonSerializer.Serialize(s)));
    }

    // ---- quality detection ----
    private const string Ini = "[/Script/Engine.GameUserSettings]\nResolutionSizeX=1920\nResolutionSizeY=1080\nFrameRateLimit=60.000000\nsg.ViewDistanceQuality=2\nsg.AntiAliasingQuality=2\nsg.ShadowQuality=2\nsg.PostProcessQuality=3\nsg.TextureQuality=2\nsg.EffectsQuality=1\nsg.FoliageQuality=2\n";

    [Fact]
    public void UnrealIniGivesPresetResolutionAndLimit()
    {
        var q = GameQualityDetector.ParseUnreal(Ini, "GameUserSettings.ini")!;
        Assert.Equal(("Unreal", "Alto", "Alto", "1920x1080", 60), (q.Engine, q.Preset, q.Level, q.Resolution, q.FpsLimit));
        var spread = GameQualityDetector.ParseUnreal("sg.ViewDistanceQuality=0\nsg.ShadowQuality=1\nsg.TextureQuality=3\nsg.EffectsQuality=4\nFrameRateLimit=0.000000", "f")!;
        Assert.Equal((null, "Personalizado", null), (spread.Preset, spread.Level, spread.FpsLimit));
        Assert.Equal("Cinemático", GameQualityDetector.ParseUnreal("sg.ViewDistanceQuality=4\nsg.ShadowQuality=4", "f")!.Level);
        Assert.Null(GameQualityDetector.ParseUnreal("[x]\nfoo=1\nsg.ShadowQuality=9", "f"));
    }

    [Fact]
    public void UnrealFileIsFoundInTheProtonPrefixByProjectName()
    {
        string prefix = Path.Combine(root, "prefix"), local = Path.Combine(prefix, "pfx", "drive_c", "users", "steamuser", "AppData", "Local");
        foreach (string proj in new[] { "CrashReportClient", "MyGame" })
        {
            Directory.CreateDirectory(Path.Combine(local, proj, "Saved", "Config", "WindowsNoEditor"));
            File.WriteAllText(Path.Combine(local, proj, "Saved", "Config", "WindowsNoEditor", "GameUserSettings.ini"), proj == "MyGame" ? Ini : "sg.ShadowQuality=0\nsg.TextureQuality=0");
        }
        var q = GameQualityDetector.Detect("My Game", "/games/MyGame-Win64-Shipping.exe", [prefix, Path.Combine(prefix, "pfx")], Path.Combine(root, "cfg"))!;
        Assert.Equal("Alto", q.Preset);
        var native = Path.Combine(root, "cfg", "Epic", "Native", "Saved", "Config", "LinuxNoEditor"); Directory.CreateDirectory(native);
        File.WriteAllText(Path.Combine(native, "GameUserSettings.ini"), "sg.ShadowQuality=3\nsg.TextureQuality=3");
        Assert.Equal("Ultra", GameQualityDetector.Detect("Native", null, [], Path.Combine(root, "cfg"))!.Preset);
        Assert.Null(GameQualityDetector.Detect("Nothing", null, [], Path.Combine(root, "empty")));
    }

    [Fact]
    public void UnityRegistryAndPrefsGiveLevelAndResolutionButNoPreset()
    {
        string reg = "WINE REGISTRY Version 2\n\n[Software\\\\Some Studio\\\\Some Game] 1700000000\n#time=1\n\"UnityGraphicsQuality_h1669003810\"=dword:00000002\n\"Screenmanager Resolution Width_h182942802\"=dword:00000780\n\"Screenmanager Resolution Height_h2627697771\"=dword:00000438\n\n[Software\\\\Other\\\\Game] 1\n\"UnityGraphicsQuality_h1\"=dword:00000005\n";
        var q = GameQualityDetector.ParseUnityReg(reg, "[Software\\\\Some Studio\\\\Some Game]", "user.reg")!;
        Assert.Equal(("Unity", null, "nível 2", "1920x1080"), (q.Engine, q.Preset, q.Level, q.Resolution));
        Assert.Null(GameQualityDetector.ParseUnityReg(reg, "[Software\\\\Nobody\\\\Game]", "user.reg"));
        var p = GameQualityDetector.ParseUnityPrefs("<unity_prefs><pref name=\"UnityGraphicsQuality\" type=\"int\">3</pref><pref name=\"Screenmanager Resolution Width\" type=\"int\">2560</pref><pref name=\"Screenmanager Resolution Height\" type=\"int\">1440</pref></unity_prefs>", "prefs")!;
        Assert.Equal(("nível 3", "2560x1440"), (p.Level, p.Resolution));
        var both = GameQualityDetector.ParseUnityPrefs("<unity_prefs><pref name=\"Screenmanager Resolution Width Default\" type=\"int\">800</pref><pref name=\"Screenmanager Resolution Width\" type=\"int\">1920</pref><pref name=\"Screenmanager Resolution Height Default\" type=\"int\">600</pref><pref name=\"Screenmanager Resolution Height\" type=\"int\">1080</pref></unity_prefs>", "prefs")!;
        Assert.Equal("1920x1080", both.Resolution); // the "... Default" keys are not the current resolution

        string game = Path.Combine(root, "game"); Directory.CreateDirectory(Path.Combine(game, "Some Game_Data")); File.WriteAllText(Path.Combine(game, "Some Game_Data", "app.info"), "Some Studio\nSome Game\n");
        Assert.Equal(("Some Studio", "Some Game"), GameQualityDetector.ReadAppInfo(Path.Combine(game, "Some Game.exe")));
        string prefix = Path.Combine(root, "pfx2"); Directory.CreateDirectory(prefix); File.WriteAllText(Path.Combine(prefix, "user.reg"), reg);
        Assert.Equal("nível 2", GameQualityDetector.Detect("Some Game", Path.Combine(game, "Some Game.exe"), [prefix], Path.Combine(root, "cfg"))!.Level);
    }
}
