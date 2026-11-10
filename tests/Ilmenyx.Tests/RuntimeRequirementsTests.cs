// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Cli;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class RuntimeRequirementsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void SharedPresentationPreservesCliOutput(bool libc, bool icu, bool invariant)
    {
        var probe = new LinuxRuntimeDependencies(name => name == "libc.so.6" && libc ||
            icu && name is "libicuuc.so" or "libicui18n.so");
        var snapshot = probe.InspectReport(invariant, true, false);
        using var shared = new StringWriter(CultureInfo.InvariantCulture);
        using var cli = new StringWriter(CultureInfo.InvariantCulture);
        int result = RuntimeRequirementsPresentation.Write(snapshot, shared);
        Assert.Equal(DoctorCommand.Run(probe, cli, invariant, true, false), result);
        Assert.Equal(cli.ToString(), shared.ToString());
        Assert.Equal(libc && (icu || invariant), snapshot.CliReady);
        Assert.Contains("DISPLAY: definida", shared.ToString());
        Assert.Contains("WAYLAND_DISPLAY: ausente", shared.ToString());
    }

    [Fact]
    public void PresentationUsesSnapshotWithoutFurtherProbes()
    {
        int calls = 0;
        var probe = new LinuxRuntimeDependencies(_ => { calls++; return true; });
        var snapshot = probe.InspectReport(false, false, true);
        int afterProbe = calls;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        RuntimeRequirementsPresentation.Write(snapshot, output);
        RuntimeRequirementsPresentation.Write(snapshot, output);
        Assert.Equal(afterProbe, calls);
        Assert.True(afterProbe > 0);
        Assert.Contains("nao certificam ABI", output.ToString());
    }
}
