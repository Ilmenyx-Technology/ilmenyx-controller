// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class MangoHudAutoConfigTests
{
    [Fact]
    public void WrittenWhenMissingWithLoggingAndNeverOverwritten()
    {
        if (!OperatingSystem.IsLinux()) return;
        string dir = Path.Combine(Path.GetTempPath(), "ilx-mh-" + Guid.NewGuid()), path = Path.Combine(dir, "cfg", "mangohud.conf"), logs = Path.Combine(dir, "logs");
        var s = new IlmenyxSettings { OverlayCorner = "top-right" };
        Assert.Equal(path, MangoHudLogReader.EnsureConfig(s, path, logs));
        string text = File.ReadAllText(path);
        Assert.Contains("output_folder=" + logs, text);
        Assert.Contains("top-right", text);
        Assert.True(Directory.Exists(logs));
        File.WriteAllText(path, "user choice");
        Assert.Equal(path, MangoHudLogReader.EnsureConfig(new IlmenyxSettings(), path, logs));
        Assert.Equal("user choice", File.ReadAllText(path));
        Directory.Delete(dir, true);
    }
}
