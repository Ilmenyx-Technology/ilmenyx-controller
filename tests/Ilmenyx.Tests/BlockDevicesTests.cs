// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

// Fixtures/lsblk-hp250g8.json: real `lsblk -J -b` of an HP 250 G8 (NVMe with btrfs + Windows, SATA HD, Ventoy USB stick, zram), user name removed.
public sealed class BlockDevicesTests
{
    private static IReadOnlyList<BlockDevice> Real() => BlockDevices.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lsblk-hp250g8.json")));

    [Fact]
    public void RealTreeIsParsedWithSystemAndExternalFlags()
    {
        var all = Real();
        var nvme = all.Single(d => d.Name == "nvme0n1");
        Assert.True(nvme.IsSystem); Assert.False(nvme.IsExternal); Assert.False(nvme.Rotational);
        Assert.Equal(4, nvme.Partitions.Count());
        var root = nvme.Partitions.Single(p => p.FsType == "btrfs");
        Assert.Contains("/", root.Mounts); Assert.True(root.FsUsed > 0 && root.FsAvail > 0);
        Assert.False(nvme.Partitions.Single(p => p.FsType == "ntfs").IsSystem);
        var hd = all.Single(d => d.Name == "sda");
        Assert.True(hd.Rotational); Assert.False(hd.IsSystem); Assert.Equal("sata", hd.Transport);
        var usb = all.Single(d => d.Name == "sdb");
        Assert.True(usb.IsExternal); Assert.False(usb.IsSystem); Assert.Equal("usb", usb.Transport);
        Assert.True(all.Single(d => d.Name == "zram0").IsSystem); // [SWAP]
    }

    [Fact]
    public void OlderLsblkWithStringsIsAccepted()
    {
        var d = Assert.Single(BlockDevices.Parse("""{"blockdevices":[{"name":"sdc","size":"1000","type":"disk","rm":"1","mountpoint":"/media/x","fsused":"10","fsavail":"90"}]}"""));
        Assert.Equal(1000, d.Size); Assert.True(d.Removable); Assert.Equal(["/media/x"], d.Mounts); Assert.Equal(10, d.FsUsed); Assert.Equal("/dev/sdc", d.Path);
    }

    [Fact]
    public async Task SystemAndInternalDisksAreRefusedBeforeAnyCommand()
    {
        var all = Real();
        var nvme = all.Single(d => d.Name == "nvme0n1");
        Assert.NotNull(await BlockDevices.EjectAsync(nvme));
        Assert.NotNull(await BlockDevices.UnmountAsync(nvme.Partitions.Single(p => p.FsType == "btrfs")));
        var hd = all.Single(d => d.Name == "sda");
        Assert.NotNull(await BlockDevices.FormatAsync(hd.Partitions.First(), hd, "exfat", "X")); // internal disk
        var usb = all.Single(d => d.Name == "sdb");
        Assert.NotNull(await BlockDevices.FormatAsync(usb.Partitions.First(), usb, "exfat", "bad'label")); // quote in the label
        Assert.NotNull(await BlockDevices.FormatAsync(usb.Partitions.First(), usb, "zfs", "OK")); // unknown type
        Assert.NotNull(await BlockDevices.FormatAsync(hd.Partitions.First(), usb, "exfat", "OK")); // partition of another disk
    }

    [Theory]
    [InlineData("Error unmounting /dev/sdb1: GDBus.Error:org.freedesktop.UDisks2.Error.DeviceBusy: Error unmounting /dev/sdb1: target is busy", "usando")]
    [InlineData("GDBus.Error:org.freedesktop.UDisks2.Error.NotAuthorizedDismissed: Not authorized to perform operation", "autorizou")]
    public void UdisksErrorsInPlainWords(string output, string expected) => Assert.Contains(expected, BlockDevices.Friendly(output));

    [Fact]
    public void SmartHelperOnlyRunsFixedReadOnlyArguments()
    {
        Assert.Contains("--json --health --attributes --nocheck=standby,3,5", SmartAccess.HelperScript);
        Assert.DoesNotContain("-t ", SmartAccess.HelperScript); Assert.DoesNotContain("-s ", SmartAccess.HelperScript);
        Assert.Contains("<allow_active>yes</allow_active>", SmartAccess.PolicyXml);
        Assert.Contains(SmartAccess.Helper, SmartAccess.PolicyXml);
        Assert.Contains("cat > " + SmartAccess.Helper, SmartAccess.InstallScript);
        Assert.Equal("rm -f " + SmartAccess.Helper + " " + SmartAccess.Policy, SmartAccess.UndoScript);
        Assert.Equal("sudo pacman -S smartmontools", SmartAccess.InstallHint("cachyos", "arch"));
        Assert.Equal("sudo apt install smartmontools", SmartAccess.InstallHint("linuxmint", "ubuntu debian"));
        Assert.Equal("sudo dnf install smartmontools", SmartAccess.InstallHint("fedora", ""));
    }
}
