// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions, adapted from the data gathering of the original DiskHealth.Read (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Disk inputs from sysfs (model, firmware, size, kind, bus), the disk's own hwmon temperature when the kernel exposes one
/// (nvme, drivetemp), mounted filesystems and optional SMART diagnostics supplied by the caller. Nothing is written; smartctl is never run here.
/// </summary>
public sealed partial class LinuxDiskHealthReader(string sysRoot = "/sys")
{
    [GeneratedRegex("^(sd[a-z]+|vd[a-z]+|xvd[a-z]+|nvme[0-9]+n[0-9]+|mmcblk[0-9]+)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Physical();

    private static string One(string path) => SysfsText.Read(path, 120).Replace("\n", " ").Trim();

    public IReadOnlyList<DiskHealthInput> Read(string mountsText, IReadOnlyDictionary<string, SmartDiagnostic>? smart = null)
    {
        string root = Path.Combine(sysRoot, "block");
        if (!Directory.Exists(root)) throw new IOException("/sys/block indisponível.");
        var mounts = MountPoints(mountsText);
        var list = new List<DiskHealthInput>();
        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(p => p, StringComparer.Ordinal).Take(257))
        {
            string name = Path.GetFileName(dir);
            if (!Physical().IsMatch(name)) continue;
            string resolved = new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName ?? "";
            var bus = LinuxParsers.DiskBus(name, resolved);
            string rot = One(Path.Combine(dir, "queue/rotational"));
            string media = rot == "0" && bus.Value is not ("VirtIO" or "Virtual/software") ? "SSD" : rot == "1" && bus.Value is not ("VirtIO" or "Virtual/software") ? "HDD" : "";
            string model = One(Path.Combine(dir, "device/model"));
            string fw = One(Path.Combine(dir, "device/firmware_rev")); if (fw.Length == 0) fw = One(Path.Combine(dir, "device/rev"));
            long size = long.TryParse(One(Path.Combine(dir, "size")), NumberStyles.None, CultureInfo.InvariantCulture, out long sectors) && sectors <= long.MaxValue / 512 ? sectors * 512 : 0;
            double? temp = HwmonTemperature(dir);
            double? wear = null; long? hours = null, cycles = null, uncorrected = null, realloc = null; double? written = null, read = null; bool? passed = null;
            if (smart is not null && smart.TryGetValue(name, out var d) && d.Status == "available")
            {
                passed = d.ReportedPassed.Value;
                double? Get(string metric) => d.Metrics?.FirstOrDefault(m => m.Name == metric)?.Value;
                temp ??= Get("Temperatura"); wear = Get("Percentual usado (NVMe)");
                hours = Get("Horas ligado") is { } hv ? (long)hv : null; cycles = Get("Ciclos liga/desliga") is { } cv ? (long)cv : null;
                long? Sum(params string[] names) { var v = names.Select(n => Get(n)).Where(x => x is not null).Select(x => (long)x!.Value).ToList(); return v.Count == 0 ? null : v.Sum(); }
                uncorrected = Sum("Erros de mídia", "Setores pendentes", "Setores incorrigíveis (offline)");
                realloc = Get("Setores realocados") is { } rv ? (long)rv : null;
                written = Get("Total gravado"); read = Get("Total lido");
            }
            string mountText = mounts.TryGetValue(name, out var m) ? string.Join(", ", m.OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).Take(4)) : "";
            list.Add(new(name, model.Length == 0 ? name : model, fw, bus.Value ?? "", media, size, mountText, passed, temp, wear, hours, cycles, uncorrected, realloc, written, read));
        }
        return list;
    }

    /// <summary>Temperature the kernel exposes for the disk itself (NVMe controller hwmon, drivetemp), in °C; null when absent or implausible.</summary>
    private static double? HwmonTemperature(string blockDir)
    {
        try
        {
            string hwmon = Path.Combine(blockDir, "device/hwmon");
            if (!Directory.Exists(hwmon)) return null;
            foreach (var chip in Directory.EnumerateDirectories(hwmon).Take(8).OrderBy(p => p, StringComparer.Ordinal))
                if (long.TryParse(One(Path.Combine(chip, "temp1_input")), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long milli) && milli is > 0 and < 150000) return milli / 1000.0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static Dictionary<string, List<string>> MountPoints(string mountsText)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in mountsText.Split('\n'))
        {
            var p = line.Split(' ');
            if (p.Length < 3 || !p[0].StartsWith("/dev/", StringComparison.Ordinal)) continue;
            string dev = p[0]["/dev/".Length..];
            string disk = Regex.Replace(dev, "^((?:nvme[0-9]+n[0-9]+|mmcblk[0-9]+))p[0-9]+$", "$1", RegexOptions.CultureInvariant);
            disk = Regex.Replace(disk, "^((?:sd|vd|xvd)[a-z]+)[0-9]+$", "$1", RegexOptions.CultureInvariant);
            string mount = MountedDrives.Unescape(p[1]);
            if (!map.TryGetValue(disk, out var l)) map[disk] = l = [];
            if (!l.Contains(mount)) l.Add(mount);
        }
        return map;
    }
}
