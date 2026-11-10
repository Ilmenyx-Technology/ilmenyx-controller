// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Reads which hardware this machine has (sysfs only): GPU vendors, whether an AMD GPU is dedicated, and HID devices of USB vendors known for RGB. Nothing is opened or started.</summary>
public static class MachineProbeReader
{
    // USB vendor ids of RGB lighting hardware OpenRGB supports in volume: Corsair, Razer, SteelSeries, HyperX/Kingston, Cooler Master, NZXT, Gigabyte, ASUS, MSI, Logitech G, ASRock/Thermaltake.
    private static readonly HashSet<string> RgbVendors = ["1b1c", "1532", "1038", "0951", "2516", "1e71", "1044", "0b05", "1462", "046d", "26ce", "264a"];

    public static MachineProbe Read(bool rayTracingGpu, bool gameLibrary, string sysRoot = "/sys")
    {
        bool amd = false, amdDedicated = false, nvidia = false, rgb = false;
        foreach (var (card, vendor) in Vendors(Path.Combine(sysRoot, "class", "drm"), "card*", "device/vendor"))
        {
            if (vendor == "0x1002") { amd = true; if (AmdDedicated(Path.Combine(card, "device"))) amdDedicated = true; }
            else if (vendor == "0x10de") nvidia = true;
        }
        // RGB controllers are HID devices; the same vendor ids also sell pendrives (Kingston DataTraveler, 0951) and webcams (Logitech), which do not count.
        foreach (var (device, vendor) in Vendors(Path.Combine(sysRoot, "bus", "usb", "devices"), "*", "idVendor"))
            if (RgbVendors.Contains(vendor) && HasHidInterface(device)) rgb = true;
        return new(amd, nvidia, rayTracingGpu, rgb, gameLibrary, amdDedicated);
    }

    /// <summary>
    /// Not an APU: the amdgpu gpu_metrics header says "dedicated table", or (no gpu_metrics, older cards) the card has its own fan sensor.
    /// An APU header or nothing readable stays false.
    /// </summary>
    private static bool AmdDedicated(string device)
    {
        try
        {
            var metrics = Path.Combine(device, "gpu_metrics");
            if (File.Exists(metrics))
            {
                using var file = new FileStream(metrics, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, false);
                Span<byte> header = stackalloc byte[4];
                bool? apu = file.ReadAtLeast(header, 4, throwOnEndOfStream: false) == 4 ? LinuxGpuCollector.GpuMetricsIsApu(header) : null;
                if (apu is not null) return apu == false;
            }
            var hwmon = Path.Combine(device, "hwmon");
            return Directory.Exists(hwmon) && Directory.EnumerateDirectories(hwmon).Any(h => File.Exists(Path.Combine(h, "fan1_input")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Any interface of the USB device with class 03 (HID): keyboards, mice and lighting controllers.</summary>
    private static bool HasHidInterface(string device)
    {
        try
        {
            foreach (var iface in Directory.EnumerateDirectories(device, "*:*"))
            {
                var file = Path.Combine(iface, "bInterfaceClass");
                if (File.Exists(file) && File.ReadAllText(file).Trim() == "03") return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return false;
    }

    /// <summary>Cheap file checks only (no Steam/Lutris/Heroic process is started): a Steam appmanifest, the Lutris database or Heroic's game list.</summary>
    public static bool HasGameLibrary(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            foreach (var steam in new[] { Path.Combine(home, ".local", "share", "Steam", "steamapps"), Path.Combine(home, ".steam", "steam", "steamapps") })
                if (Directory.Exists(steam) && Directory.EnumerateFiles(steam, "appmanifest_*.acf").Any()) return true;
            return File.Exists(Path.Combine(home, ".local", "share", "lutris", "pga.db")) || Directory.Exists(Path.Combine(home, ".config", "heroic", "store_cache"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<(string Entry, string Vendor)> Vendors(string folder, string pattern, string file)
    {
        string[] entries;
        try { entries = Directory.Exists(folder) ? Directory.GetDirectories(folder, pattern) : []; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var entry in entries)
        {
            string text;
            try { var path = Path.Combine(entry, file); if (!File.Exists(path)) continue; text = File.ReadAllText(path).Trim(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (text.Length is > 0 and < 12) yield return (entry, text.ToLowerInvariant());
        }
    }
}
