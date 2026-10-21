using System.Globalization;
using System.Net;
using System.Text;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

public static class ValidationNotices
{
    public const string AmdNvidia = "AMD/NVIDIA · Testes não realizados em hardware real";
}

/// <summary>Standalone, passive HTML. All data is encoded as text; no untrusted attributes or markup.</summary>
public static class HardwareHtmlReport
{
    public const string FormatVersion = "1.0";
    public static Task ExportAsync(HardwareSnapshot snapshot, string path, CancellationToken ct = default) =>
        ReportFile.WriteAsync(path, async (output, token) =>
        {
            var html = Render(snapshot, token);
            await using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true);
            await writer.WriteAsync(html.AsMemory(), token);
            await writer.FlushAsync(token);
        }, ct);

    public static string Render(HardwareSnapshot snapshot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var html = new StringBuilder(16384);
        html.Append("""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <title>Ilmenyx — Relatório de hardware</title>
            <style>
            :root{color-scheme:dark}*{box-sizing:border-box}body{margin:0;background:#111315;color:#ededf0;font:15px system-ui,sans-serif;line-height:1.5}
            main{max-width:1400px;margin:auto;padding:32px}h1{font-size:32px;margin-bottom:4px}h2{font-size:21px;margin-top:0}
            section{background:#24252e;border-radius:12px;padding:22px;margin:24px 0}.muted{color:#c4c4cf}.notice{padding:12px;background:#3a3020;color:#f3ce85;border-radius:6px}
            .table-wrap{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:13px}th,td{padding:10px;text-align:left;vertical-align:top;border-bottom:1px solid #454552;overflow-wrap:anywhere;min-width:90px}th{color:#c4c4cf}
            li{margin:8px 0}dl{display:grid;grid-template-columns:minmax(140px,1fr) 3fr;gap:8px}dt{color:#c4c4cf}dd{margin:0;overflow-wrap:anywhere}
            @media(max-width:600px){main{padding:12px}section{padding:12px}dl{display:block}dd{margin-bottom:10px}}
            @media print{:root{color-scheme:light}body,section{background:white;color:black}.muted,th,dt{color:#333}.notice{background:#eee;color:black}main{padding:0}.table-wrap{overflow:visible}th,td{min-width:0;padding:5px}}
            </style></head><body><main><header><h1>Ilmenyx</h1><p>by Bismuth Technology</p><p class="muted">Relatório de hardware · arquivo offline</p></header>
            <p class="notice">Snapshot de diagnóstico. Dados não indicam saúde ou desempenho. Nenhum benchmark é executado para gerar este relatório.</p>
            """);
        html.Append("<section><h2>Proveniência</h2><dl>");
        Metadata("Formato HTML", FormatVersion); Metadata("Schema de hardware", snapshot.SchemaVersion);
        Metadata("Aplicação", snapshot.ApplicationVersion); Metadata("Coleta UTC", snapshot.CollectedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Metadata("Runtime", snapshot.Runtime);
        html.Append("</dl><p>Sem coleta adicional de hostname, serial, UUID, IP ou machine-id. Fontes do kernel são registradas; padrões óbvios de identificadores em texto livre podem ser redigidos. Isso não garante anonimização.</p></section>");
        Table("Sistema, CPU e memória");
        AddReading("Sistema operacional", snapshot.OperatingSystem); AddReading("Kernel", snapshot.Kernel);
        AddReading("CPU", snapshot.Cpu.Name); AddReading("Processadores lógicos visíveis", snapshot.Cpu.LogicalProcessors);
        AddReading("RAM total", snapshot.Memory.Total); AddReading("RAM disponível", snapshot.Memory.Available); EndTable();
        Table("Dispositivos de bloco — capacidade, não espaço livre");
        if (snapshot.Disks.Status == "available" && snapshot.Disks.Value is { Count: > 0 } disks)
            foreach (var disk in disks) AddReading(disk.Device, disk.Capacity);
        else AddReading("Dispositivos de bloco", Reading<string>.Missing("devices", snapshot.Disks.Source, snapshot.Disks.Reason ?? "Nenhum dispositivo informado."));
        EndTable();
        html.Append("<section><h2>GPU · DRM/PCI</h2><p class=\"notice\">").Append(E(ValidationNotices.AmdNvidia)).Append("</p>");
        if (snapshot.Gpus is { } gpus)
        {
            MetadataParagraph("Estado do inventário", gpus.Status); MetadataParagraph("Coleta UTC", gpus.CollectedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            html.Append("</section>"); Table("Leituras GPU");
            if (gpus.Devices.Count == 0) Row("GPU", "Indisponível", "", gpus.Status, "/sys/class/drm", "", "", gpus.Reason ?? "Nenhuma placa informada.");
            foreach (var gpu in gpus.Devices)
            {
                AddReading(gpu.Device + " · vendor PCI", gpu.VendorId); AddReading(gpu.Device + " · device PCI", gpu.DeviceId); AddReading(gpu.Device + " · classe PCI", gpu.PciClass);
                AddReading(gpu.Device + " · carga", gpu.BusyPercent); AddReading(gpu.Device + " · VRAM total", gpu.VramTotal); AddReading(gpu.Device + " · VRAM usada", gpu.VramUsed);
            }
            EndTable(); Limitations("Limitações GPU", gpus.Limitations);
        }
        else html.Append("<p>Inventário GPU indisponível: leitor não configurado.</p></section>");
        if (snapshot.Sensors is { } sensors)
        {
            Table("Sensores · hwmon / thermal / energia");
            if (sensors.Readings.Count == 0) Row("Sensores", "Indisponível", "", sensors.Status, "", "", "", sensors.Reason ?? "Nenhuma leitura informada.");
            foreach (var sensor in sensors.Readings)
                AddReading($"{sensor.Device} · {sensor.Chip} · {sensor.Label}", sensor.Reading, sensor.CollectedAt, sensor.Quality);
            EndTable();
            html.Append("<section><h2>Metadados de sensores</h2><dl>");
            Metadata("Schema", sensors.SchemaVersion); Metadata("Estado agregado", sensors.Status); Metadata("Motivo", sensors.Reason ?? "");
            Metadata("Coleta UTC", sensors.CollectedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); html.Append("</dl></section>");
            Table("Bateria e alimentação — dados brutos");
            if (sensors.PowerSupplies is not { Count: > 0 }) Row("Energia", "Indisponível", "", "unavailable", "/sys/class/power_supply", "", "", "Nenhuma fonte de energia informada.");
            foreach (var supply in sensors.PowerSupplies ?? [])
            {
                AddReading(supply.Device + " · tipo", supply.Type); AddReading(supply.Device + " · status", supply.Status); AddReading(supply.Device + " · presente", supply.Present);
            }
            EndTable(); Limitations("Limitações de sensores", sensors.Limitations);
        }
        else html.Append("<section><h2>Sensores</h2><p>Indisponível: leitor não configurado.</p></section>");
        html.Append("<section><h2>Capacidades</h2><div class=\"table-wrap\"><table><thead><tr><th>Recurso</th><th>Estado</th><th>Motivo</th></tr></thead><tbody>");
        foreach (var capability in snapshot.Capabilities) Row(capability.Name, capability.Status, capability.Reason);
        EndTable(); Limitations("Limitações do snapshot", snapshot.Limitations);
        html.Append("<footer><p>Ilmenyx é uma adaptação independente e não oficial de RESET EXPLODE, de Gustavo (GustavoReset), MIT. by Bismuth Technology. Sem afiliação, patrocínio ou endosso da equipe RESET.</p><p>Revise antes de compartilhar: fontes, rótulos livres e configuração podem identificar a máquina. Não é anonimização garantida.</p><p>SMART não é consultado na exportação: diagnóstico separado na CLI por opt-in. Scores Linux/Windows não têm comparabilidade validada.</p></footer></main></body></html>\n");
        return html.ToString();

        void Metadata(string key, string value) => html.Append("<dt>").Append(E(key)).Append("</dt><dd>").Append(E(value)).Append("</dd>");
        void MetadataParagraph(string key, string value) => html.Append("<p>").Append(E(key)).Append(": ").Append(E(value)).Append("</p>");
        void Table(string title) => html.Append("<section><h2>").Append(E(title)).Append("</h2><div class=\"table-wrap\"><table><thead><tr><th>Item</th><th>Valor</th><th>Unidade</th><th>Estado</th><th>Fonte</th><th>Coleta UTC</th><th>Qualidade</th><th>Motivo</th></tr></thead><tbody>");
        void EndTable() => html.Append("</tbody></table></div></section>");
        void Row(params string[] cells)
        {
            ct.ThrowIfCancellationRequested(); html.Append("<tr>");
            foreach (string cell in cells) html.Append("<td>").Append(E(cell)).Append("</td>");
            html.Append("</tr>");
        }
        void AddReading<T>(string label, Reading<T> reading, DateTimeOffset? collected = null, string quality = "")
        {
            string value = reading.Status == "available" && reading.Value is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "Indisponível";
            Row(label, value, reading.Unit, reading.Status, reading.Source, (collected ?? snapshot.CollectedAt).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), quality, reading.Reason ?? "");
        }
        void Limitations(string title, IReadOnlyList<string> items)
        {
            html.Append("<section><h2>").Append(E(title)).Append("</h2><ul>");
            foreach (var item in items) { ct.ThrowIfCancellationRequested(); html.Append("<li>").Append(E(item)).Append("</li>"); }
            html.Append("</ul></section>");
        }
    }
    private static string E(string value) => WebUtility.HtmlEncode(value);
}
