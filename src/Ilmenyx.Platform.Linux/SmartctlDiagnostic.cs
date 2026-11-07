// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ilmenyx.Contracts;

namespace Ilmenyx.Platform.Linux;

public sealed record SmartctlOutput(int ExitCode, string Json);
public interface ISmartctlQuery
{
    Task<SmartctlOutput> QueryAsync(string device, CancellationToken ct);
}
/// <summary>One explicit read-only health query. Never enables SMART, runs self-tests or elevates privileges.</summary>
public sealed partial class SmartctlQuery : ISmartctlQuery
{
    [GeneratedRegex("^(sd[a-z]{1,4}|nvme[0-9]{1,4}n[0-9]{1,4})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DevicePattern();
    public static bool ValidDeviceName(string name) => DevicePattern().IsMatch(name);
    public static IReadOnlyList<string> Arguments(string device)
    {
        if (!ValidDeviceName(device)) throw new ArgumentException("Informe somente um nome de disco SATA/NVMe, como sda ou nvme0n1.");
        return ["--json", "--health", "--attributes", "--nocheck=standby,3,5", "--device=" + (device.StartsWith("nvme", StringComparison.Ordinal) ? "nvme" : "ata"), "/dev/" + device];
    }
    public async Task<SmartctlOutput> QueryAsync(string device, CancellationToken ct)
    {
        var arguments = Arguments(device);
        if (!Directory.Exists("/sys/block/" + device)) throw new IOException("Disco não exposto em /sys/block.");
        string executable = new[] { "/usr/sbin/smartctl", "/usr/bin/smartctl", "/sbin/smartctl" }.FirstOrDefault(p => File.Exists(HostSpawn.HostPath(p))) ?? throw new FileNotFoundException("smartctl opcional não encontrado.");
        var start = HostSpawn.Start(executable, arguments, new Dictionary<string, string> { ["LC_ALL"] = "C" });
        using var process = new Process { StartInfo = start };
        ct.ThrowIfCancellationRequested();
        if (!process.Start()) throw new IOException("Não foi possível iniciar smartctl.");
        try
        {
            var output = ReadBounded(process.StandardOutput, 65536, ct);
            var errors = ReadBounded(process.StandardError, 8192, ct);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(ct));
            return new(process.ExitCode, await output);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
    internal static async Task<string> ReadBounded(TextReader reader, int limit, CancellationToken ct)
    {
        var result = new StringBuilder(); var buffer = new char[1024]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (result.Length + count > limit) throw new IOException("Saída de smartctl excede limite.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
public sealed class SmartctlDiagnostic(ISmartctlQuery query)
{
    private static readonly string[] Limitations = ["Consulta explícita somente leitura; sem habilitar SMART, self-test, escrita ou elevação de privilégios.", "Resultado reportedPassed é o relato do firmware, não garantia de saúde. Serial/modelo/JSON bruto não são exportados.", "SATA via ATA e NVMe somente. USB/SCSI/RAID podem estar indisponíveis; sem detecção automática de transporte.", "nocheck solicita evitar acordar discos compatíveis; não há garantia universal de detecção de standby por controladores.", "Métricas opcionais (temperatura, horas, ciclos, desgaste NVMe, erros) são leituras brutas do firmware via smartctl --attributes, sem veredito ou limiar; podem estar ausentes ou ter semântica de fabricante."];
    public async Task<SmartDiagnostic> CollectAsync(string device, CancellationToken cancellationToken = default)
    {
        if (!SmartctlQuery.ValidDeviceName(device)) throw new ArgumentException("Nome de disco inválido.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); stop.CancelAfter(TimeSpan.FromSeconds(3));
        try { var result = await query.QueryAsync(device, stop.Token); cancellationToken.ThrowIfCancellationRequested(); return Parse(device, result); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Missing(device, null, "Timeout de 3 segundos na consulta smartctl."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        { return Missing(device, null, "smartctl ausente, acesso negado ou consulta inacessível; nenhuma elevação solicitada."); }
    }
    public static SmartDiagnostic Parse(string device, SmartctlOutput result)
    {
        if (!SmartctlQuery.ValidDeviceName(device)) throw new ArgumentException("Nome de disco inválido.");
        if (result.ExitCode < 0 || result.ExitCode > 255 || (result.ExitCode & 7) != 0)
            return Missing(device, result.ExitCode, "Consulta recusada, disco em standby, transporte incompatível ou erro de comando. Sem resultado de saúde válido.");
        try
        {
            if (result.Json.Length > 65536) return Missing(device, result.ExitCode, "JSON excede limite.");
            using var doc = JsonDocument.Parse(result.Json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("smartctl", out var tool) || tool.ValueKind != JsonValueKind.Object
                || !tool.TryGetProperty("exit_status", out var code) || !code.TryGetInt32(out int exit) || exit != result.ExitCode
                || !root.TryGetProperty("smart_status", out var status) || status.ValueKind != JsonValueKind.Object
                || !status.TryGetProperty("passed", out var passed) || passed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Missing(device, result.ExitCode, "Resposta sem status SMART booleano consistente.");
            bool value = passed.GetBoolean();
            if (((result.ExitCode & 8) != 0) == value) return Missing(device, result.ExitCode, "Status SMART contradiz o código de saída.");
            return new("1.1", DateTimeOffset.UtcNow, device, "available", null, result.ExitCode, Reading<bool?>.Available(value, "boolean", Source(device)), Limitations, Metrics(root, device));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { return Missing(device, result.ExitCode, "JSON inválido ou sem status SMART utilizável."); }
    }
        internal static IReadOnlyList<SmartMetric> Metrics(JsonElement root, string device)
    {
        var list = new List<SmartMetric>();
        double? Num(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d) ? d : null;
        void Add(string name, double? value, string unit, string source, double min = 0, double max = 1e15)
        { if (value is { } v && v >= min && v <= max && list.Count < 24) list.Add(new(name, v, unit, source)); }
        if (root.TryGetProperty("temperature", out var temperature)) Add("Temperatura", Num(temperature, "current"), "°C", "smartctl:temperature.current", -50, 150);
        if (root.TryGetProperty("power_on_time", out var powerOn)) Add("Horas ligado", Num(powerOn, "hours"), "h", "smartctl:power_on_time.hours");
        Add("Ciclos liga/desliga", Num(root, "power_cycle_count"), "ciclos", "smartctl:power_cycle_count");
        if (device.StartsWith("nvme", StringComparison.Ordinal) && root.TryGetProperty("nvme_smart_health_information_log", out var log) && log.ValueKind == JsonValueKind.Object)
        {
            const string Prefix = "smartctl:nvme_smart_health_information_log.";
            Add("Percentual usado (NVMe)", Num(log, "percentage_used"), "%", Prefix + "percentage_used", 0, 255);
            Add("Reserva disponível (NVMe)", Num(log, "available_spare"), "%", Prefix + "available_spare", 0, 100);
            Add("Erros de mídia", Num(log, "media_errors"), "erros", Prefix + "media_errors");
            Add("Entradas no log de erros", Num(log, "num_err_log_entries"), "entradas", Prefix + "num_err_log_entries");
            Add("Desligamentos inseguros", Num(log, "unsafe_shutdowns"), "eventos", Prefix + "unsafe_shutdowns");
            Add("Aviso crítico (bitmask)", Num(log, "critical_warning"), "bitmask", Prefix + "critical_warning", 0, 255);
            if (Num(log, "data_units_read") is { } read) Add("Total lido", read * 512000, "B", Prefix + "data_units_read");
            if (Num(log, "data_units_written") is { } written) Add("Total gravado", written * 512000, "B", Prefix + "data_units_written");
        }
        else if (root.TryGetProperty("ata_smart_attributes", out var ata) && ata.ValueKind == JsonValueKind.Object && ata.TryGetProperty("table", out var table) && table.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<int>();
            foreach (var attribute in table.EnumerateArray().Take(64))
            {
                if (Num(attribute, "id") is not { } idValue || idValue != Math.Floor(idValue) || !attribute.TryGetProperty("raw", out var raw)) continue;
                int id = (int)idValue;
                string? name = id switch { 5 => "Setores realocados", 197 => "Setores pendentes", 198 => "Setores incorrigíveis (offline)", _ => null };
                if (name is null || !seen.Add(id)) continue;
                Add(name, Num(raw, "value"), "setores", "smartctl:ata_smart_attributes.table[id=" + id + "].raw.value");
            }
        }
        return list;
    }
    private static string Source(string device) => "smartctl --health --json /dev/" + device;
    private static SmartDiagnostic Missing(string device, int? exit, string reason) => new("1.0", DateTimeOffset.UtcNow, device, "unavailable", reason, exit,
        Reading<bool?>.Missing("boolean", Source(device), reason), Limitations);
}
