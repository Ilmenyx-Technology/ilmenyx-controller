// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class FpsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-fps-" + Guid.NewGuid().ToString("N"));
    public FpsTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void RowsRateAndEstimateLocallyLikeTheOriginal()
    {
        var rows = GameCatalog.Games.Select(g => new GameFpsRow(g)).ToList();
        var medium = FpsAccuracy.Assess(HardwareMatch.Desktop, 100, false, 100);
        GameFpsRow.RefreshLocal(rows, 100, 100, "1920x1080", "Alto", "Native", medium);
        // The local estimate is a range around the central value (344: CS2's measured 1080p High curve at index 100, refit of 2026-11-10), never a single number.
        var cs2 = rows.Single(r => r.Name == "Counter-Strike 2"); Assert.Equal(344, cs2.Fps); Assert.Equal("292–396", cs2.FpsText); Assert.Equal("EXCELENTE", cs2.Rating); Assert.Equal("ESTIMATIVA", cs2.Source); Assert.Equal("gargalo: GPU", cs2.Detail); Assert.Equal("confiança média", cs2.LowText);
        Assert.Contains("não é medição", cs2.Summary);
        var elden = rows.Single(r => r.Name == "Elden Ring"); Assert.Equal("trava do jogo em 60 FPS", elden.Detail); Assert.Equal("JOGÁVEL", elden.Rating); Assert.Equal("51–60", elden.FpsText); // never above the engine cap
        cs2.Set(0, 0, "x", ""); Assert.Equal("—", cs2.FpsText); Assert.Equal("", cs2.Rating); Assert.Equal(0, cs2.Bar);
        cs2.Set(95.4, 60, GameFpsRow.SourceMeasured, ""); Assert.Equal("ÓTIMO", cs2.Rating); Assert.Equal("1% low 60", cs2.LowText); Assert.InRange(cs2.Bar, 0.39, 0.4);
        foreach (var (fps, rating) in new[] { (29.0, "INJOGÁVEL"), (30.0, "FRACO"), (59.9, "FRACO"), (60.0, "JOGÁVEL"), (90.0, "ÓTIMO"), (144.0, "EXCELENTE") }) { cs2.Set(fps, 0, "", ""); Assert.Equal(rating, cs2.Rating); }
    }

    [Fact]
    public void OldSettingsWithTheRemovedHowManyFpsKeyLoadAndDropItOnTheNextSave()
    {
        // HowManyFPS was removed on 2026-11-08; files saved before still carry "HowManyFpsKey". They must load normally (nothing else lost)
        // and the key must be gone after the next write.
        string path = Path.Combine(root, "cfg/settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"HowManyFpsKey": "secret-token", "ThemeLight": true, "FpsSessions": [{"Game": "Dota 2", "At": "2026-11-06T21:00:00+00:00", "AvgFps": 87.4, "Low1Fps": 52, "DurationSeconds": 1800, "Gpu": null, "Resolution": null}]}""");
        var loaded = IlmenyxSettings.Load(path);
        Assert.True(loaded.ThemeLight); Assert.Equal("Dota 2", Assert.Single(loaded.FpsSessions).Game);
        IlmenyxSettings.Update(path, s => s.DeviceToasts = false);
        string text = File.ReadAllText(path);
        Assert.DoesNotContain("HowManyFpsKey", text); Assert.DoesNotContain("secret-token", text);
        Assert.True(IlmenyxSettings.Load(path).ThemeLight);
        if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }
}
