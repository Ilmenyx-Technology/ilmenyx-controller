using System.Text.RegularExpressions;

namespace Ilmenyx.Platform.Linux;

public enum SysfsArea { Thermal, PowerSupply, Drm }
public interface IDiagnosticSysfsFiles
{
    IReadOnlyList<string> Devices(SysfsArea area);
    Task<string> ReadAsync(SysfsArea area, string device, string attribute, CancellationToken ct);
}

/// <summary>Fixed read-only sysfs roots and allowlisted attributes; never opens a device node.</summary>
public sealed partial class DiagnosticSysfsFiles : IDiagnosticSysfsFiles
{
    public static string Root(SysfsArea area) => area switch
    {
        SysfsArea.Thermal => "/sys/class/thermal",
        SysfsArea.PowerSupply => "/sys/class/power_supply",
        SysfsArea.Drm => "/sys/class/drm",
        _ => throw new ArgumentOutOfRangeException(nameof(area))
    };
    [GeneratedRegex("^thermal_zone[0-9]{1,5}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ThermalPattern();
    [GeneratedRegex("^card[0-9]{1,5}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CardPattern();
    public static bool ValidDevice(SysfsArea area, string device) => area switch
    {
        SysfsArea.Thermal => ThermalPattern().IsMatch(device),
        SysfsArea.Drm => CardPattern().IsMatch(device),
        SysfsArea.PowerSupply => device.Length is > 0 and <= 128 && device is not ("." or "..") &&
            device.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '+'),
        _ => false
    };
    public static bool ValidAttribute(SysfsArea area, string attribute) => area switch
    {
        SysfsArea.Thermal => attribute is "temp" or "type",
        SysfsArea.PowerSupply => attribute is "type" or "status" or "present" or "online" or "capacity" or "voltage_now" or "current_now" or "power_now" or "energy_now" or "energy_full" or "energy_full_design" or "charge_now" or "charge_full" or "charge_full_design" or "cycle_count" or "temp",
        SysfsArea.Drm => attribute is "device/vendor" or "device/device" or "device/class" or "device/gpu_busy_percent" or "device/mem_info_vram_total" or "device/mem_info_vram_used",
        _ => false
    };
    public IReadOnlyList<string> Devices(SysfsArea area) => Directory.EnumerateDirectories(Root(area))
        .Select(Path.GetFileName).OfType<string>().Where(d => ValidDevice(area, d)).Take(65).ToArray();
    public async Task<string> ReadAsync(SysfsArea area, string device, string attribute, CancellationToken ct)
    {
        if (!ValidDevice(area, device) || !ValidAttribute(area, attribute)) throw new IOException("Caminho sysfs não permitido.");
        return await ReadBoundedAsync(Path.Combine(Root(area), device, attribute), ct);
    }
    internal static async Task<string> ReadBoundedAsync(string path, CancellationToken ct)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true));
        var buffer = new char[4097];
        int length = 0, count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(length), ct)) > 0)
        {
            length += count;
            if (length > 4096) throw new IOException("Atributo sysfs excede 4096 caracteres.");
        }
        return new string(buffer, 0, length);
    }
}
