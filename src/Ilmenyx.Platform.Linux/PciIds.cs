// New Ilmenyx additions: see LICENSING.md.
using System.Globalization;

namespace Ilmenyx.Platform.Linux;

/// <summary>Vendor/device names from the system pci.ids (read-only, bounded). Missing file means no names, never an error.</summary>
public sealed class PciIds
{
    public static readonly string[] CandidatePaths = ["/usr/share/hwdata/pci.ids", "/usr/share/misc/pci.ids", "/usr/share/pci.ids"];
    private readonly Dictionary<string, string> vendors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> devices = new(StringComparer.Ordinal);
    public bool IsEmpty => vendors.Count == 0;

    public static PciIds Load(IEnumerable<string>? paths = null)
    {
        foreach (var path in paths ?? CandidatePaths)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length is 0 or > 16 << 20) continue;
                return Parse(File.ReadAllText(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new();
    }

    public static PciIds Parse(string text)
    {
        var ids = new PciIds(); string? vendor = null;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == 'C') break; // class section follows the vendor tables
            if (line[0] != '\t')
            {
                if (line.Length > 6 && line[4] == ' ' && IsHex(line[..4])) { vendor = line[..4].ToLowerInvariant(); ids.vendors[vendor] = line[6..].Trim(); }
                else vendor = null;
            }
            else if (line[1] != '\t' && vendor is not null && line.Length > 7 && line[5] == ' ' && IsHex(line[1..5]))
                ids.devices[vendor + ":" + line[1..5].ToLowerInvariant()] = line[7..].Trim();
        }
        return ids;
    }

    private static bool IsHex(string s) => s.Length == 4 && s.All(Uri.IsHexDigit);
    private static string Norm(string id) => id.Trim().ToLowerInvariant().Replace("0x", "").PadLeft(4, '0');
    public string? Vendor(string vendorId) => vendors.GetValueOrDefault(Norm(vendorId));
    public string? Device(string vendorId, string deviceId) => devices.GetValueOrDefault(Norm(vendorId) + ":" + Norm(deviceId));
}
