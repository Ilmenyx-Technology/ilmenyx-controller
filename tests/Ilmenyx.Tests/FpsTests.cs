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
