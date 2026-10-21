using System.Text.Json;
using System.Text.Json.Serialization;
using Silex.Audit.Contracts;
namespace Silex.Audit.Core;

public static class SnapshotReport
{
    public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static string Json(GameSimFullPlan plan) => JsonSerializer.Serialize(plan, Options);
    public static Task ExportAsync(GameSimFullPlan plan, string path, CancellationToken ct = default) => ExportDocumentAsync(plan, path, ct);
    public static string Json(GameSimValidationReport report) => JsonSerializer.Serialize(report, Options);
    public static Task ExportAsync(GameSimValidationReport report, string path, CancellationToken ct = default) => ExportDocumentAsync(report, path, ct);
    public static string Json(MemoryValidationReport report) => JsonSerializer.Serialize(report, Options);
    public static Task ExportAsync(MemoryValidationReport report, string path, CancellationToken ct = default) => ExportDocumentAsync(report, path, ct);
    public static string Json(HardwareSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
    public static string Json(SmartDiagnostic snapshot) => JsonSerializer.Serialize(snapshot, Options);
    public static Task ExportAsync(SmartDiagnostic snapshot, string path, CancellationToken ct = default) => ExportDocumentAsync(snapshot, path, ct);
    public static string Json(SensorSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
    public static Task ExportAsync(SensorSnapshot snapshot, string path, CancellationToken ct = default) => ExportDocumentAsync(snapshot, path, ct);
    public static string Json(CpuBenchmarkReport report) => JsonSerializer.Serialize(report, Options);
    public static Task ExportAsync(HardwareSnapshot snapshot, string path, CancellationToken ct = default) => ExportDocumentAsync(snapshot, path, ct);
    public static Task ExportAsync(CpuBenchmarkReport report, string path, CancellationToken ct = default) => ExportDocumentAsync(report, path, ct);
    private static Task ExportDocumentAsync<T>(T snapshot, string path, CancellationToken ct) =>
        ReportFile.WriteAsync(path, (output, token) => JsonSerializer.SerializeAsync(output, snapshot, Options, token), ct);
    public static string Describe<T>(Reading<T> reading) => reading.Status == "available"
        ? $"{reading.Value} {reading.Unit}".TrimEnd() : $"Indisponível: {reading.Reason}";
    public static string Text(HardwareSnapshot s) => $"Silex Audit {s.ApplicationVersion} · {s.CollectedAt:O}\n" +
        $"SO: {Describe(s.OperatingSystem)}\nKernel: {Describe(s.Kernel)}\nCPU: {Describe(s.Cpu.Name)}\n" +
        $"Processadores lógicos visíveis: {Describe(s.Cpu.LogicalProcessors)}\nRAM total: {Describe(s.Memory.Total)}\nRAM disponível: {Describe(s.Memory.Available)}\n" +
        "Discos: " + (s.Disks.Status == "available" ? string.Join("\n", s.Disks.Value!.Select(d => $"{d.Device}: {Describe(d.Capacity)}")) : Describe(s.Disks)) + "\n" + (s.Sensors is { } sensors ? SensorPresentation.Text(sensors) : "Sensores: leitor não configurado.") + "\n" + (s.Gpus is { } gpus ? GpuPresentation.Text(gpus) : "GPU: leitor não configurado.");
}


public static class SensorPresentation
{
    public static string Text(SensorSnapshot s) => $"Sensores Linux · {s.Status} · {s.CollectedAt:O}\n" +
        (s.Readings.Count == 0 ? s.Reason : string.Join("\n", s.Readings.Select(r => $"{r.Device} · {r.Chip} · {r.Label}: {SnapshotReport.Describe(r.Reading)}"))) +
        (s.PowerSupplies is { Count: > 0 } supplies ? "\n" + string.Join("\n", supplies.Select(p => $"{p.Device} · tipo: {SnapshotReport.Describe(p.Type)} · status: {SnapshotReport.Describe(p.Status)} · presente: {SnapshotReport.Describe(p.Present)}")) : "");
}


public static class GpuPresentation
{
    public static string Text(GpuSnapshot s) => $"DRM/PCI · {s.Status}\n" + (s.Devices.Count == 0 ? s.Reason :
        string.Join("\n", s.Devices.Select(g => $"{g.Device}: vendor {SnapshotReport.Describe(g.VendorId)} · device {SnapshotReport.Describe(g.DeviceId)}\nCarga: {SnapshotReport.Describe(g.BusyPercent)} · VRAM total: {SnapshotReport.Describe(g.VramTotal)} · usada: {SnapshotReport.Describe(g.VramUsed)}")));
}
