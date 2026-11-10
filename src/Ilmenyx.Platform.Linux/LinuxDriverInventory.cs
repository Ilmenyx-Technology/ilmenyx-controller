// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions, adapted from the inventory and GPU/board checks of the original DriverManager (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Installed drivers from sysfs: PCI and USB devices with the kernel module bound to each. Read-only; no modinfo, no root.</summary>
public sealed partial class LinuxDriverInventory(string sysRoot = "/sys", string procRoot = "/proc", string etcRoot = "/etc")
{
    private readonly string etcRoot = HostSpawn.HostPath(etcRoot); // Flatpak: the host's /etc (/run/host/etc)

    private string Sys(string p) => Path.Combine(sysRoot, p);
    private static string Attr(string path, int max = 160) => SysfsText.Read(path, max).Replace("\n", " ").Trim();

    private static string ClassOf(string pciClass) => pciClass.Length >= 4 ? pciClass[..4] switch
    {
        _ when pciClass.StartsWith("0x03") || pciClass.StartsWith("03") => "DISPLAY",
        _ when pciClass.StartsWith("0x02") || pciClass.StartsWith("02") => "NET",
        _ when pciClass.StartsWith("0x0d") || pciClass.StartsWith("0d") => "NET",
        _ when pciClass.StartsWith("0x04") || pciClass.StartsWith("04") => "MEDIA",
        _ when pciClass.StartsWith("0x01") || pciClass.StartsWith("01") => "STORAGE",
        _ when pciClass.StartsWith("0x0c") || pciClass.StartsWith("0c") => "USB/BUS",
        _ when pciClass.StartsWith("0x08") || pciClass.StartsWith("08") => "SYSTEM",
        _ => ""
    } : "";

    public static string Flags(string taint)
    {
        var f = new List<string>();
        if (taint.Contains('P')) f.Add(L.T("proprietário"));
        if (taint.Contains('O')) f.Add(L.T("fora da árvore do kernel"));
        if (taint.Contains('E')) f.Add(L.T("sem assinatura"));
        return f.Count == 0 ? L.T("módulo do kernel") : string.Join(", ", f);
    }

    private string DriverName(string deviceDir)
    {
        try { var link = new DirectoryInfo(Path.Combine(deviceDir, "driver")).ResolveLinkTarget(true); return link is null ? "" : Path.GetFileName(link.FullName); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return ""; }
    }

    private static string Hex(string id) => id.ToLowerInvariant().Replace("0x", "");

    /// <summary>A Wi-Fi interface lives under the device: net/&lt;if&gt;/wireless or ieee80211/phy*.</summary>
    private static bool IsWireless(string deviceDir)
    {
        try
        {
            if (Directory.Exists(Path.Combine(deviceDir, "ieee80211"))) return true;
            string net = Path.Combine(deviceDir, "net");
            return Directory.Exists(net) && Directory.EnumerateDirectories(net).Take(8).Any(i => Directory.Exists(Path.Combine(i, "wireless")) || Directory.Exists(Path.Combine(i, "phy80211")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private string KernelRelease() => Attr(Path.Combine(procRoot, "sys/kernel/osrelease"), 80);

    public IReadOnlyList<DriverRow> Inventory(PciIds ids)
    {
        var rows = new List<DriverRow>();
        string pci = Sys("bus/pci/devices");
        if (Directory.Exists(pci))
            foreach (var dir in Directory.EnumerateDirectories(pci).OrderBy(p => p, StringComparer.Ordinal).Take(512))
            {
                string cls = ClassOf(Attr(Path.Combine(dir, "class"), 16)); if (cls.Length == 0) continue;
                string vendor = Attr(Path.Combine(dir, "vendor"), 8), device = Attr(Path.Combine(dir, "device"), 8);
                string drv = DriverName(dir);
                string version = drv.Length > 0 && Attr(Sys($"module/{drv}/version"), 64) is { Length: > 0 } v ? v : (drv.Length > 0 ? $"kernel {KernelRelease()}" : "—");
                string taint = drv.Length > 0 ? Attr(Sys($"module/{drv}/taint"), 16) : "";
                rows.Add(new(ids.Device(vendor, device) ?? L.T($"Dispositivo PCI {vendor}:{device}"), cls, ids.Vendor(vendor) ?? vendor, version, drv.Length > 0 ? drv : L.T("sem driver"), drv.Length > 0 ? Flags(taint) : L.T("sem driver carregado"),
                    "PCI", Hex(vendor) + ":" + Hex(device), Attr(Path.Combine(dir, "class"), 16), IsWireless(dir)));
            }
        string usb = Sys("bus/usb/devices");
        if (Directory.Exists(usb))
            foreach (var dir in Directory.EnumerateDirectories(usb).Where(d => !Path.GetFileName(d).Contains(':') && !Path.GetFileName(d).StartsWith("usb", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).Take(128))
            {
                string product = Attr(Path.Combine(dir, "product"), 80); if (product.Length == 0) continue;
                var drivers = Directory.EnumerateDirectories(dir, Path.GetFileName(dir) + ":*").Take(16).Select(DriverName).Where(d => d.Length > 0).Distinct().ToList();
                var interfaces = Directory.EnumerateDirectories(dir, Path.GetFileName(dir) + ":*").Take(16).ToList();
                string classes = string.Join(",", interfaces.Select(i => Attr(Path.Combine(i, "bInterfaceClass"), 4)).Where(c => c.Length > 0).Distinct());
                rows.Add(new(product, "USB", Attr(Path.Combine(dir, "manufacturer"), 80) is { Length: > 0 } mf ? mf : "—", $"kernel {KernelRelease()}", drivers.Count > 0 ? string.Join(", ", drivers) : L.T("sem driver"), drivers.Count > 0 ? L.T("módulo do kernel") : L.T("sem driver carregado"),
                    "USB", Hex(Attr(Path.Combine(dir, "idVendor"), 8)) + ":" + Hex(Attr(Path.Combine(dir, "idProduct"), 8)), classes, interfaces.Any(IsWireless)));
            }
        return rows.OrderBy(r => r.Class, StringComparer.Ordinal).ThenBy(r => r.Device, StringComparer.Ordinal).ToList();
    }

    [GeneratedRegex(@"Kernel Module\s+([0-9][0-9.]*)", RegexOptions.CultureInvariant)]
    private static partial Regex NvrmVersion();

    public IReadOnlyList<GpuDriverStatus> Gpus(PciIds ids)
    {
        var list = new List<GpuDriverStatus>();
        string pci = Sys("bus/pci/devices");
        if (!Directory.Exists(pci)) return list;
        foreach (var dir in Directory.EnumerateDirectories(pci).OrderBy(p => p, StringComparer.Ordinal).Take(512))
        {
            if (!Attr(Path.Combine(dir, "class"), 16).StartsWith("0x03", StringComparison.Ordinal)) continue;
            string vendor = Attr(Path.Combine(dir, "vendor"), 8), device = Attr(Path.Combine(dir, "device"), 8), drv = DriverName(dir);
            var g = new GpuDriverStatus { GpuName = ids.Device(vendor, device) ?? $"GPU {vendor}:{device}", Driver = drv.Length > 0 ? drv : L.T("sem driver"), DeviceId = Hex(device), PciId = Hex(vendor) + ":" + Hex(device) };
            switch (vendor.ToLowerInvariant().Replace("0x", ""))
            {
                case "10de":
                    g.Vendor = "NVIDIA"; g.OfficialPage = "https://www.nvidia.com/Download/index.aspx";
                    bool proprietary = drv.StartsWith("nvidia", StringComparison.Ordinal); // nouveau must not inherit the proprietary module's version
                    var m = NvrmVersion().Match(proprietary && File.Exists(Path.Combine(procRoot, "driver/nvidia/version")) ? Safe(Path.Combine(procRoot, "driver/nvidia/version")) : "");
                    g.Installed = m.Success ? m.Groups[1].Value : proprietary ? Attr(Sys("module/nvidia/version"), 64) : "";
                    if (g.Installed.Length == 0) g.Message = drv == "nouveau" ? GpuTelemetrySensors.NouveauReason : L.T("Driver proprietário NVIDIA não detectado.");
                    break;
                case "1002": g.Vendor = "AMD"; g.OfficialPage = "https://www.amd.com/en/support/download/drivers.html"; g.Installed = $"{g.Driver} · kernel {KernelRelease()}"; g.Message = L.T("Driver amdgpu/Mesa acompanha o kernel e as atualizações da sua distribuição."); break;
                case "8086": g.Vendor = "Intel"; g.OfficialPage = "https://www.intel.com/content/www/us/en/download-center/home.html"; g.Installed = $"{g.Driver} · kernel {KernelRelease()}"; g.Message = L.T("Driver i915/xe e Mesa acompanham o kernel e as atualizações da sua distribuição."); break;
                default: g.Installed = $"{g.Driver} · kernel {KernelRelease()}"; g.Message = L.T("Fabricante não reconhecido."); break;
            }
            list.Add(g);
        }
        return list;
    }
    private static string Safe(string path) { try { return File.ReadAllText(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; } }

    public BoardInfo Board()
    {
        string D(string f) => Attr(Sys("class/dmi/id/" + f));
        string Clean(string s) => s == "" ? "" : s;
        return DriverBoard.Describe(Clean(D("board_vendor")), Clean(D("board_name")), Clean(D("sys_vendor")), Clean(D("product_name")), Clean(D("bios_version")), Clean(D("bios_date")));
    }

    public (string Id, string IdLike) Distro()
    {
        string text = Safe(Path.Combine(etcRoot, "os-release"));
        string F(string key) => text.Split('\n').Where(l => l.StartsWith(key + "=", StringComparison.Ordinal)).Select(l => l[(key.Length + 1)..].Trim().Trim('"')).FirstOrDefault() ?? "";
        return (F("ID"), F("ID_LIKE"));
    }
}
