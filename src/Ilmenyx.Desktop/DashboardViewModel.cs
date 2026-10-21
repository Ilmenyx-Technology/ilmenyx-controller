using System.ComponentModel;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
namespace Ilmenyx.Desktop;

public sealed class DashboardViewModel(IHardwareInfoProvider provider) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public HardwareSnapshot? Snapshot { get; private set; }
    public string Cpu => Snapshot is { } s ? SnapshotReport.Describe(s.Cpu.Name) + "\n" + (s.Cpu.LogicalProcessors.Status == "available" ? $"{s.Cpu.LogicalProcessors.Value} processadores lógicos visíveis" : SnapshotReport.Describe(s.Cpu.LogicalProcessors)) : "Aguardando coleta";
    public string Memory => Snapshot is { } s ? "Total: " + Bytes(s.Memory.Total) + "\nDisponível: " + Bytes(s.Memory.Available) : "Aguardando coleta";
    public string System => Snapshot is { } s ? SnapshotReport.Describe(s.OperatingSystem) + "\n" + SnapshotReport.Describe(s.Kernel) : "Aguardando coleta";
    public string Disks => Snapshot is { } s ? (s.Disks.Status == "available" ? string.Join("\n", s.Disks.Value!.Select(d => d.Device + " · " + Bytes(d.Capacity))) : SnapshotReport.Describe(s.Disks)) : "Aguardando coleta";
    public string Gpus => Snapshot?.Gpus is { } g ? GpuPresentation.Text(g) : "Inventário DRM indisponível.";
    private static string Bytes(Reading<long?> reading) => reading.Status == "available" && reading.Value is { } value ? $"{value / 1073741824.0:0.00} GiB ({value} B)" : SnapshotReport.Describe(reading);
    public string Status { get; private set; } = "Pronto";
    public void SetStatus(string text) { Status = text; PropertyChanged?.Invoke(this, new(nameof(Status))); }
    public async Task RefreshAsync(CancellationToken ct)
    {
        SetStatus("Lendo /proc e /sys…");
        try
        {
            Snapshot = await Task.Run(() => provider.CollectAsync(ct), ct);
            foreach (var name in new[] { nameof(Cpu), nameof(Memory), nameof(System), nameof(Disks), nameof(Gpus) }) PropertyChanged?.Invoke(this, new(name));
            SetStatus($"Snapshot: {Snapshot.CollectedAt.LocalDateTime:G} · somente leitura");
        }
        catch (OperationCanceledException) { SetStatus("Coleta cancelada ou timeout. Último snapshot preservado."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatus("Coleta indisponível: " + e.Message); }
    }
}
