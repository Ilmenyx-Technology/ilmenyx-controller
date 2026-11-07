// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public class FullBenchTests
{
    [Fact]
    public void MemoryCheckRefusesWhenReserveWouldBeBroken()
    {
        Assert.NotNull(MemoryBench.Check(MemoryBench.RequiredBytes - 1));
        Assert.Null(MemoryBench.Check(MemoryBench.RequiredBytes));
    }

    [Fact]
    public async Task MemoryFullBenchRefusesWithoutRunningWhenMemoryIsLow()
    {
        var r = await MemoryBench.RunAsync(1L << 30, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal(BenchRunStatus.Unavailable, r.Status);
        Assert.Null(r.LatencyNs);
        Assert.Null(r.ReadGBs);
        Assert.NotNull(r.Reason);
    }

    [Fact]
    public async Task MemoryBenchWithSmallBuffersCompletesAndPublishesValues()
    {
        var r = await MemoryBench.RunAsync(0, TimeSpan.FromSeconds(30), CancellationToken.None, null, 1 << 20, 4 << 20, 200_000);
        Assert.Equal(BenchRunStatus.Completed, r.Status);
        Assert.True(r.LatencyNs > 0);
        Assert.True(r.ReadGBs > 0);
        Assert.True(r.CopyGBs > 0);
    }

    [Fact]
    public async Task MemoryBenchCancelledPublishesNoValues()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = await MemoryBench.RunAsync(0, TimeSpan.FromSeconds(30), cts.Token, null, 1 << 20, 4 << 20, 200_000);
        Assert.Equal(BenchRunStatus.Cancelled, r.Status);
        Assert.Null(r.LatencyNs);
        Assert.Null(r.ReadGBs);
        Assert.Null(r.CopyGBs);
    }

    [Theory]
    [InlineData("relativo", 1024, 4, 100_000, true)]
    [InlineData("/dev/sda", 1024, 4, 100_000, true)]
    [InlineData("/dev", 1024, 4, 100_000, true)]
    [InlineData("/proc/x", 1024, 4, 100_000, true)]
    [InlineData("/home/x", 32, 4, 100_000, true)]
    [InlineData("/home/x", 20000, 4, 100_000, true)]
    [InlineData("/home/x", 1024, 0, 100_000, true)]
    [InlineData("/home/x", 1024, 4, 100, true)]
    public void DiskOptionsRejectUnsafeInput(string dir, int mb, int seconds, long freeMb, bool rejected)
    {
        Assert.Equal(rejected, DiskBenchOptions.Validate(new DiskBenchOptions(dir, mb, seconds), freeMb << 20) is not null);
    }

    [Fact]
    public void DiskOptionsAcceptSafeInput() =>
        Assert.Null(DiskBenchOptions.Validate(new DiskBenchOptions("/home/x/bench", 1024, 4), 2L << 30));

    [Fact]
    public async Task DiskBenchWithMissingDirectoryIsUnavailable()
    {
        var r = await LinuxDiskBench.RunAsync(new DiskBenchOptions("/nonexistent-ilmenyx-dir/x", 256), CancellationToken.None);
        Assert.Equal(BenchRunStatus.Unavailable, r.Status);
        Assert.Null(r.SeqReadMBs);
        Assert.Null(r.Score);
    }

    [Fact]
    public async Task DiskBenchCancelledBeforeIoLeavesNoTemporaryFilesOrValues()
    {
        string root = Path.Combine(Path.GetTempPath(), "ilmenyx-diskbench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var r = await LinuxDiskBench.RunAsync(new DiskBenchOptions(root, 64), cts.Token);
            // tmpfs refuses O_DIRECT (Unavailable); a real disk reaches the first cancellation check.
            Assert.Contains(r.Status, new[] { BenchRunStatus.Cancelled, BenchRunStatus.Unavailable });
            Assert.Null(r.SeqReadMBs);
            Assert.Null(r.Score);
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private const string Mounts = "/dev/nvme0n1p2 / ext4 rw 0 0\ntmpfs /tmp tmpfs rw 0 0\n/dev/sdb1 /mnt/HDD ext4 rw 0 0\n/dev/sdc1 /mnt/with\\040space btrfs rw 0 0\n";

    [Theory]
    [InlineData("/tmp/x", "tmpfs")]
    [InlineData("/home/u", "ext4")]
    [InlineData("/mnt/HDD/Github", "ext4")]
    [InlineData("/mnt/with space/a", "btrfs")]
    [InlineData("/tmpx", "ext4")]
    public void FilesystemTypeUsesLongestMountPoint(string path, string expected) =>
        Assert.Equal(expected, LinuxDiskBench.FilesystemTypeOf(path, Mounts));

    [Fact]
    public void MemoryBackedFoldersAreRefused()
    {
        Assert.NotNull(LinuxDiskBench.MemoryBackedReason("/tmp/bench", Mounts));
        Assert.Null(LinuxDiskBench.MemoryBackedReason("/home/u/bench", Mounts));
    }
}
