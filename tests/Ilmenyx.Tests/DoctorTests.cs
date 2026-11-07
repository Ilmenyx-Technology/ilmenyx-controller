// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Cli;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class DoctorTests
{
    [Fact]
    public void DoctorParsesWithoutInventoriesOrWorkloads()
    {
        var args = CliArguments.Parse(["doctor"]);
        Assert.Equal(CliOperation.Doctor, args.Operation);
        Assert.Null(args.Benchmark); Assert.Null(args.Memory); Assert.Null(args.Simulation);
        Assert.Null(args.Output); Assert.False(args.Json); Assert.False(args.Html);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--html")]
    [InlineData("--output")]
    [InlineData("--allow-cpu-load")]
    [InlineData("--device")]
    public void UnsupportedFlagsAreRejectedBeforeAnyDiagnostics(string flag) =>
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["doctor", flag]));

    [Theory]
    [InlineData(false, false, false, 5)]
    [InlineData(true, false, false, 5)]
    [InlineData(true, false, true, 0)]
    [InlineData(true, true, false, 0)]
    [InlineData(false, true, true, 5)]
    public void ExitCodeReflectsCliRequirementsNotOptionalGraphics(bool glibc, bool icu, bool invariant, int expected)
    {
        var dependencies = new LinuxRuntimeDependencies(name => name == "libc.so.6" && glibc ||
            icu && name is "libicuuc.so" or "libicui18n.so");
        using var output = new StringWriter();
        Assert.Equal(expected, DoctorCommand.Run(dependencies, output, invariant, false, false));
        Assert.Contains("GUI nao testada", output.ToString());
        Assert.Contains("fontconfig: nao encontrada/carregavel", output.ToString());
        Assert.Contains("DISPLAY: ausente", output.ToString());
    }

    [Fact]
    public void DisplayVariablesAndLibrariesDoNotClaimWorkingUi()
    {
        using var output = new StringWriter();
        Assert.Equal(0, DoctorCommand.Run(new LinuxRuntimeDependencies(_ => true), output, false, true, true));
        Assert.Contains("WAYLAND_DISPLAY: definida", output.ToString());
        Assert.Contains("nao certificam", output.ToString());
        Assert.Contains("GUI nao testada", output.ToString());
    }
}
