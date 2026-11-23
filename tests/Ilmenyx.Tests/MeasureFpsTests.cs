// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

/// <summary>"Medir FPS" (MangoHud escondido, só para o log), perfil de cada lançamento e o texto de Parar acompanhamento. Nada aqui abre jogo nem MangoHud.</summary>
public sealed class MeasureFpsTests
{
    private static string? Find(string n) => "/usr/bin/" + n;

    [Fact]
    public void MeasureIsOnByDefaultAndOldProfilesGetIt()
    {
        Assert.True(new GameProfile().MeasureFps);
        var old = System.Text.Json.JsonSerializer.Deserialize<GameProfile>("{\"MangoHud\":false,\"Res\":\"1280x720\"}")!;
        Assert.True(old.MeasureFps);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<GameProfile>("{\"MeasureFps\":false}")!.MeasureFps);
    }

    [Fact]
    public void CloneIsIndependentOfTheProfileTheUserKeepsEditing()
    {
        var p = new GameProfile { MangoHud = true, GfxPreset = "Alto", Helpers = [new() { Path = "/bin/x" }] };
        var c = p.Clone();
        p.MangoHud = false; p.GfxPreset = "Baixo"; p.Helpers.Clear();
        Assert.True(c.MangoHud); Assert.Equal("Alto", c.GfxPreset); Assert.Single(c.Helpers);
    }

    [Fact]
    public void MeasureOnlyInjectsMangoHudWithTheLogOnlyConfig()
    {
        var cmd = LinuxGameLauncher.BuildManual("/g/game", new(), "", "AMD", Find, "/cfg/mangohud-medir.conf", measureOnly: true);
        Assert.Equal("/usr/bin/mangohud", cmd.Program);
        Assert.Equal(["/g/game"], cmd.Arguments);
        Assert.Contains(new KeyValuePair<string, string>("MANGOHUD_CONFIGFILE", "/cfg/mangohud-medir.conf"), cmd.Environment);
        // Not asked: the command stays as it was (the default profile also has MeasureFps on; the launcher decides, not the builder).
        var plain = LinuxGameLauncher.BuildManual("/g/game", new(), "", "AMD", Find, "/cfg/mangohud-medir.conf");
        Assert.Equal("/g/game", plain.Program); Assert.Empty(plain.Environment);
        // Without a mangohud on this PC nothing is wrapped.
        Assert.Equal("/g/game", LinuxGameLauncher.BuildManual("/g/game", new(), "", "AMD", _ => null, "/cfg/m.conf", measureOnly: true).Program);
    }

    [Fact]
    public void MeasureOnlyWrapsAFlatpakGameWithTheExtension()
    {
        var cmd = LinuxGameLauncher.BuildManual("/usr/bin/flatpak", new(), "run org.exemplo.Jogo", "AMD", Find, "/home/u/.config/ilmenyx/mangohud-medir.conf",
            flatpakCommand: _ => "jogo-bin", mangoLogFolder: "/home/u/.local/share/ilmenyx/mangohud", measureOnly: true);
        Assert.Contains("--env=MANGOHUD_CONFIGFILE=/home/u/.config/ilmenyx/mangohud-medir.conf", cmd.Arguments);
        Assert.Contains("--command=" + FlatpakLaunch.MangoHudScript, cmd.Arguments);
    }

    [Fact]
    public void LogOnlyConfigHidesTheOverlayAndLogsToTheIlmenyxFolder()
    {
        string text = MangoHudConfig.BuildMeasureOnly("/home/u/.local/share/ilmenyx/mangohud");
        Assert.Contains("no_display=1\n", text); Assert.Contains("output_folder=/home/u/.local/share/ilmenyx/mangohud\n", text); Assert.Contains("autostart_log=1\n", text);
        Assert.DoesNotContain("position=", text); Assert.DoesNotContain("toggle_hud", text);
        Assert.Throws<ArgumentException>(() => MangoHudConfig.BuildMeasureOnly("relativo"));
        Assert.Throws<ArgumentException>(() => MangoHudConfig.BuildMeasureOnly("/x\nno_display=0"));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void MeasureConfigIsOurOwnFileAndIsWrittenOnce()
    {
        string dir = Directory.CreateTempSubdirectory("ilx-measure").FullName;
        try
        {
            string user = Path.Combine(dir, "MangoHud.conf"); File.WriteAllText(user, "fps_only\n");
            string path = Path.Combine(dir, "mangohud-medir.conf"), logs = Path.Combine(dir, "logs");
            Assert.Equal(path, MangoHudLogReader.EnsureMeasureConfig(path, logs));
            var first = File.GetLastWriteTimeUtc(path);
            Assert.Contains("no_display=1", File.ReadAllText(path)); Assert.True(Directory.Exists(logs));
            Thread.Sleep(20); MangoHudLogReader.EnsureMeasureConfig(path, logs);
            Assert.Equal(first, File.GetLastWriteTimeUtc(path));
            Assert.Equal("fps_only\n", File.ReadAllText(user));
            Assert.NotEqual(MangoHudLogReader.ConfigPath(), MangoHudLogReader.MeasureConfigPath());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PruneKeepsTheNewestLogsAndNothingElse()
    {
        string dir = Directory.CreateTempSubdirectory("ilx-prune").FullName;
        try
        {
            for (int i = 0; i < 5; i++) { string f = Path.Combine(dir, $"jogo_{i}.csv"); File.WriteAllText(f, "x"); File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(-i)); }
            File.WriteAllText(Path.Combine(dir, "nota.txt"), "x");
            MangoHudLogReader.Prune(dir, keep: 2);
            Assert.Equal(["jogo_0.csv", "jogo_1.csv"], Directory.GetFiles(dir, "*.csv").Select(Path.GetFileName).Order());
            Assert.True(File.Exists(Path.Combine(dir, "nota.txt")));
            MangoHudLogReader.Prune(Path.Combine(dir, "nao-existe"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StopTrackingListsOnlyWhatReallyStops()
    {
        var plain = StopTracking.Explain("Jogo", new GameProfile { MeasureFps = false }, true, false, false);
        Assert.DoesNotContain("DESEMPENHO MEDIDO", plain); Assert.DoesNotContain("energia", plain); Assert.DoesNotContain("conquista", plain);
        Assert.Contains("O jogo não é fechado", plain);
        var full = StopTracking.Explain("Jogo", new GameProfile { HighPerfPlan = true, MangoHud = true, Helpers = [new() { Path = "/bin/x", CloseWithGame = true }] }, true, true, true);
        Assert.Contains("DESEMPENHO MEDIDO", full); Assert.Contains("conquista", full); Assert.Contains("perfil de energia volta", full); Assert.Contains("auxiliares", full); Assert.Contains("continua aparecendo", full);
        Assert.Contains("escondido", StopTracking.Explain("Jogo", new GameProfile(), true, false, true));
        var watchOnly = StopTracking.Explain("Jogo", new GameProfile { HighPerfPlan = true }, false, true, true);
        Assert.Contains("conquista", watchOnly); Assert.DoesNotContain("energia", watchOnly); Assert.DoesNotContain("DESEMPENHO", watchOnly);
        Assert.DoesNotContain("auxiliares", StopTracking.Explain("Jogo", new GameProfile { Helpers = [new() { Path = "/bin/x", CloseWithGame = false }] }, true, false, false));
    }
}
