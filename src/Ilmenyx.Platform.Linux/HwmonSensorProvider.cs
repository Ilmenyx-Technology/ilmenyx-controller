// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public sealed class HwmonSensorProvider : ISensorProvider
{
    private readonly IHwmonFiles files;
    private readonly TimeSpan timeout;
    public HwmonSensorProvider(IHwmonFiles files, TimeSpan? timeout = null)
    {
        this.files = files;
        this.timeout = timeout ?? TimeSpan.FromSeconds(2);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
    }
    private sealed record Attribute(string? Text, string? Error, bool Missing);
    private async Task<Attribute> Read(string device, string attribute, CancellationToken ct)
    {
        try { return new(await files.ReadAsync(device, attribute, ct), null, false); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return new(null, L.T("Atributo ausente ou dispositivo removido."), true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(null, e is UnauthorizedAccessException ? L.T("Permissão negada.") : L.T("Fonte inacessível ou inválida."), false); }
    }
    public async Task<SensorSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(timeout);
        var ct = stop.Token;
        var readings = new List<SensorMeasurement>();
        var limitations = new List<string>
        {
            L.T("Leituras brutas do kernel; sem calibração de placa, inferência de saúde ou controle de hardware."),
            L.T("Sem ajuste por sensors.conf; canais de tensão podem exigir escala específica da placa."),
            L.T("Temperatura em °C validada apenas para coretemp, k10temp, acpitz, nvme e amdgpu; termistores exigem conversão própria."),
            L.T("Identificadores hwmon podem mudar após hotplug/reboot; nenhuma identidade persistente é inferida.")
        };
        bool incomplete = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var devices = files.Devices();
            if (devices.Count > 64) { incomplete = true; limitations.Add(L.T("Inventário limitado a 64 dispositivos.")); }
            foreach (string device in devices.Take(64).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!HwmonParser.ValidDevice(device)) { incomplete = true; limitations.Add(L.T("Nome hwmon inválido rejeitado.")); continue; }
                var name = await Read(device, "name", ct);
                string chip = LinuxParsers.Clean(name.Text ?? device);
                if (chip.Length == 0) chip = device;
                IReadOnlyList<string> inputs;
                try { inputs = files.Inputs(device); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { incomplete = true; limitations.Add(L.T($"{device}: canais inacessíveis ou dispositivo removido.")); continue; }
                if (inputs.Count > 128) { incomplete = true; limitations.Add(L.T($"{device}: inventário limitado a 128 canais.")); }
                foreach (var input in inputs.Take(128).Order(StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!HwmonParser.TryChannel(input, out var channel, out var kind)) continue;
                    if (readings.Count == 512) { incomplete = true; limitations.Add(L.T("Snapshot limitado a 512 sensores.")); break; }
                    var label = await Read(device, channel + "_label", ct);
                    string labelText = LinuxParsers.Clean(label.Text ?? channel);
                    string source = $"/sys/class/hwmon/{device}/{input}";
                    var enable = await Read(device, channel + "_enable", ct);
                    var fault = await Read(device, channel + "_fault", ct);
                    string? reason = enable.Error is not null && !enable.Missing ? enable.Error : fault.Error is not null && !fault.Missing ? fault.Error : null;
                    if (enable.Text is { } enabled && enabled.Trim() != "1") reason = enabled.Trim() == "0" ? L.T("Canal desativado pelo kernel.") : L.T("Indicador enable inválido.");
                    if (fault.Text is { } faulty && faulty.Trim() != "0") reason = faulty.Trim() == "1" ? L.T("Kernel sinalizou falha do sensor.") : L.T("Indicador fault inválido.");
                    if (kind == "temp")
                    {
                        var type = await Read(device, channel + "_type", ct);
                        if (type.Text is { } typeText && typeText.Trim() is not ("1" or "2" or "3" or "4" or "5" or "6")) reason = L.T("Indicador type inválido.");
                        else if (type.Text?.Trim() == "4") reason = L.T("Termistor exige conversão específica; temperatura indisponível.");
                        else if (type.Error is not null && !type.Missing) reason = type.Error;
                        else if (!HwmonParser.KnownTemperatureDriver(chip)) reason = L.T("Conversão de temperatura não validada para esse driver.");
                    }
                    Reading<double?> value;
                    if (reason is not null) value = Reading<double?>.Missing(HwmonParser.Unit(kind), source, reason);
                    else
                    {
                        var raw = await Read(device, input, ct);
                        value = raw.Text is { } text ? HwmonParser.Parse(text, kind, source) : Reading<double?>.Missing(HwmonParser.Unit(kind), source, raw.Error!);
                    }
                    readings.Add(new(device, chip, channel, labelText.Length == 0 ? channel : labelText, DateTimeOffset.UtcNow, "kernel-raw", value));
                }
                if (readings.Count == 512)
                {
                    incomplete = true;
                    limitations.Add(L.T("Limite de 512 sensores atingido; inventário pode estar truncado."));
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { incomplete = true; limitations.Add(L.T("Timeout de coleta hwmon; snapshot pode ser parcial.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { incomplete = true; limitations.Add(e is UnauthorizedAccessException ? L.T("Permissão negada no inventário hwmon.") : L.T("Inventário hwmon ausente ou inacessível.")); }
        cancellationToken.ThrowIfCancellationRequested();
        int available = readings.Count(r => r.Reading.Status == "available");
        string status = available == 0 ? "unavailable" : incomplete || available != readings.Count ? "partial" : "available";
        return new("1.0", DateTimeOffset.UtcNow, status, status == "available" ? null : available == 0
            ? L.T("Nenhuma leitura hwmon suportada disponível; veja causas por canal e limitações.") : L.T("Parte dos canais está indisponível ou o inventário foi limitado."), readings, limitations);
    }
}
