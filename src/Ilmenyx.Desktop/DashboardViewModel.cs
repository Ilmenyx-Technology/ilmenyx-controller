// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.ComponentModel;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
namespace Ilmenyx.Desktop;

public sealed class DashboardViewModel(IHardwareInfoProvider provider) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public HardwareSnapshot? Snapshot { get; private set; }
    public string Cpu => Snapshot is { } s ? SnapshotReport.Describe(s.Cpu.Name) + "\n" + (s.Cpu.LogicalProcessors.Status == "available" ? L.T($"{s.Cpu.LogicalProcessors.Value} processadores lógicos visíveis") : SnapshotReport.Describe(s.Cpu.LogicalProcessors)) : L.T("Aguardando coleta");
    public string Memory => Snapshot is { } s ? "Total: " + Bytes(s.Memory.Total) + L.T("\nDisponível: ") + Bytes(s.Memory.Available) : L.T("Aguardando coleta");
    public string System => Snapshot is { } s ? SnapshotReport.Describe(s.OperatingSystem) + "\n" + SnapshotReport.Describe(s.Kernel) : L.T("Aguardando coleta");
    public string Disks => Snapshot is { } s ? (s.Disks.Status == "available" ? string.Join("\n", s.Disks.Value!.Select(d => d.Device + " · " + Bytes(d.Capacity) + SnapshotReport.DiskExtra(d))) : SnapshotReport.Describe(s.Disks)) : L.T("Aguardando coleta");
    public string Gpus => Snapshot?.Gpus is { } g ? GpuPresentation.Text(g) : L.T("Inventário DRM indisponível.");
    private static string Bytes(Reading<long?> reading) => reading.Status == "available" && reading.Value is { } value ? $"{value / 1073741824.0:0.00} GiB ({value} B)" : SnapshotReport.Describe(reading);
    public string Status { get; private set; } = L.T("Pronto");
    public void SetStatus(string text) { Status = text; PropertyChanged?.Invoke(this, new(nameof(Status))); }
    public async Task RefreshAsync(CancellationToken ct)
    {
        SetStatus(L.T("Lendo /proc e /sys…"));
        try
        {
            Snapshot = await Task.Run(() => provider.CollectAsync(ct), ct);
            foreach (var name in new[] { nameof(Cpu), nameof(Memory), nameof(System), nameof(Disks), nameof(Gpus) }) PropertyChanged?.Invoke(this, new(name));
            SetStatus(L.T($"Snapshot: {Snapshot.CollectedAt.LocalDateTime:G} · somente leitura"));
        }
        catch (OperationCanceledException) { SetStatus(L.T("Coleta cancelada ou timeout. Último snapshot preservado.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatus(L.T("Coleta indisponível: ") + e.Message); }
    }
}
