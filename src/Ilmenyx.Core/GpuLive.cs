// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions, adapted from the live extras of the original GpuInfo.LiveExtras (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;

namespace Ilmenyx.Core;

/// <summary>One live reading set; null means the driver/sysfs does not expose it (never zero by assumption).</summary>
/// <param name="Integrated">The GPU shares the chip and memory with the CPU: VRAM is the firmware carve-out (UMA), not dedicated memory.</param>
/// <param name="PowerIsPackage">PowerW is the whole package (CPU+GPU), as amdgpu reports power1 on APUs, not the GPU alone.</param>
public sealed record GpuTelemetry(double? LoadPercent, double? TempC, double? TempHotspotC, double? TempMemC, double? CoreMHz, double? MemMHz,
    ulong? VramUsed, ulong? VramTotal, double? PowerW, double? PowerLimitW, double? FanPercent, double? FanRpm, string? PState, ulong? ThrottleMask,
    double? MemControllerPercent, double? EncoderPercent, double? DecoderPercent, int? PcieGen, int? PcieWidth, bool Integrated = false, bool PowerIsPackage = false, double? CoreVoltageV = null, bool Sleeping = false);

public sealed record LiveRow(string Category, string Name, string Value);

public static class GpuLive
{
    private static string F0(double v) => v.ToString("0", CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    public static IReadOnlyList<KeyValuePair<string, string>> Extras(GpuTelemetry t)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (t.PState is { Length: > 0 } p) list.Add(new(L.T("Estado de desempenho"), p));
        if (t.ThrottleMask is { } r) list.Add(new(L.T("PerfCap (limitador)"), GpuCardBuilder.PerfCap(r)));
        if (t.PowerW is { } pw && t.PowerLimitW is > 0) list.Add(new(L.T("Consumo / limite"), $"{F1(pw)} W · {F0(100.0 * pw / t.PowerLimitW.Value)} %"));
        if (t.MemControllerPercent is { } m) list.Add(new(L.T("Controlador de memória"), F0(m) + " %"));
        if (t.EncoderPercent is { } e) list.Add(new("Encoder", F0(e) + " %"));
        if (t.DecoderPercent is { } d) list.Add(new("Decoder", F0(d) + " %"));
        if (t.PcieGen is { } g && t.PcieWidth is { } w) list.Add(new(L.T("PCIe em uso"), $"{GpuCardBuilder.PcieName(g)} x{w}"));
        return list;
    }

    public static IReadOnlyList<LiveRow> Rows(GpuTelemetry t)
    {
        var rows = new List<LiveRow>();
        void Add(string cat, string name, string? value) { if (value is not null) rows.Add(new(cat, name, value)); }
        Add(L.T("Carga · "), "GPU", t.LoadPercent is { } l ? F0(l) + " %" : null);
        Add(L.T("Temperatura · "), L.T("Núcleo"), t.TempC is { } c ? F1(c) + " °C" : null);
        Add(L.T("Temperatura · "), "Hotspot", t.TempHotspotC is { } h ? F1(h) + " °C" : null);
        Add(L.T("Temperatura · "), L.T("Memória"), t.TempMemC is { } m ? F1(m) + " °C" : null);
        Add("Clock · ", L.T("Núcleo"), t.CoreMHz is { } cc ? F0(cc) + " MHz" : null);
        Add("Clock · ", L.T("Memória"), t.MemMHz is { } mm ? F0(mm) + " MHz" : null);
        Add(L.T("Tensão · "), L.T("Núcleo"), t.CoreVoltageV is { } cv ? cv.ToString("0.000", CultureInfo.InvariantCulture) + " V" : null);
        string vram = t.Integrated ? L.T("VRAM reservada · ") : "VRAM · ";
        Add(vram, L.T("Usada"), t.VramUsed is { } u ? (u / 1048576.0).ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".") + " MB" : null);
        Add(vram, "Total", t.VramTotal is { } tt ? (tt / 1048576.0).ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".") + " MB" : null);
        Add(vram, L.T("Ocupação"), t.VramUsed is { } uu && t.VramTotal is > 0 ? F0(100.0 * uu / t.VramTotal.Value) + " %" : null);
        Add(L.T("Energia · "), t.PowerIsPackage ? L.T("Pacote (CPU+GPU)") : L.T("Consumo"), t.PowerW is { } p ? F1(p) + " W" : null);
        Add(L.T("Ventoinha · "), L.T("Velocidade"), t.FanPercent is { } f ? F0(f) + " %" : t.FanRpm is { } rpm ? F0(rpm) + " RPM" : null);
        return rows;
    }
}
