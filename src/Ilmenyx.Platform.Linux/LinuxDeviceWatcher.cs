// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Read-only snapshot of USB devices (sysfs) and user-mounted drives (/proc/self/mounts). Serial numbers are never read.</summary>
public static class LinuxDeviceWatcher
{
    public const int MaxDevices = 256;
    private static readonly string[] IgnoredFilesystems = ["squashfs", "overlay", "tmpfs", "proc", "sysfs", "devtmpfs", "cgroup2", "autofs", "fuse.gvfsd-fuse", "fuse.portal"];

    public static DeviceSnapshot Read(string sysRoot = "/sys", string mountsPath = "/proc/self/mounts")
    {
        var usb = new List<WatchedUsb>();
        try
        {
            string root = Path.Combine(sysRoot, "bus/usb/devices");
            if (Directory.Exists(root))
                foreach (string dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal).Take(MaxDevices))
                {
                    string id = Path.GetFileName(dir);
                    if (id.Contains(':') || id.StartsWith("usb", StringComparison.Ordinal)) continue; // interfaces and root hubs
                    string cls = One(dir, "bDeviceClass");
                    if (cls == "09") continue; // hubs
                    string product = One(dir, "product"), maker = One(dir, "manufacturer");
                    string name = product.Length > 0 ? (maker.Length > 0 && !product.Contains(maker, StringComparison.OrdinalIgnoreCase) ? $"{product} · {maker}" : product)
                        : $"{One(dir, "idVendor")}:{One(dir, "idProduct")}";
                    usb.Add(new(id, name));
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var mounts = new List<WatchedMount>();
        try
        {
            foreach (string line in File.ReadLines(mountsPath).Take(2048))
            {
                var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 3 || IgnoredFilesystems.Contains(f[2]) || !f[0].StartsWith("/dev/", StringComparison.Ordinal)) continue;
                string mount = MountedDrives.Unescape(f[1]);
                if (!(mount.StartsWith("/media/", StringComparison.Ordinal) || mount.StartsWith("/run/media/", StringComparison.Ordinal))) continue;
                mounts.Add(new(mount, Path.GetFileName(mount.TrimEnd('/')) is { Length: > 0 } label ? $"{label} ({mount})" : mount));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new(usb, mounts);
    }

    private static string One(string dir, string file) { try { return SysfsText.Read(Path.Combine(dir, file), 128).Trim(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; } }
}
