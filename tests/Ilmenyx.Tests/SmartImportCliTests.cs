// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Cli;
using Xunit;
namespace Ilmenyx.Tests;

public class SmartImportCliTests
{
    [Theory]
    [InlineData("--html", true)]
    [InlineData("--json", false)]
    public void ImportIsSeparateFromDeviceQueriesAndWorkloads(string flag, bool html)
    {
        var command = CliArguments.Parse(["smart-import", "--device", "nvme0n1", flag, "--output", "report.out"]);
        Assert.Equal(CliOperation.SmartImport, command.Operation);
        Assert.Equal("nvme0n1", command.Device); Assert.Equal("report.out", command.Output);
        Assert.Equal(html, command.Html); Assert.Equal(!html, command.Json);
        Assert.Null(command.Benchmark); Assert.Null(command.Memory); Assert.Null(command.Simulation);
    }
    [Theory]
    [InlineData("--allow-device-query")]
    [InlineData("--input")]
    [InlineData("--threads")]
    [InlineData("--allow-cpu-load")]
    public void QueryAndWorkloadFlagsAreRejected(string flag) =>
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["smart-import", "--device", "nvme0n1", flag]));
    [Fact]
    public void ImportRequiresDeviceAndExclusiveFormat()
    {
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["smart-import"]));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["smart-import", "--device", "/dev/sda"]));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["smart-import", "--device", "sda", "--html", "--json"]));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["smart", "--device", "sda", "--allow-device-query", "--html"]));
    }
}
