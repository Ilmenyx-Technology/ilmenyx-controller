// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// GPU live readings as sensor channels (load, temperatures, clocks, power, fan, VRAM): amdgpu/i915 sysfs and, for NVIDIA, the same read-only nvidia-smi query
/// the GPU page uses. It may repeat values already present in hwmon (not deduplicated by design). Absent sources are unavailable, never zero.
/// </summary>
public sealed class GpuTelemetrySensors(Func<CancellationToken, Task<IReadOnlyList<(GpuRaw Raw, GpuTelemetry Telemetry)>>>? read = null) : ISensorProvider
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<(GpuRaw Raw, GpuTelemetry Telemetry)>>> read = read ?? ReadReal;

    public static readonly string NouveauReason = L.T("O driver livre (nouveau) não informa carga, clocks, VRAM e consumo nesta geração. O driver NVIDIA libera esses contadores; veja a página Drivers.");
    public static string MissingReason(GpuRaw raw) => raw.VendorId == "10de" && raw.Driver == "nouveau"
        ? NouveauReason : L.T("Contador não exposto por este driver/GPU.");

    private static async Task<IReadOnlyList<(GpuRaw, GpuTelemetry)>> ReadReal(CancellationToken ct)
    {
        var collector = new LinuxGpuCollector();
        var gpus = await Task.Run(() => collector.Collect(PciIds.Load()), ct);
        var list = new List<(GpuRaw, GpuTelemetry)>();
        foreach (var (raw, _) in gpus.Take(8)) list.Add((raw, await collector.TelemetryAsync(raw, ct)));
        return list;
    }

    public async Task<SensorSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        var limitations = new List<string> { L.T("GPU: leituras do driver (sysfs ou nvidia-smi); contadores ausentes ficam indisponíveis, nunca zero.") };
        try
        {
            var gpus = await read(cancellationToken);
            var now = DateTimeOffset.UtcNow; var list = new List<SensorMeasurement>();
            foreach (var (raw, t) in gpus)
            {
                string device = "gpu:" + raw.PciAddress, source = raw.VendorId == "10de" && raw.Driver != "nouveau" ? "nvidia-smi" : "sysfs (" + (raw.Driver.Length > 0 ? raw.Driver : "drm") + ")";
                void Add(string channel, string label, double? value, string unit)
                {
                    var reading = value is double v ? Reading<double?>.Available(v, unit, source)
                        : Reading<double?>.Missing(unit, source, t.Sleeping ? L.T("GPU dedicada em repouso (economia de energia): o Ilmenyx não a acorda só para ler sensores; os valores voltam quando um jogo ou app usar a GPU.") : MissingReason(raw));
                    list.Add(new(device, raw.Name, channel, label, now, "driver", reading));
                }
                Add("load", L.T("Uso da GPU"), t.LoadPercent, "%"); Add("temp1", L.T("Temperatura do núcleo"), t.TempC, "°C"); Add("temp2", L.T("Temperatura hotspot"), t.TempHotspotC, "°C"); Add("temp3", L.T("Temperatura da memória"), t.TempMemC, "°C");
                Add("freq1", L.T("Clock do núcleo"), t.CoreMHz, "MHz"); Add("freq2", L.T("Clock da memória"), t.MemMHz, "MHz");
                // Channel ids stay stable for exported files; only the labels say when power is the package and VRAM is the UMA carve-out.
                Add("power1", t.PowerIsPackage ? L.T("Consumo do pacote (CPU+GPU)") : L.T("Consumo"), t.PowerW, "W"); Add("fan1", L.T("Ventoinha"), t.FanRpm, "RPM"); Add("fan_pct", L.T("Ventoinha (%)"), t.FanPercent, "%");
                string vram = t.Integrated ? L.T("VRAM reservada") : "VRAM";
                Add("vram_used", vram + " usada", t.VramUsed is ulong u ? u / 1048576.0 : null, "MiB"); Add("vram_total", vram + " total", t.VramTotal is ulong tt ? tt / 1048576.0 : null, "MiB");
            }
            string status = list.Count == 0 ? "unavailable" : list.Any(m => m.Reading.Status != "available") ? "partial" : "available";
            return new("1.1", now, status, list.Count == 0 ? L.T("Nenhuma GPU PCI listada.") : null, list, limitations);
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
        {
            return new("1.1", DateTimeOffset.UtcNow, "unavailable", L.T("Leitura da GPU falhou: ") + e.Message, [], limitations);
        }
    }
}
