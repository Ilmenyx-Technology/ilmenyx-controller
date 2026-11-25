// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Desktop;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class GamesWindowLogicTests
{
    [Theory]
    [InlineData("launcher")] [InlineData("achievements")] [InlineData("compat")] [InlineData("gamecheck")] [InlineData("canrun")]
    public void TabForKnownPages(string key) => Assert.Equal(key, GamesWindow.TabFor(key));

    [Theory]
    [InlineData("optimize")] [InlineData("nvdriver")] [InlineData("bottleneck")] [InlineData("")]
    public void TabForFullAppOnlyPagesIsNull(string key) => Assert.Null(GamesWindow.TabFor(key));

    [Fact]
    public void ReadsCpuModelAndRamFromProcFixtures()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ilx-gw-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "cpuinfo"), "processor\t: 0\nmodel name\t: Test CPU 9000\n\nprocessor\t: 1\nmodel name\t: Test CPU 9000\n");
            File.WriteAllText(Path.Combine(dir, "meminfo"), "MemTotal:       16384 kB\nMemAvailable:    8192 kB\n");
            Assert.Equal("Test CPU 9000", LinuxMemoryInfo.CpuModel(Path.Combine(dir, "cpuinfo")));
            Assert.Equal(16384L * 1024, LinuxMemoryInfo.TotalBytes(Path.Combine(dir, "meminfo")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingProcFilesGiveEmptyNotInvented()
    {
        Assert.Equal("", LinuxMemoryInfo.CpuModel("/nonexistent/cpuinfo"));
        Assert.Equal(0, LinuxMemoryInfo.TotalBytes("/nonexistent/meminfo"));
    }
}
