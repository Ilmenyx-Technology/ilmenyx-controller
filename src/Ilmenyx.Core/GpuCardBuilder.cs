// New Ilmenyx additions, adapted from the sections built by the original GpuInfoCollector.Build (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

/// <summary>Everything the OS reported about one GPU; null/empty when unavailable. Pure data: the card builder does no I/O.</summary>
public sealed record GpuRaw(string Name, string VendorId, string DeviceId, string SubVendorId, string SubDeviceId, string Revision, string PciAddress,
    string Driver, string DriverVersion, string? Vbios, ulong VramBytes, ulong? GttBytes, bool Integrated, int? MaxGen, int? MaxWidth, int? CurGen, int? CurWidth,
    int? MaxCoreMHz, int? MaxMemMHz, int? MaxVideoMHz, int? PowerLimitW, int? PowerDefaultW, int? PowerMinW, int? PowerMaxW, string? ComputeCap, string? CudaVersion,
    IReadOnlyList<string> Apis, string DriverFlags);

public sealed class GpuCard
{
    public string Name { get; init; } = "";
    public string Subtitle { get; set; } = "";
    public string Vendor { get; init; } = "";
    public string VendorId { get; init; } = "";
    public string PciAddress { get; init; } = "";
    public ulong VramBytes { get; init; }
    public List<InfoNode> Sections { get; } = [];
}

public static class GpuCardBuilder
{
    public static string PcieName(int gen) => gen switch { 1 => "PCIe 1.1", 2 => "PCIe 2.0", 3 => "PCIe 3.0", 4 => "PCIe 4.0", 5 => "PCIe 5.0", 6 => "PCIe 6.0", _ => $"PCIe gen {gen}" };

    /// <summary>Gen from a sysfs link speed such as "16.0 GT/s PCIe" (2.5 → 1, 5 → 2, 8 → 3, 16 → 4, 32 → 5, 64 → 6); null when unrecognised.</summary>
    public static int? GenFromLinkSpeed(string text)
    {
        var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null && double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double gt)
            ? gt switch { 2.5 => 1, 5 => 2, 8 => 3, 16 => 4, 32 => 5, 64 => 6, _ => null } : null;
    }

    private static string Bytes(ulong b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"]; double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return v.ToString("0.##", CultureInfo.InvariantCulture) + " " + u[i];
    }

    public static GpuCard Build(GpuRaw r)
    {
        string ven = r.VendorId.ToUpperInvariant().Replace("0X", ""), dev = r.DeviceId.ToUpperInvariant().Replace("0X", "");
        string subVen = r.SubVendorId.ToUpperInvariant().Replace("0X", ""), subDev = r.SubDeviceId.ToUpperInvariant().Replace("0X", "");
        string vendor = ven switch { "10DE" => "NVIDIA", "1002" => "AMD", "8086" => "Intel", _ => ven };
        var spec = GpuSpecDb.Find(ven, dev, r.Name);
        var gpu = new GpuCard { Name = r.Name, Vendor = vendor, VendorId = ven, PciAddress = r.PciAddress, VramBytes = r.VramBytes };
        InfoNode Section(string title) { var n = new InfoNode { Header = title }; gpu.Sections.Add(n); return n; }

        var id = Section("IDENTIFICAÇÃO");
        id.Add("Nome", r.Name); id.Add("Chip", spec?.Chip ?? "—"); id.Add("Arquitetura", spec?.Arch ?? "—");
        id.Add("Revisão", r.Revision.Length > 0 ? r.Revision : "—");
        id.Add("Fabricante da placa", subVen.Length > 0 && subVen != "0000" ? GpuSpecDb.SubvendorName(subVen) : "—");
        id.Add("Device ID", $"{ven} {dev} · {subVen} {subDev}".Trim(' ', '·'));
        id.Add("Processo", spec?.Process ?? "—");
        id.Add("Área do die", spec != null ? $"{spec.DieMm2} mm²" : "—");
        id.Add("Transistores", spec != null ? spec.TransistorsBi.ToString("0.#", CultureInfo.InvariantCulture) + " bilhões" : "—");
        id.Add("Lançamento", spec?.Release ?? "—");

        var units = Section("UNIDADES DE PROCESSAMENTO");
        int shaders = spec?.Shaders ?? 0; bool nv = ven == "10DE"; int perUnit = nv ? 128 : 64;
        if (shaders > 0)
        {
            units.Add(nv ? "CUDA cores" : "Stream processors", shaders.ToString("N0", CultureInfo.InvariantCulture));
            units.Add(nv ? "SMs" : "Compute units", (shaders / perUnit).ToString(CultureInfo.InvariantCulture));
            if (nv && spec!.Arch is "Blackwell" or "Ada Lovelace" or "Ampere") { units.Add("Tensor cores", (shaders / perUnit * 4).ToString(CultureInfo.InvariantCulture)); units.Add("RT cores", (shaders / perUnit).ToString(CultureInfo.InvariantCulture)); }
            else if (!nv && spec!.Arch.StartsWith("RDNA", StringComparison.Ordinal)) units.Add("Ray accelerators", (shaders / perUnit).ToString(CultureInfo.InvariantCulture));
        }
        if (spec != null)
        {
            units.Add("ROPs / TMUs", $"{spec.Rops} / {spec.Tmus}");
            units.Add("Pixel fillrate", (spec.Rops * spec.BoostMHz / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " GPixel/s");
            units.Add("Texture fillrate", (spec.Tmus * spec.BoostMHz / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " GTexel/s");
            units.Add("FP32 (teórico)", (spec.Shaders * 2.0 * spec.BoostMHz / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " TFLOPS");
        }
        if (units.Children.Count == 0) units.Add("Sem dados", "GPU fora do banco de especificações");

        var mem = Section("MEMÓRIA");
        bool igpu = r.Integrated || spec?.MemType.StartsWith("Memória do sistema", StringComparison.Ordinal) == true;
        if (r.VramBytes == 0) mem.Add(igpu ? "Dedicada" : "Tamanho", igpu ? "memória compartilhada com o sistema (VRAM de iGPU não é inferida)" : "indisponível (o driver não expõe)");
        else if (igpu || r.VramBytes < 1UL << 30) mem.Add(igpu ? "Reservada (dedicada)" : "Tamanho", Bytes(r.VramBytes));
        else
        {
            double gib = r.VramBytes / (double)(1UL << 30);
            mem.Add("Tamanho", $"{Math.Ceiling(gib - 0.15):0} GB ({r.VramBytes >> 20:N0} MB utilizáveis)".Replace(",", "."));
        }
        if (r.GttBytes is > 0) mem.Add("Compartilhada do sistema (GTT)", Bytes(r.GttBytes.Value));
        mem.Add("Tipo", spec?.MemType ?? "—");
        if (r.MaxMemMHz is int mc) mem.Add("Clock da memória", $"{mc} MHz ({(mc * 2 / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} Gbps efetivos, conforme o original)");

        var clk = Section("CLOCKS");
        if (spec != null) { clk.Add("Base (referência)", $"{spec.BaseMHz} MHz"); clk.Add("Boost (referência)", $"{spec.BoostMHz} MHz"); }
        if (r.MaxCoreMHz is int g) clk.Add("Máximo do núcleo", $"{g} MHz");
        if (r.MaxVideoMHz is int v) clk.Add("Video engine máx.", $"{v} MHz");
        if (clk.Children.Count == 0) clk.Add("Sem dados", "—");

        var pcie = Section("BARRAMENTO");
        if (r.MaxGen is int mg && r.MaxWidth is int mw) pcie.Add("Interface", $"{PcieName(mg)} x{mw}");
        if (r.CurGen is int cg && r.CurWidth is int cw) pcie.Add("Em uso agora", $"{PcieName(cg)} x{cw}" + (r.MaxGen > r.CurGen ? " (economia de energia em repouso)" : ""));
        if (r.PciAddress.Length > 0) pcie.Add("Localização", "PCI " + r.PciAddress);
        if (pcie.Children.Count == 0) pcie.Add("Sem dados", "—");

        if (r.PowerLimitW != null || spec?.TdpW > 0)
        {
            var pwr = Section("ENERGIA");
            if (spec?.TdpW > 0) pwr.Add("TDP (referência)", $"{spec.TdpW} W");
            if (r.PowerLimitW is int lim) pwr.Add("Limite atual", $"{lim} W");
            if (r.PowerDefaultW is int def) pwr.Add("Limite padrão da placa", $"{def} W");
            if (r.PowerMinW is int pmin && r.PowerMaxW is int pmax) pwr.Add("Faixa ajustável", $"{pmin} – {pmax} W");
        }

        var drv = Section("DRIVER E BIOS");
        drv.Add("BIOS (VBIOS)", r.Vbios ?? "—");
        drv.Add("Driver do kernel", r.Driver.Length > 0 ? r.Driver : "sem driver");
        drv.Add("Versão do driver", r.DriverVersion.Length > 0 ? r.DriverVersion : "—");
        drv.Add("Tipo do módulo", r.DriverFlags.Length > 0 ? r.DriverFlags : "—");
        if (r.CudaVersion is { Length: > 0 } cuda) drv.Add("Versão CUDA", cuda);
        if (r.ComputeCap is { Length: > 0 } cc) drv.Add("Compute capability", cc);

        var tech = Section("TECNOLOGIAS");
        tech.Add("APIs (bibliotecas instaladas)", r.Apis.Count > 0 ? string.Join(" · ", r.Apis) : "—");
        if (spec != null && GpuSpecDb.ArchFeatures.TryGetValue(spec.Arch, out var feats)) tech.Add("Recursos da arquitetura", feats);

        string memLabel = spec?.MemType.StartsWith("GDDR", StringComparison.Ordinal) == true ? spec.MemType : "";
        gpu.Subtitle = string.Join("  ·  ", new[]
        {
            spec?.Chip, spec?.Arch, spec?.Process,
            r.VramBytes >= 1UL << 30 && !igpu ? $"{Math.Ceiling(r.VramBytes / (double)(1UL << 30) - 0.15):0} GB {memLabel}".Trim() : null,
            subVen.Length > 0 && subVen != "0000" ? GpuSpecDb.SubvendorName(subVen) : null
        }.Where(s => !string.IsNullOrEmpty(s)));
        return gpu;
    }

    /// <summary>NVIDIA clocks_throttle_reasons bitmask → text, as the original PerfCap (0x4 power, 0x60 thermal, 0x8 HW slowdown, 0x80 power brake).</summary>
    public static string PerfCap(ulong r)
    {
        var parts = new List<string>();
        if ((r & 0x4) != 0) parts.Add("Energia");
        if ((r & 0x60) != 0) parts.Add("Térmico");
        if ((r & 0x8) != 0) parts.Add("HW slowdown");
        if ((r & 0x80) != 0) parts.Add("Power brake");
        return parts.Count == 0 ? ((r & 0x1) != 0 ? "Ociosa" : "Nenhum") : string.Join(" + ", parts);
    }
}
