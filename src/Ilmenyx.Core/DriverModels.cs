// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions, adapted from the model and board mapping of the original DriverManager (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;

namespace Ilmenyx.Core;

/// <param name="Bus">"PCI" or "USB" (empty in older exports).</param>
/// <param name="Id">vendor:device in hex ("10de:134d"), PCI or USB.</param>
/// <param name="ClassCode">PCI class ("0x028000") or the USB interface classes ("e0,e0").</param>
/// <param name="Wireless">A Wi-Fi interface (net/*/wireless or phy80211) belongs to this device.</param>
public sealed record DriverRow(string Device, string Class, string Vendor, string Version, string Driver, string Flags, string Bus = "", string Id = "", string ClassCode = "", bool Wireless = false);
public sealed record DriverInventoryReport(string SchemaVersion, DateTimeOffset ExportedAt, string Note, IReadOnlyList<DriverRow> Drivers);

public sealed class GpuDriverStatus
{
    public string Vendor { get; set; } = "";       // NVIDIA / AMD / Intel / ""
    public string GpuName { get; set; } = "";
    public string Driver { get; set; } = "";       // kernel module in use
    public string Installed { get; set; } = "";
    public string Latest { get; set; } = "";       // empty when not queried
    public string OfficialPage { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>PCI device id (hex, no "0x"): for NVIDIA it selects the driver branch that supports the generation.</summary>
    public string DeviceId { get; set; } = "";
    /// <summary>vendor:device ("10de:134d"), the same key as <see cref="DriverRow.Id"/>.</summary>
    public string PciId { get; set; } = "";
    /// <summary>NVIDIA branch that supports this card (580 for Maxwell–Volta, 595 for Turing+, 470 for Kepler), null otherwise.</summary>
    public string? SupportedBranch => Vendor == "NVIDIA" ? NvidiaGenerations.Support(NvidiaGenerations.Classify(DeviceId).Generation).Branch : null;
    /// <summary>Latest is the newest version of <see cref="SupportedBranch"/> (never the global latest, which may not support the card).</summary>
    public bool UpdateAvailable => Vendor == "NVIDIA" && Latest.Length > 0 && Installed.Length > 0 && DriverVersions.Compare(Latest, Installed) > 0;
}

public sealed class BoardInfo
{
    public string Maker { get; set; } = "";
    public string Model { get; set; } = "";
    public string Bios { get; set; } = "";
    public string Brand { get; set; } = "";
    public string SupportUrl { get; set; } = "";
}

public static class DriverVersions
{
    /// <summary>Compares dotted numeric versions segment by segment ("570.133.07" vs "550.54"); non-numeric segments count as 0.</summary>
    public static int Compare(string a, string b)
    {
        var x = a.Split('.'); var y = b.Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            long p = i < x.Length && long.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out long v1) ? v1 : 0;
            long q = i < y.Length && long.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out long v2) ? v2 : 0;
            if (p != q) return p.CompareTo(q);
        }
        return 0;
    }
}

public static class DriverBoard
{
    public static BoardInfo Describe(string boardMaker, string boardModel, string sysVendor, string productName, string biosVersion, string biosDate)
    {
        var b = new BoardInfo { Maker = boardMaker, Model = boardModel, Bios = biosVersion.Length > 0 ? $"{biosVersion}{(biosDate.Length > 0 ? $" ({biosDate})" : "")}" : "" };
        string all = boardMaker + " " + sysVendor;
        bool Has(string s) => all.Contains(s, StringComparison.OrdinalIgnoreCase);
        string model = boardModel.Length > 0 && !boardModel.Contains("Default", StringComparison.OrdinalIgnoreCase) && boardModel != "—" ? boardModel : productName;
        string q = Uri.EscapeDataString(model);
        if (Has("ASUS")) { b.Brand = "ASUS"; b.SupportUrl = "https://www.asus.com/support/download-center/"; }
        else if (Has(L.T("Micro-Star")) || Has("MSI")) { b.Brand = "MSI"; b.SupportUrl = "https://www.msi.com/search/" + q; }
        else if (Has("GIGABYTE")) { b.Brand = "Gigabyte"; b.SupportUrl = "https://www.gigabyte.com/Support"; }
        else if (Has("ASRock")) { b.Brand = "ASRock"; b.SupportUrl = "https://www.asrock.com/support/index.asp"; }
        else if (Has("Dell")) { b.Brand = "Dell"; b.SupportUrl = "https://www.dell.com/support/home/"; }
        else if (Has("LENOVO")) { b.Brand = "Lenovo"; b.SupportUrl = "https://support.lenovo.com/"; }
        else if (Has("Hewlett") || all.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.Equals("HP", StringComparison.OrdinalIgnoreCase))) { b.Brand = "HP"; b.SupportUrl = "https://support.hp.com/"; }
        else if (Has("Acer")) { b.Brand = "Acer"; b.SupportUrl = "https://www.acer.com/support"; }
        if (b.Maker.Length == 0) b.Maker = sysVendor;
        if (b.Model.Length == 0) b.Model = productName;
        return b;
    }
}

public static class DriverInstallHints
{
    /// <summary>Suggested (never executed) commands for the user's distribution family. Returns generic guidance when unknown.</summary>
    public static string For(string osId, string osIdLike, string vendor)
    {
        string family = $"{osId} {osIdLike}".ToLowerInvariant();
        bool Is(params string[] ids) => ids.Any(i => family.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(i));
        string pkg = vendor switch
        {
            "NVIDIA" when Is("arch", "cachyos", "manjaro", "endeavouros") => "sudo pacman -S nvidia-open-dkms nvidia-utils",
            // Not "ubuntu-drivers install": on Mint 22.3 it chose the prebuilt HWE modules, pulled a new kernel with incomplete headers and left the driver half-installed.
            "NVIDIA" when Is("ubuntu") => L.T("Use a aba Driver NVIDIA do Ilmenyx (DKMS para o kernel em uso, com simulação antes).\nEvite `ubuntu-drivers install`: ele pode puxar um kernel novo com cabeçalhos incompletos."),
            "NVIDIA" when Is("debian") => "sudo apt install nvidia-driver",
            "NVIDIA" when Is("fedora", "rhel") => "sudo dnf install akmod-nvidia   # requer RPM Fusion",
            "AMD" when Is("arch", "cachyos", "manjaro", "endeavouros") => "sudo pacman -S mesa vulkan-radeon",
            "Intel" when Is("arch", "cachyos", "manjaro", "endeavouros") => "sudo pacman -S mesa vulkan-intel intel-media-driver",
            "AMD" or "Intel" when Is("debian", "ubuntu") => "sudo apt install mesa-vulkan-drivers libgl1-mesa-dri",
            "AMD" or "Intel" when Is("fedora", "rhel") => "sudo dnf install mesa-dri-drivers mesa-vulkan-drivers",
            _ => ""
        };
        return pkg.Length > 0 ? L.T($"Sugestão para a sua distribuição (não é executada pelo Ilmenyx; confira a documentação antes):\n{pkg}")
            : L.T("Instale o driver pelo gerenciador de pacotes da sua distribuição; o Ilmenyx não instala drivers.");
    }
}
