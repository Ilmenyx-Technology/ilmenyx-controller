// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Text.Json;
using Ilmenyx.Contracts;

namespace Ilmenyx.Platform.Linux;

/// <summary>Bounded offline stdin analysis. Never instantiates a query or opens a device.</summary>
public static class SmartctlReportImport
{
    public static async Task<SmartImportReport> ReadAsync(string device, TextReader input, CancellationToken ct = default)
    {
        if (!SmartctlQuery.ValidDeviceName(device)) throw new ArgumentException("Nome de disco inválido.");
        ct.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(TimeSpan.FromSeconds(3));
        var token = stop.Token;
        try
        {
            // Console.In may implement asynchronous reads synchronously. Bound the
            // wait too; a noncooperative reader may outlive it until stdin/process ends.
            string json = await Task.Run(() => SmartctlQuery.ReadBounded(input, 65536, token), token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            var report = Parse(device, json);
            token.ThrowIfCancellationRequested();
            return report;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("Timeout de 3 segundos aguardando relatório SMART na entrada padrão."); }
    }

    /// <summary>Reads one user-chosen regular file (explicit opt-in). Rejects links, device/proc/sys paths and non-seekable files.</summary>
    public static async Task<SmartImportReport> ReadFileAsync(string device, string path, CancellationToken ct = default)
    {
        if (!SmartctlQuery.ValidDeviceName(device)) throw new ArgumentException("Nome de disco inválido.");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Informe o caminho completo do relatório.");
        string full = Path.GetFullPath(path);
        foreach (string prefix in new[] { "/dev/", "/proc/", "/sys/" })
            if (full.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("Caminho de sistema não é aceito como relatório SMART.");
        ct.ThrowIfCancellationRequested();
        try
        {
            var reader = await Task.Run(() => Open(full), ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            using (reader) return await ReadAsync(device, reader, ct);
        }
        catch (TimeoutException) { throw new TimeoutException("Timeout de 3 segundos abrindo o relatório SMART."); }
    }
    private static StreamReader Open(string full)
    {
        var info = new FileInfo(full);
        if (info.LinkTarget is not null || !info.Exists) throw new ArgumentException("O relatório deve ser um arquivo regular existente, sem link simbólico.");
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!stream.CanSeek || stream.Length is 0 or > 262144) throw new IOException("Relatório SMART vazio, não regular ou acima do limite.");
            return new StreamReader(stream, System.Text.Encoding.UTF8, false);
        }
        catch { stream.Dispose(); throw; }
    }

    public static SmartImportReport Parse(string device, string json)
    {
        if (!SmartctlQuery.ValidDeviceName(device)) throw new ArgumentException("Nome de disco inválido.");
        if (json.Length > 65536) throw new IOException("Relatório SMART excede limite de 65536 caracteres.");
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (!Unique(root) || !root.TryGetProperty("smartctl", out var tool) || !Unique(tool)
                || !root.TryGetProperty("device", out var source) || !Unique(source)
                || !source.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != "/dev/" + device
                || !tool.TryGetProperty("exit_status", out var status) || !status.TryGetInt32(out int exit) || exit is < 0 or > 255
                || root.TryGetProperty("smart_status", out var smart) && !Unique(smart))
                throw new ArgumentException("Metadados SMART ausentes, duplicados ou incompatíveis com o dispositivo declarado.");
            var diagnostic = SmartctlDiagnostic.Parse(device, new(exit, json));
            return new("1.0", "smart-import", diagnostic.CollectedAt, diagnostic,
                ["Relatório histórico importado; nenhuma consulta ao disco foi executada nesta operação.",
                 "ImportedAt e CollectedAt registram esta análise; data original da coleta não confirmada.",
                 "Entrada fornecida pelo usuário, sem autenticação de origem. Relato do firmware não garante saúde atual.",
                 "Serial, modelo, mensagens, horário bruto e JSON original não são incluídos na saída filtrada."]);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { throw new ArgumentException("Relatório smartctl inválido ou metadados inconsistentes."); }
    }
    private static bool Unique(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject()) if (!names.Add(field.Name)) return false;
        return true;
    }
}
