// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Globalization;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Read-only temperatures for the temperature cut of CLI loads: CPU package (coretemp/k10temp/zenpower) and the DRM card that matches a PCI vendor/device ID
/// (amdgpu/nouveau/i915 hwmon). Null when there is no readable sensor; nothing is inferred.
/// </summary>
public static class LinuxThermalProbe
{
    private static double? Milli(string path)
    {
        try { return long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long m) && m is > -50000 and < 150000 ? m / 1000.0 : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string Text(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>CPU package temperature (Tctl/Tdie/Package id 0, else the first input of the chip).</summary>
    public static double? CpuTempC(string sysRoot = "/sys")
    {
        string root = Path.Combine(sysRoot, "class/hwmon");
        if (!Directory.Exists(root)) return null;
        try
        {
            foreach (var chip in Directory.EnumerateDirectories(root).Take(64).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (Text(Path.Combine(chip, "name")) is not ("coretemp" or "k10temp" or "zenpower")) continue;
                double? first = null, preferred = null;
                foreach (var input in Directory.EnumerateFiles(chip, "temp*_input").Take(64).OrderBy(p => p, StringComparer.Ordinal))
                {
                    if (Milli(input) is not double c) continue;
                    first ??= c;
                    if (Text(input.Replace("_input", "_label", StringComparison.Ordinal)) is "Package id 0" or "Tctl" or "Tdie") preferred ??= c;
                }
                if ((preferred ?? first) is double found) return found;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Hottest temp*_input of the hwmon of the DRM card whose PCI IDs match (Vulkan vendorID/deviceID).</summary>
    public static double? GpuTempC(uint vendorId, uint deviceId, string sysRoot = "/sys")
    {
        string root = Path.Combine(sysRoot, "class/drm");
        if (!Directory.Exists(root)) return null;
        string vendor = "0x" + vendorId.ToString("x4", CultureInfo.InvariantCulture), device = "0x" + deviceId.ToString("x4", CultureInfo.InvariantCulture);
        try
        {
            foreach (var card in Directory.EnumerateDirectories(root, "card*").Where(c => !Path.GetFileName(c).Contains('-')).Take(16).OrderBy(p => p, StringComparer.Ordinal))
            {
                string dev = Path.Combine(card, "device");
                if (!string.Equals(Text(Path.Combine(dev, "vendor")), vendor, StringComparison.OrdinalIgnoreCase) || !string.Equals(Text(Path.Combine(dev, "device")), device, StringComparison.OrdinalIgnoreCase)) continue;
                string hw = Path.Combine(dev, "hwmon");
                if (!Directory.Exists(hw)) return null;
                double? max = null;
                foreach (var chip in Directory.EnumerateDirectories(hw).Take(8))
                    foreach (var input in Directory.EnumerateFiles(chip, "temp*_input").Take(16))
                        if (Milli(input) is double c) max = max is double m ? Math.Max(m, c) : c;
                return max;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
