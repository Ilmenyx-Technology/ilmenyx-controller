// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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
    public static string PcieName(int gen) => gen switch { 1 => L.T("PCIe 1.1"), 2 => L.T("PCIe 2.0"), 3 => L.T("PCIe 3.0"), 4 => L.T("PCIe 4.0"), 5 => L.T("PCIe 5.0"), 6 => L.T("PCIe 6.0"), _ => L.T($"PCIe gen {gen}") };

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

        var id = Section(L.T("IDENTIFICAÇÃO"));
        id.Add(L.T("Nome"), r.Name); id.Add("Chip", spec?.Chip ?? "—"); id.Add(L.T("Arquitetura"), spec?.Arch ?? "—");
        id.Add(L.T("Revisão"), r.Revision.Length > 0 ? r.Revision : "—");
        id.Add(L.T("Fabricante da placa"), subVen.Length > 0 && subVen != "0000" ? GpuSpecDb.SubvendorName(subVen) : "—");
        id.Add("Device ID", $"{ven} {dev} · {subVen} {subDev}".Trim(' ', '·'));
        id.Add(L.T("Processo"), spec?.Process ?? "—");
        // 0 in the reference table means "not published": shown as unknown, never as 0.
        id.Add(L.T("Área do die"), spec is { DieMm2: > 0 } ? $"{spec.DieMm2} mm²" : "—");
        id.Add(L.T("Transistores"), spec is { TransistorsBi: > 0 } ? spec.TransistorsBi.ToString("0.#", CultureInfo.InvariantCulture) + L.T(" bilhões") : "—");
        id.Add(L.T("Lançamento"), spec?.Release ?? "—");

        var units = Section(L.T("UNIDADES DE PROCESSAMENTO"));
        int shaders = spec?.Shaders ?? 0; bool nv = ven == "10DE"; int perUnit = nv ? 128 : 64;
        if (shaders > 0)
        {
            units.Add(nv ? L.T("CUDA cores") : "Stream processors", shaders.ToString("N0", CultureInfo.InvariantCulture));
            units.Add(nv ? L.T("SMs") : "Compute units", (shaders / perUnit).ToString(CultureInfo.InvariantCulture));
            if (nv && spec!.Arch is "Blackwell" or "Ada Lovelace" or "Ampere") { units.Add("Tensor cores", (shaders / perUnit * 4).ToString(CultureInfo.InvariantCulture)); units.Add("RT cores", (shaders / perUnit).ToString(CultureInfo.InvariantCulture)); }
            else if (!nv && spec!.Arch.StartsWith("RDNA", StringComparison.Ordinal)) units.Add("Ray accelerators", (shaders / perUnit).ToString(CultureInfo.InvariantCulture));
        }
        if (spec != null)
        {
            units.Add(L.T("ROPs / TMUs"), $"{spec.Rops} / {spec.Tmus}");
            units.Add(L.T("Pixel fillrate"), (spec.Rops * spec.BoostMHz / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " GPixel/s");
            units.Add(L.T("Texture fillrate"), (spec.Tmus * spec.BoostMHz / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " GTexel/s");
            units.Add(L.T("FP32 (teórico)"), (spec.Shaders * 2.0 * spec.BoostMHz / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " TFLOPS");
        }
        if (units.Children.Count == 0) units.Add(L.T("Sem dados"), L.T("GPU fora do banco de especificações"));

        var mem = Section(L.T("MEMÓRIA"));
        bool igpu = r.Integrated || spec?.MemType.StartsWith(L.T("Memória do sistema"), StringComparison.Ordinal) == true;
        if (r.VramBytes == 0) mem.Add(igpu ? L.T("Dedicada") : L.T("Tamanho"), igpu ? L.T("memória compartilhada com o sistema (VRAM de iGPU não é inferida)") : L.T("indisponível (o driver não expõe)"));
        else if (igpu || r.VramBytes < 1UL << 30) mem.Add(igpu ? L.T("Reservada (dedicada)") : L.T("Tamanho"), Bytes(r.VramBytes));
        else
        {
            double gib = r.VramBytes / (double)(1UL << 30);
            mem.Add(L.T("Tamanho"), L.T($"{Math.Ceiling(gib - 0.15):0} GB ({r.VramBytes >> 20:N0} MB utilizáveis)").Replace(",", "."));
        }
        if (r.GttBytes is > 0) mem.Add(L.T("Compartilhada do sistema (GTT)"), Bytes(r.GttBytes.Value));
        mem.Add(L.T("Tipo"), spec?.MemType ?? "—");
        if (r.MaxMemMHz is int mc) mem.Add(L.T("Clock da memória"), L.T($"{mc} MHz ({(mc * 2 / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} Gbps efetivos, conforme o original)"));

        var clk = Section("CLOCKS");
        if (spec != null) { clk.Add(L.T("Base (referência)"), $"{spec.BaseMHz} MHz"); clk.Add(L.T("Boost (referência)"), $"{spec.BoostMHz} MHz"); }
        if (r.MaxCoreMHz is int g) clk.Add(L.T("Máximo do núcleo"), $"{g} MHz");
        if (r.MaxVideoMHz is int v) clk.Add(L.T("Video engine máx."), $"{v} MHz");
        if (clk.Children.Count == 0) clk.Add(L.T("Sem dados"), "—");

        var pcie = Section(L.T("BARRAMENTO"));
        // An APU reports the link to its own GPU block: it is internal to the chip, not a slot (seen on a real Radeon 610M: "PCIe 4.0 x16").
        if (r.MaxGen is int mg && r.MaxWidth is int mw) pcie.Add("Interface", $"{PcieName(mg)} x{mw}" + (igpu ? L.T(" (ligação interna do chip, não é um slot)") : ""));
        if (r.CurGen is int cg && r.CurWidth is int cw) pcie.Add(L.T("Em uso agora"), $"{PcieName(cg)} x{cw}" + (r.MaxGen > r.CurGen ? L.T(" (economia de energia em repouso)") : ""));
        if (r.PciAddress.Length > 0) pcie.Add(L.T("Localização"), "PCI " + r.PciAddress);
        if (pcie.Children.Count == 0) pcie.Add(L.T("Sem dados"), "—");

        if (r.PowerLimitW != null || spec?.TdpW > 0)
        {
            var pwr = Section(L.T("ENERGIA"));
            if (spec?.TdpW > 0) pwr.Add(L.T("TDP (referência)"), $"{spec.TdpW} W");
            if (r.PowerLimitW is int lim) pwr.Add(L.T("Limite atual"), $"{lim} W");
            if (r.PowerDefaultW is int def) pwr.Add(L.T("Limite padrão da placa"), $"{def} W");
            if (r.PowerMinW is int pmin && r.PowerMaxW is int pmax) pwr.Add(L.T("Faixa ajustável"), $"{pmin} – {pmax} W");
        }

        var drv = Section(L.T("DRIVER E BIOS"));
        drv.Add(L.T("BIOS (VBIOS)"), r.Vbios ?? "—");
        drv.Add(L.T("Driver do kernel"), r.Driver.Length > 0 ? r.Driver : L.T("sem driver"));
        drv.Add(L.T("Versão do driver"), r.DriverVersion.Length > 0 ? r.DriverVersion : "—");
        drv.Add(L.T("Tipo do módulo"), r.DriverFlags.Length > 0 ? r.DriverFlags : "—");
        if (r.CudaVersion is { Length: > 0 } cuda) drv.Add(L.T("Versão CUDA"), cuda);
        if (r.ComputeCap is { Length: > 0 } cc) drv.Add("Compute capability", cc);

        var tech = Section(L.T("TECNOLOGIAS"));
        tech.Add(L.T("APIs (bibliotecas instaladas)"), r.Apis.Count > 0 ? string.Join(" · ", r.Apis) : "—");
        if (spec != null && GpuSpecDb.ArchFeatures.TryGetValue(spec.Arch, out var feats)) tech.Add(L.T("Recursos da arquitetura"), feats);

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
        if ((r & 0x4) != 0) parts.Add(L.T("Energia"));
        if ((r & 0x60) != 0) parts.Add(L.T("Térmico"));
        if ((r & 0x8) != 0) parts.Add("HW slowdown");
        if ((r & 0x80) != 0) parts.Add("Power brake");
        return parts.Count == 0 ? ((r & 0x1) != 0 ? L.T("Ociosa") : L.T("Nenhum")) : string.Join(" + ", parts);
    }
}
