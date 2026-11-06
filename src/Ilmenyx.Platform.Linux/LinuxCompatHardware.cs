// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>What is compared with ProtonDB reports: GPU names (PCI, reference table, optional Vulkan name), distro and NVIDIA proprietary driver. Read-only.</summary>
public static class LinuxCompatHardware
{
    public static CompatHardware Detect(string? vulkanName = null, string sysRoot = "/sys", string etc = "/etc")
    {
        var gpus = new LinuxGpuCollector(sysRoot).Collect(PciIds.Load());
        // Dedicated GPU first (the collector orders by VRAM); its names plus the reference-table chip ("Mendocino (iGPU · Radeon 610M)").
        var first = gpus.FirstOrDefault();
        string chip = first.Card?.Sections.FirstOrDefault(s => s.Header == "IDENTIFICAÇÃO")?.Children.FirstOrDefault(c => c.Header == "Chip")?.Value ?? "";
        string vendor = first.Card?.Vendor ?? "";
        string text = string.Join(" ", new[] { vendor, first.Raw?.Name, chip == "—" ? null : chip, vulkanName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        bool nvidia = gpus.Any(g => g.Raw.Driver == "nvidia");
        return new(text, OsName(etc), nvidia);
    }

    public static string? OsName(string etc = "/etc")
    {
        foreach (var file in new[] { Path.Combine(etc, "os-release"), "/usr/lib/os-release" })
        {
            string text = SysfsText.Read(file, 8192);
            foreach (var key in new[] { "PRETTY_NAME=", "NAME=" })
                if (text.Split('\n').FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal)) is { } line)
                    return line[key.Length..].Trim().Trim('"');
        }
        return null;
    }
}
