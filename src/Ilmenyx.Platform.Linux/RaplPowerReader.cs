// New Ilmenyx additions: see LICENSING.md.
namespace Ilmenyx.Platform.Linux;

public enum RaplState { Unknown, Available, NotPresent, NoPermission }

/// <summary>
/// CPU package power from the kernel powercap RAPL counters (/sys/class/powercap/intel-rapl:N, also used for AMD Zen).
/// Read-only. Since CVE-2020-8694 the kernel makes energy_uj readable by root only: then the state is NoPermission and the
/// value stays null (never estimated). Power = energy delta / time delta between two samples; the counter wrap is handled.
/// </summary>
public sealed class RaplPowerReader(string sysRoot = "/sys")
{
    private (double At, long Energy)? before;
    private string? zone;
    public RaplState State { get; private set; }
    public string? ZonePath => zone;

    /// <summary>The package zone (name starting with "package"); null when the machine exposes no RAPL.</summary>
    public string? FindPackageZone()
    {
        string root = Path.Combine(sysRoot, "class/powercap");
        try
        {
            if (!Directory.Exists(root)) return null;
            foreach (var dir in Directory.EnumerateDirectories(root, "intel-rapl:*").Where(d => Path.GetFileName(d).Count(c => c == ':') == 1).OrderBy(d => d, StringComparer.Ordinal).Take(8))
                if (SysfsText.Read(Path.Combine(dir, "name"), 64).StartsWith("package", StringComparison.Ordinal)) return dir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static long? ReadLong(string path, out bool denied)
    {
        denied = false;
        try
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, false));
            var buffer = new char[32]; int n = reader.ReadBlock(buffer, 0, buffer.Length);
            return long.TryParse(new string(buffer, 0, n).Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : null;
        }
        catch (UnauthorizedAccessException) { denied = true; return null; }
        catch (IOException) { return null; }
    }

    /// <summary>Watts since the previous call (null on the first call, on failure or without permission).</summary>
    public double? Sample(double nowSeconds)
    {
        zone ??= FindPackageZone();
        if (zone is null) { State = RaplState.NotPresent; return null; }
        long? energy = ReadLong(Path.Combine(zone, "energy_uj"), out bool denied);
        if (denied) { State = RaplState.NoPermission; before = null; return null; }
        if (energy is not long e) { State = RaplState.NotPresent; before = null; return null; }
        State = RaplState.Available;
        var previous = before; before = (nowSeconds, e);
        if (previous is not { } p || nowSeconds - p.At < 0.2) return null;
        long delta = e - p.Energy;
        if (delta < 0)
        {
            // The counter wrapped at max_energy_range_uj.
            if (ReadLong(Path.Combine(zone, "max_energy_range_uj"), out _) is not long max || max <= 0) return null;
            delta += max;
        }
        double watts = delta / 1_000_000.0 / (nowSeconds - p.At);
        return watts is >= 0 and < 2000 ? watts : null;
    }
}
