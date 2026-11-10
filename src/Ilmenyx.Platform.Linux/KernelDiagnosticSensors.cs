// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Thermal zones and power supply data, without health heuristics, writes or derived totals.</summary>
public sealed class KernelDiagnosticSensors(IDiagnosticSysfsFiles files, TimeSpan? timeout = null) : ISensorProvider
{
    private readonly TimeSpan timeout = ValidateTimeout(timeout ?? TimeSpan.FromSeconds(2));
    private static TimeSpan ValidateTimeout(TimeSpan value) => value > TimeSpan.Zero && value <= TimeSpan.FromSeconds(5) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public static readonly IReadOnlyList<string> PowerAttributes = Array.AsReadOnly(new[] { "online", "capacity", "voltage_now", "current_now", "power_now", "energy_now", "energy_full", "energy_full_design", "charge_now", "charge_full", "charge_full_design", "cycle_count", "temp" });
    public static string Unit(string attribute) => attribute switch
    {
        "temp" => "°C",
        "capacity" => "%",
        "online" or "present" => "boolean",
        "voltage_now" => "V",
        "current_now" => "A",
        "power_now" => "W",
        "energy_now" or "energy_full" or "energy_full_design" => "Wh",
        "charge_now" or "charge_full" or "charge_full_design" => "Ah",
        "cycle_count" => "cycles",
        _ => ""
    };
    public static Reading<double?> ParsePower(string raw, string attribute, string source)
    {
        string unit = Unit(attribute);
        if (unit.Length == 0 || !long.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || (attribute is "online" or "present" && value is not (0 or 1))
            || (attribute == "capacity" && (value < 0 || value > 100))
            || (attribute == "temp" && (value < -2731 || value > 10000))
            || (attribute is not ("current_now" or "power_now" or "temp") && value < 0))
            return Reading<double?>.Missing(unit, source, L.T("Valor inválido ou fora do domínio da unidade."));
        double divisor = attribute == "temp" ? 10 : attribute is "online" or "present" or "capacity" or "cycle_count" ? 1 : 1000000;
        return Reading<double?>.Available(value / divisor, unit, source);
    }
    private sealed record Attribute(string? Text, string? Error, bool Missing);
    private async Task<Attribute> Read(SysfsArea area, string device, string attribute, CancellationToken ct)
    {
        try { return new(await files.ReadAsync(area, device, attribute, ct), null, false); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return new(null, L.T("Atributo ausente ou dispositivo removido."), true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(null, e is UnauthorizedAccessException ? L.T("Permissão negada.") : L.T("Fonte inacessível ou inválida."), false); }
    }
    public async Task<SensorSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); stop.CancelAfter(timeout);
        var ct = stop.Token;
        var readings = new List<SensorMeasurement>(); var supplies = new List<PowerSupplyInfo>();
        var limitations = new List<string> { L.T("Fontes independentes: thermal pode duplicar hwmon; não somar ou deduplicar por rótulo."), L.T("Energia/carga/potência são dados brutos do kernel; sem cálculo de desgaste, autonomia, saúde ou proteção térmica."), L.T("Atributos opcionais de power_supply ausentes são omitidos; capacidade em % não é saúde. Sem identificadores de fabricante/modelo/serial.") };
        bool incomplete = false;
        try
        {
            foreach (var area in new[] { SysfsArea.Thermal, SysfsArea.PowerSupply })
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<string> devices;
                try { devices = files.Devices(area); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { incomplete = true; limitations.Add(L.T($"{DiagnosticSysfsFiles.Root(area)}: inventário ausente ou inacessível.")); continue; }
                if (devices.Count > 64) { incomplete = true; limitations.Add(L.T($"{area}: inventário limitado a 64 dispositivos.")); }
                foreach (var device in devices.Take(64).Order(StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!DiagnosticSysfsFiles.ValidDevice(area, device)) { incomplete = true; limitations.Add(L.T("Nome sysfs inválido rejeitado.")); continue; }
                    var type = await Read(area, device, "type", ct);
                    var typeReading = type.Text is { } text && LinuxParsers.Clean(text).Length > 0 ? Reading<string>.Available(LinuxParsers.Clean(text), "", Source(area, device, "type")) : Reading<string>.Missing("", Source(area, device, "type"), type.Error ?? L.T("Tipo vazio."));
                    if (typeReading.Status != "available") incomplete = true;
                    string chip = typeReading.Value ?? device;
                    if (area == SysfsArea.Thermal)
                    {
                        var raw = await Read(area, device, "temp", ct);
                        var source = Source(area, device, "temp");
                        Add(area, device, chip, "temp", raw.Text is { } t ? HwmonParser.Parse(t, "temp", source) : Reading<double?>.Missing("°C", source, raw.Error!));
                        continue;
                    }
                    var status = await Read(area, device, "status", ct);
                    var statusReading = status.Text is { } state && state.Trim() is "Unknown" or "Charging" or "Discharging" or "Not charging" or "Full"
                        ? Reading<string>.Available(state.Trim(), "", Source(area, device, "status"))
                        : Reading<string>.Missing("", Source(area, device, "status"), status.Error ?? L.T("Status não reconhecido."));
                    var present = await Read(area, device, "present", ct);
                    var presentReading = present.Text is { } p ? ParsePower(p, "present", Source(area, device, "present")) : Reading<double?>.Missing("boolean", Source(area, device, "present"), present.Error!);
                    supplies.Add(new(device, typeReading, statusReading, presentReading));
                    if ((chip is "Battery" or "UPS" && statusReading.Status != "available") || (!status.Missing && statusReading.Status != "available") || (!present.Missing && presentReading.Status != "available")) incomplete = true;
                    string? blocked = presentReading.Status == "available" && presentReading.Value == 0 ? L.T("Fonte removida/ausente segundo present=0.") : !present.Missing && presentReading.Status != "available" ? L.T("Não foi possível validar present.") : null;
                    int before = readings.Count;
                    foreach (var attribute in PowerAttributes)
                    {
                        ct.ThrowIfCancellationRequested();
                        var raw = await Read(area, device, attribute, ct);
                        if (raw.Missing) continue;
                        string source = Source(area, device, attribute);
                        var value = blocked is not null ? Reading<double?>.Missing(Unit(attribute), source, blocked)
                            : raw.Text is { } t ? ParsePower(t, attribute, source) : Reading<double?>.Missing(Unit(attribute), source, raw.Error!);
                        Add(area, device, chip, attribute, value);
                    }
                    if (readings.Count == before) incomplete = true;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { incomplete = true; limitations.Add(L.T("Timeout thermal/power_supply; snapshot pode estar incompleto.")); }
        cancellationToken.ThrowIfCancellationRequested();
        int available = readings.Count(r => r.Reading.Status == "available") + supplies.Count(s => s.Type.Status == "available");
        string stateResult = available == 0 ? "unavailable" : incomplete || readings.Any(r => r.Reading.Status != "available") ? "partial" : "available";
        return new("1.1", DateTimeOffset.UtcNow, stateResult, stateResult == "available" ? null : L.T("Consulte fontes e limitações: parte do diagnóstico não está disponível."), readings, limitations, supplies);

        void Add(SysfsArea area, string device, string chip, string channel, Reading<double?> value) =>
            readings.Add(new($"{area}/{device}", chip, channel, $"{device} · {channel}", DateTimeOffset.UtcNow, "kernel-raw", value));
    }
    private static string Source(SysfsArea area, string device, string attribute) => $"{DiagnosticSysfsFiles.Root(area)}/{device}/{attribute}";
}
