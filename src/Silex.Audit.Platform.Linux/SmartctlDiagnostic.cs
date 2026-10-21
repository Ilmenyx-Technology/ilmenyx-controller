using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Silex.Audit.Contracts;

namespace Silex.Audit.Platform.Linux;

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
        return ["--json", "--health", "--nocheck=standby,3,5", "--device=" + (device.StartsWith("nvme", StringComparison.Ordinal) ? "nvme" : "ata"), "/dev/" + device];
    }
    public async Task<SmartctlOutput> QueryAsync(string device, CancellationToken ct)
    {
        var arguments = Arguments(device);
        if (!Directory.Exists("/sys/block/" + device)) throw new IOException("Disco não exposto em /sys/block.");
        string executable = new[] { "/usr/sbin/smartctl", "/usr/bin/smartctl", "/sbin/smartctl" }.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("smartctl opcional não encontrado.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["LC_ALL"] = "C";
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
    private static readonly string[] Limitations = ["Consulta explícita somente leitura; sem habilitar SMART, self-test, escrita ou elevação de privilégios.", "Resultado reportedPassed é o relato do firmware, não garantia de saúde. Serial/modelo/JSON bruto não são exportados.", "SATA via ATA e NVMe somente. USB/SCSI/RAID podem estar indisponíveis; sem detecção automática de transporte.", "nocheck solicita evitar acordar discos compatíveis; não há garantia universal de detecção de standby por controladores."];
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
            return new("1.0", DateTimeOffset.UtcNow, device, "available", null, result.ExitCode, Reading<bool?>.Available(value, "boolean", Source(device)), Limitations);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { return Missing(device, result.ExitCode, "JSON inválido ou sem status SMART utilizável."); }
    }
    private static string Source(string device) => "smartctl --health --json /dev/" + device;
    private static SmartDiagnostic Missing(string device, int? exit, string reason) => new("1.0", DateTimeOffset.UtcNow, device, "unavailable", reason, exit,
        Reading<bool?>.Missing("boolean", Source(device), reason), Limitations);
}
