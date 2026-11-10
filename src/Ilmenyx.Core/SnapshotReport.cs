// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text.Json;
using System.Text.Json.Serialization;
using Ilmenyx.Contracts;
namespace Ilmenyx.Core;

public static class SnapshotReport
{
    public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static string Json(SmartImportReport report) => JsonSerializer.Serialize(report, Options);
    public static Task ExportAsync(SmartImportReport report, string path, CancellationToken ct = default) => ExportDocumentAsync(report, path, ct);
    public static Task ExportAsync(DriverInventoryReport report, string path, CancellationToken ct = default) => ExportDocumentAsync(report, path, ct);
    public static string Json(GameSimBenchmarkReport report) => JsonSerializer.Serialize(report, Options);
    public static Task ExportAsync(GameSimBenchmarkReport report, string path, CancellationToken ct = default) => ExportDocumentAsync(report, path, ct);
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
    public static string Describe<T>(Reading<T> reading) => reading.Status != "available" ? L.T($"Indisponível: {reading.Reason}")
        : reading.Value is bool yes ? (yes ? L.T("sim") : L.T("não"))
        : $"{reading.Value} {Unit(reading.Unit)}".TrimEnd();
    // Units are contract values. "logical processors" is already in each label ("Processadores lógicos visíveis: 8").
    private static string Unit(string unit) => unit == "logical processors" ? "" : unit;
    public static string DiskExtra(DiskInfo d) => (d.Kind is { Status: "available" } k ? " · " + k.Value : "") + (d.Bus is { Status: "available" } b ? " · " + b.Value : "");
    public static string Text(HardwareSnapshot s) => $"Ilmenyx Controller · by Ilmenyx Technology · {s.ApplicationVersion} · {s.CollectedAt:O}\n" +
        L.T("SO: ") + Describe(s.OperatingSystem) + $"\nKernel: {Describe(s.Kernel)}\nCPU: {Describe(s.Cpu.Name)}\n" +
        L.T($"Processadores lógicos visíveis: {Describe(s.Cpu.LogicalProcessors)}\nRAM total: {Describe(s.Memory.Total)}\nRAM disponível: {Describe(s.Memory.Available)}\n") +
        L.T("Discos: ") + (s.Disks.Status == "available" ? string.Join("\n", s.Disks.Value!.Select(d => $"{d.Device}: {Describe(d.Capacity)}{DiskExtra(d)}")) : Describe(s.Disks)) + "\n" + (s.Sensors is { } sensors ? SensorPresentation.Text(sensors) : L.T("Sensores: leitor não configurado.")) + "\n" + (s.Gpus is { } gpus ? GpuPresentation.Text(gpus) : L.T("GPU: leitor não configurado."));
}


public static class SensorPresentation
{
    public static string Text(SensorSnapshot s) => L.T($"Sensores Linux · {StatusText.Of(s.Status)} · {s.CollectedAt:O}\n") +
        (s.Readings.Count == 0 ? s.Reason : string.Join("\n", s.Readings.Select(r => $"{r.Device} · {r.Chip} · {r.Label}: {SnapshotReport.Describe(r.Reading)}"))) +
        (s.PowerSupplies is { Count: > 0 } supplies ? "\n" + string.Join("\n", supplies.Select(p => L.T($"{p.Device} · tipo: {SnapshotReport.Describe(p.Type)} · status: {SnapshotReport.Describe(p.Status)} · presente: {SnapshotReport.Describe(p.Present)}"))) : "");
}


public static class GpuPresentation
{
    public static string Text(GpuSnapshot s) => $"DRM/PCI · {StatusText.Of(s.Status)}\n" + (s.Devices.Count == 0 ? s.Reason :
        string.Join("\n", s.Devices.Select(g => L.T($"{g.Device}: vendor {SnapshotReport.Describe(g.VendorId)} · device {SnapshotReport.Describe(g.DeviceId)}\nCarga: {SnapshotReport.Describe(g.BusyPercent)} · VRAM total: {SnapshotReport.Describe(g.VramTotal)} · usada: {SnapshotReport.Describe(g.VramUsed)}"))));
}
