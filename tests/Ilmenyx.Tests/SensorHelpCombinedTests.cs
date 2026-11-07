// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Diagnostics;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class SensorHelpCombinedTests
{
    private static SensorFix Fix(string? apply) => new("t", "w", [], Apply: apply);

    [Fact]
    public void NothingToApplyGivesNoScript()
    {
        Assert.Null(SensorHelp.Combined([]));
        Assert.Null(SensorHelp.Combined([Fix(null), Fix("")]));
    }

    [Fact]
    public void SingleFixIsUnchanged() => Assert.Equal("modprobe coretemp", SensorHelp.Combined([Fix(null), Fix("modprobe coretemp")]));

    [Fact]
    public void RealFixesCombineIntoOneScriptThatOnlyUsesTheirFixedText()
    {
        var fixes = SensorHelp.Build(true, CpuPowerAccess.NoPermission, "Intel(R) Core(TM) i7-7500U", "/sys/devices/virtual/powercap/intel-rapl/intel-rapl:0", false);
        string script = SensorHelp.Combined(fixes)!;
        Assert.StartsWith("rc=0; (", script);
        Assert.EndsWith("exit $rc", script);
        foreach (var f in fixes) Assert.Contains("(" + f.Apply + ") || rc=1; ", script);
    }

    [Theory]
    [InlineData("true", "true", 0)]
    [InlineData("false", "true", 1)]
    [InlineData("true", "false", 1)]
    public void AFailureDoesNotStopTheNextFixAndIsReported(string first, string second, int expected)
    {
        string marker = Path.Combine(Path.GetTempPath(), "ilmenyx-combined-" + Guid.NewGuid());
        string script = SensorHelp.Combined([Fix(first), Fix(second), Fix("touch '" + marker + "'")])!;
        var p = Process.Start(new ProcessStartInfo("/bin/sh", ["-c", script]) { RedirectStandardOutput = true })!;
        p.WaitForExit();
        Assert.Equal(expected, p.ExitCode);
        Assert.True(File.Exists(marker), "the fix after a failing one must still run");
        File.Delete(marker);
    }
}
