// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Globalization;
using Ilmenyx.Contracts;

namespace Ilmenyx.Platform.Linux;

/// <summary>DRM cards with PCI IDs. Optional AMD sysfs counters, no vendor library or device-node access.</summary>
public sealed class DrmGpuInfoProvider(IDiagnosticSysfsFiles files) : IGpuInfoProvider
{
    public static Reading<string> Hex(string raw, int digits, string source)
    {
        string text = raw.Trim();
        return text.Length == digits + 2 && text.StartsWith("0x", StringComparison.Ordinal) && text.AsSpan(2).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0
            ? Reading<string>.Available(text.ToLowerInvariant(), "", source) : Reading<string>.Missing("", source, "ID PCI hexadecimal inválido.");
    }
    public static Reading<long?> Counter(string raw, string unit, string source)
    {
        if (!long.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || (unit == "%" && value > 100))
            return Reading<long?>.Missing(unit, source, "Contador inválido ou fora do domínio.");
        return Reading<long?>.Available(value, unit, source);
    }
    public async Task<GpuSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); stop.CancelAfter(TimeSpan.FromSeconds(2));
        var ct = stop.Token; var gpus = new List<GpuInfo>();
        var limitations = new List<string> { "Inventário DRM/PCI; sem inferir modelo comercial, desempenho, saúde ou memória compartilhada.", "IDs cardN não são persistentes. Apenas placas DRM expostas; GPU sem driver DRM pode não aparecer.", "Contadores opcionais de carga/VRAM somente para PCI AMD; ausência não vira zero. Temperaturas permanecem nas fontes hwmon/thermal.", "Intel/NVIDIA e outros fabricantes: identificação PCI apenas; sem NVML, busy% ou VRAM garantidos." };
        bool incomplete = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var devices = files.Devices(SysfsArea.Drm);
            if (devices.Count > 16) { incomplete = true; limitations.Add("Inventário limitado a 16 placas DRM."); }
            foreach (var card in devices.Take(16).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!DiagnosticSysfsFiles.ValidDevice(SysfsArea.Drm, card)) { incomplete = true; continue; }
                var vendor = await Id(card, "vendor", 4); var device = await Id(card, "device", 4); var pciClass = await Id(card, "class", 6);
                var total = Reading<long?>.Missing("B", Source(card, "mem_info_vram_total"), "Contador não suportado para esse adaptador.");
                var used = Reading<long?>.Missing("B", Source(card, "mem_info_vram_used"), "Contador não suportado para esse adaptador.");
                var busy = Reading<double?>.Missing("%", Source(card, "gpu_busy_percent"), "Contador não suportado para esse adaptador.");
                if (vendor.Value == "0x1002")
                {
                    total = await Number(card, "mem_info_vram_total", "B"); used = await Number(card, "mem_info_vram_used", "B");
                    var count = await Number(card, "gpu_busy_percent", "%");
                    busy = count.Status == "available" ? Reading<double?>.Available(count.Value, "%", count.Source) : Reading<double?>.Missing("%", count.Source, count.Reason!);
                    if (total.Value is { } t && used.Value is { } u && u > t)
                        used = Reading<long?>.Missing("B", used.Source, "VRAM usada excede total na coleta; amostras não são atômicas.");
                }
                if (vendor.Status != "available" || device.Status != "available" || pciClass.Status != "available") incomplete = true;
                gpus.Add(new(card, vendor, device, pciClass, busy, total, used));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { incomplete = true; limitations.Add("Timeout DRM; inventário pode estar incompleto."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { incomplete = true; limitations.Add("Inventário DRM ausente ou inacessível."); }
        cancellationToken.ThrowIfCancellationRequested();
        string status = gpus.Count == 0 || gpus.All(g => g.VendorId.Status != "available" || g.DeviceId.Status != "available") ? "unavailable" : incomplete ? "partial" : "available";
        return new("1.0", DateTimeOffset.UtcNow, status, status == "available" ? null : "Identificação DRM/PCI ausente ou parcial.", gpus, limitations);

        async Task<Reading<string>> Id(string card, string attribute, int digits)
        {
            var source = Source(card, attribute);
            var r = await Raw(card, attribute);
            return r.Text is { } text ? Hex(text, digits, source) : Reading<string>.Missing("", source, r.Error!);
        }
        async Task<Reading<long?>> Number(string card, string attribute, string unit)
        {
            var source = Source(card, attribute); var r = await Raw(card, attribute);
            return r.Text is { } text ? Counter(text, unit, source) : Reading<long?>.Missing(unit, source, r.Error!);
        }
        async Task<(string? Text, string? Error)> Raw(string card, string attribute)
        {
            try { return (await files.ReadAsync(SysfsArea.Drm, card, "device/" + attribute, ct), null); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { return (null, e is UnauthorizedAccessException ? "Permissão negada." : "Atributo ausente ou inacessível."); }
        }
    }
    private static string Source(string card, string attr) => $"/sys/class/drm/{card}/device/{attr}";
}
