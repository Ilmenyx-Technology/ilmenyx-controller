// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

// Fixtures sintéticos de /proc e /sys; nada é lido do host.
public sealed class LinuxUsageProviderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-usage-" + Guid.NewGuid().ToString("N"));
    public LinuxUsageProviderTests() { Directory.CreateDirectory(Path.Combine(root, "proc/net")); Directory.CreateDirectory(Path.Combine(root, "sys/class/hwmon")); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private string Proc(string p) => Path.Combine(root, "proc", p);
    private LinuxUsageProvider Provider() => new(Path.Combine(root, "proc"), Path.Combine(root, "sys"));

    private void Write(long total, long idle, long sectorsRead, long sectorsWritten, long rx, long tx, long ticks)
    {
        File.WriteAllText(Proc("stat"), $"cpu  {total - idle} 0 0 {idle} 0 0 0 0 0 0\ncpu0 1 0 0 1 0 0 0 0 0 0\ncpu1 1 0 0 1 0 0 0 0 0 0\n");
        File.WriteAllText(Proc("meminfo"), "MemTotal:       8000000 kB\nMemFree: 100 kB\nMemAvailable:   6000000 kB\n");
        File.WriteAllText(Proc("cpuinfo"), "processor\t: 0\nmodel name\t: Fixture CPU\ncpu MHz\t\t: 1000.0\n\nprocessor\t: 1\ncpu MHz\t\t: 3000.0\n");
        File.WriteAllText(Proc("diskstats"), $"   8       0 sda 1 0 {sectorsRead} 0 1 0 {sectorsWritten} 0 0 0 0\n   8       1 sda1 1 0 99999 0 1 0 99999 0 0 0 0\n 259 0 nvme0n1 0 0 0 0 0 0 0 0 0 0 0\n   7 0 loop0 1 0 99999 0 1 0 99999 0 0 0 0\n");
        File.WriteAllText(Proc("net/dev"), $"Inter-|   Receive\n face |bytes\n    lo: 999 0 0 0 0 0 0 0 999 0 0 0 0 0 0 0\n  eth0: {rx} 0 0 0 0 0 0 0 {tx} 0 0 0 0 0 0 0\ndocker0: 5 0 0 0 0 0 0 0 5 0 0 0 0 0 0 0\n");
        Directory.CreateDirectory(Proc("42"));
        File.WriteAllText(Proc("42/stat"), $"42 (my (odd) app) S 1 1 1 0 -1 0 0 0 0 0 {ticks} 0 0 0 20 0 1 0 777 1000 25 0 0 0\n");
        Directory.CreateDirectory(Proc("self")); // not numeric: must be ignored
    }

    [Fact]
    public async Task FirstSampleHasNoRatesAndSecondComputesDeltas()
    {
        Write(1000, 800, 100, 200, 1000, 2000, 10);
        var provider = Provider();
        var first = await provider.SampleAsync();
        Assert.Null(first.CpuPercent); Assert.Null(first.DiskReadBps); Assert.Null(first.NetRxBps);
        Assert.Equal("Fixture CPU", first.CpuModel);
        Assert.Equal(2000.0, first.CpuMhz);
        Assert.Equal(8000000L * 1024, first.MemoryTotal);
        Assert.Equal(2000000L * 1024, first.MemoryUsed);
        Assert.Equal("eth0", first.NetInterface);
        await Task.Delay(120);
        Write(2000, 1500, 300, 400, 5000, 3000, 30);
        var second = await provider.SampleAsync();
        Assert.Equal(30.0, second.CpuPercent!.Value, 3); // 300 busy of 1000 total
        Assert.True(second.DiskReadBps > 0 && second.DiskWriteBps > 0);
        Assert.True(second.NetRxBps > second.NetTxBps);
        var process = Assert.Single(second.Processes);
        Assert.Equal(42, process.Pid);
        Assert.Equal("my (odd) app", process.Name);
        Assert.Equal(25L * Environment.SystemPageSize, process.ResidentBytes);
        Assert.True(process.CpuPercent > 0);
        Assert.Equal(1, second.ProcessCount);
    }

    [Fact]
    public async Task CountersGoingBackwardsDoNotProduceNegativeRates()
    {
        Write(2000, 1500, 300, 400, 5000, 3000, 30);
        var provider = Provider();
        await provider.SampleAsync();
        await Task.Delay(50);
        Write(1000, 800, 100, 200, 1000, 2000, 10);
        var second = await provider.SampleAsync();
        Assert.Null(second.CpuPercent); Assert.Null(second.DiskReadBps); Assert.Null(second.NetRxBps);
    }

    [Fact]
    public async Task MissingSourcesYieldNullsWithoutThrowing()
    {
        var sample = await Provider().SampleAsync();
        Assert.Null(sample.CpuPercent); Assert.Null(sample.MemoryTotal); Assert.Null(sample.NetInterface); Assert.Empty(sample.Processes);
        Assert.Null(sample.CpuTempC);
    }

    [Fact]
    public async Task CpuTemperaturePrefersPackageLabelAndRejectsInsaneValues()
    {
        string chip = Path.Combine(root, "sys/class/hwmon/hwmon0"); Directory.CreateDirectory(chip);
        File.WriteAllText(Path.Combine(chip, "name"), "coretemp\n");
        File.WriteAllText(Path.Combine(chip, "temp1_input"), "99000\n"); File.WriteAllText(Path.Combine(chip, "temp1_label"), "Core 0\n");
        File.WriteAllText(Path.Combine(chip, "temp2_input"), "47000\n"); File.WriteAllText(Path.Combine(chip, "temp2_label"), "Package id 0\n");
        File.WriteAllText(Path.Combine(chip, "temp3_input"), "999999\n");
        string other = Path.Combine(root, "sys/class/hwmon/hwmon1"); Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "name"), "nvme\n"); File.WriteAllText(Path.Combine(other, "temp1_input"), "10000\n");
        Write(1000, 800, 1, 1, 1, 1, 1);
        Assert.Equal(47.0, (await Provider().SampleAsync()).CpuTempC);
    }

    [Theory]
    [InlineData("1 (x) S 1 1 1 0 -1 0 0 0 0 0 5 5 0 0 20 0 1 0 9 100 3 0 0")]
    public void ParseStatReadsUtimeStimeStartAndRss(string text)
    {
        var s = LinuxUsageProvider.ParseStat(text);
        Assert.NotNull(s); Assert.Equal(10, s!.Value.ticks); Assert.Equal(9, s.Value.start); Assert.Equal(3, s.Value.rssPages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("1 (x) S 1 2")]
    public void ParseStatRejectsMalformed(string text) => Assert.Null(LinuxUsageProvider.ParseStat(text));

    [Fact]
    public async Task CancellationIsHonoured()
    {
        Write(1000, 800, 1, 1, 1, 1, 1);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().SampleAsync(cts.Token));
    }
}
