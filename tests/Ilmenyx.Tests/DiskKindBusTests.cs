// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class DiskKindBusTests
{
    [Theory]
    [InlineData("0", "available", "Não rotacional")]
    [InlineData("1\n", "available", "Rotacional (HDD)")]
    [InlineData("2", "unavailable", null)]
    [InlineData("", "unavailable", null)]
    [InlineData(null, "unavailable", null)]
    public void RotationalClassification(string? text, string status, string? contains)
    {
        var kind = LinuxParsers.DiskKind(text, "/sys/block/x/queue/rotational");
        Assert.Equal(status, kind.Status);
        if (contains is not null) Assert.Contains(contains, kind.Value);
    }

    [Theory]
    [InlineData("nvme0n1", "/sys/devices/pci0000:00/0000:00:1d.0/0000:3d:00.0/nvme/nvme0/nvme0n1", "NVMe")]
    [InlineData("sda", "/sys/devices/pci0000:00/0000:00:17.0/ata1/host0/target0:0:0/0:0:0:0/block/sda", "SATA/ATA")]
    [InlineData("sdb", "/sys/devices/pci0000:00/0000:00:14.0/usb2/2-1/2-1:1.0/host6/target6:0:0/6:0:0:0/block/sdb", "USB")]
    [InlineData("vda", "/sys/devices/pci0000:00/0000:00:04.0/virtio1/block/vda", "VirtIO")]
    [InlineData("mmcblk0", "/sys/devices/pci0000:00/0000:00:1a.0/mmc_host/mmc0/mmc0:0001/block/mmcblk0", "MMC/SD")]
    [InlineData("zram0", "/sys/devices/virtual/block/zram0", "Virtual/software")]
    [InlineData("loop3", null, "Virtual/software")]
    public void BusClassification(string name, string? resolved, string expected)
    {
        var bus = LinuxParsers.DiskBus(name, resolved);
        Assert.Equal("available", bus.Status); Assert.Equal(expected, bus.Value);
    }

    [Theory]
    [InlineData("sdc", null)]
    [InlineData("sdc", "/sys/devices/pci0000:00/host9/target9:0:0/block/sdc")]
    public void UnknownTransportIsReportedUnavailableNotGuessed(string name, string? resolved)
    {
        var bus = LinuxParsers.DiskBus(name, resolved);
        Assert.Equal("unavailable", bus.Status); Assert.Null(bus.Value);
    }

    [Fact]
    public void PathSegmentsAreMatchedByPrefixNotSubstring()
    {
        // "dataset" contains "ata" but is not an ata controller segment.
        Assert.Equal("unavailable", LinuxParsers.DiskBus("sdd", "/sys/devices/dataset/block/sdd").Status);
    }

    [Fact]
    public void DiskExtraShowsOnlyAvailableReadings()
    {
        var disk = new DiskInfo("sda", Reading<long?>.Available(1, "B", "x"), LinuxParsers.DiskKind("1", "k"), LinuxParsers.DiskBus("sda", null));
        Assert.Equal(" · Rotacional (HDD)", SnapshotReport.DiskExtra(disk));
        Assert.Equal("", SnapshotReport.DiskExtra(new DiskInfo("sda", Reading<long?>.Available(1, "B", "x"))));
    }

    [Theory]
    [InlineData("sr0", "0", null, "Unidade óptica")]
    [InlineData("fd0", "1", null, "Disquete")]
    [InlineData("vda", "1", "/sys/devices/pci0000:00/0000:00:04.0/virtio1/block/vda", "disco virtual")]
    [InlineData("loop0", "1", null, "disco virtual")]
    [InlineData("sda", "1", "/sys/devices/pci0000:00/ata1/host0/block/sda", "Rotacional (HDD)")]
    public void KindIsCorrectedForOpticalLegacyAndVirtualDevices(string name, string text, string? path, string expected)
    {
        var kind = LinuxParsers.DiskKindFor(name, text, "src", LinuxParsers.DiskBus(name, path));
        Assert.Equal("available", kind.Status); Assert.Contains(expected, kind.Value);
        if (name == "sda") Assert.DoesNotContain("virtual", kind.Value);
    }

    [Theory]
    [InlineData("sr")]
    [InlineData("sra")]
    [InlineData("fdx")]
    public void OpticalAndLegacyOverridesRequireNumericSuffix(string name) =>
        Assert.DoesNotContain("óptica", LinuxParsers.DiskKindFor(name, "0", "src", LinuxParsers.DiskBus(name, null)).Value ?? "");
}
