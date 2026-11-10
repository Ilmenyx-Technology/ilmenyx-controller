// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class DeviceChangesTests
{
    private static DeviceSnapshot Snap(string[] usb, string[] mounts) =>
        new(usb.Select(u => new WatchedUsb(u, "Dev " + u)).ToList(), mounts.Select(m => new WatchedMount(m, Path.GetFileName(m))).ToList());

    [Fact]
    public void FirstSnapshotIsOnlyABaseline() => Assert.Empty(DeviceChanges.Diff(null, Snap(["1-1"], ["/run/media/u/STICK"])));

    [Fact]
    public void ConnectAndRemoveProduceNotices()
    {
        var before = Snap(["1-1", "1-2"], ["/run/media/u/A"]);
        var after = Snap(["1-1", "1-3"], ["/run/media/u/B"]);
        var toasts = DeviceChanges.Diff(before, after);
        Assert.Contains(toasts, t => t.Title == "Unidade conectada" && t.Text == "B");
        Assert.Contains(toasts, t => t.Title == "Unidade removida" && t.Text.StartsWith("A"));
        Assert.Contains(toasts, t => t.Title == "Dispositivo USB conectado" && t.Text == "Dev 1-3");
        Assert.Contains(toasts, t => t.Title == "Dispositivo USB desconectado" && t.Text == "Dev 1-2");
        Assert.Empty(DeviceChanges.Diff(after, after));
    }

    [Fact]
    public void NoticeFloodIsBounded()
    {
        var many = Snap(Enumerable.Range(0, 50).Select(i => "9-" + i).ToArray(), []);
        Assert.Equal(DeviceChanges.MaxToasts, DeviceChanges.Diff(Snap([], []), many).Count);
    }

    [Fact]
    public void WatcherReadsUsbAndUserMountsWithoutSerials()
    {
        string root = Path.Combine(Path.GetTempPath(), "ilmenyx-dev-" + Guid.NewGuid().ToString("N"));
        string devices = Path.Combine(root, "sys/bus/usb/devices"); Directory.CreateDirectory(devices);
        void Dev(string id, string cls, string product, string maker = "", string serial = "SECRET-SERIAL")
        {
            string d = Path.Combine(devices, id); Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "bDeviceClass"), cls + "\n"); File.WriteAllText(Path.Combine(d, "product"), product + "\n");
            File.WriteAllText(Path.Combine(d, "manufacturer"), maker + "\n"); File.WriteAllText(Path.Combine(d, "serial"), serial + "\n");
            File.WriteAllText(Path.Combine(d, "idVendor"), "046d\n"); File.WriteAllText(Path.Combine(d, "idProduct"), "c08b\n");
        }
        try
        {
            Dev("1-1", "00", "Gaming Mouse G502", "Logitech"); Dev("1-3", "09", "USB2.0 HUB"); Dev("usb1", "09", "xHCI Host Controller"); Dev("1-1:1.0", "00", "interface"); Dev("1-5", "00", "");
            string mounts = Path.Combine(root, "mounts");
            File.WriteAllText(mounts, "/dev/nvme0n1p2 / ext4 rw 0 0\n/dev/sdb1 /run/media/u/MEU\\040PEN vfat rw 0 0\n/dev/sdc1 /mnt/HDD ext4 rw 0 0\ntmpfs /run/media/x tmpfs rw 0 0\n/dev/loop1 /snap/core/1 squashfs ro 0 0\n");
            var snap = LinuxDeviceWatcher.Read(Path.Combine(root, "sys"), mounts);
            Assert.Equal(["1-1", "1-5"], snap.Usb.Select(u => u.Id));
            Assert.Equal("Gaming Mouse G502 · Logitech", snap.Usb[0].Name);
            Assert.Equal("046d:c08b", snap.Usb[1].Name);
            Assert.DoesNotContain(snap.Usb, u => u.Name.Contains("SECRET"));
            Assert.Equal("MEU PEN (/run/media/u/MEU PEN)", snap.Mounts.Single().Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void WatcherToleratesMissingSources()
    {
        var snap = LinuxDeviceWatcher.Read("/nonexistent-sys", "/nonexistent-mounts");
        Assert.Empty(snap.Usb); Assert.Empty(snap.Mounts);
    }
}
