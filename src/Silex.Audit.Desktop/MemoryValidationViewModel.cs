using System.ComponentModel;
using Silex.Audit.Contracts;
using Silex.Audit.Core;

namespace Silex.Audit.Desktop;

public sealed class MemoryValidationViewModel(IHardwareInfoProvider hardware, IMemoryValidationRunner runner) : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? active;
    private bool disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsRunning { get; private set; }
    public bool IsExporting { get; private set; }
    public bool CanStart => !disposed && !IsRunning && !IsExporting;
    public bool CanCancel => !disposed && IsRunning && active is { IsCancellationRequested: false };
    public bool CanExport => !disposed && !IsRunning && !IsExporting && Report is not null;
    public MemoryValidationReport? Report { get; private set; }
    public string Status { get; private set; } = "Pronto. Validação pequena; não mede desempenho da RAM.";
    public string Summary => Report is { } report
        ? $"{Label(report.Validation.Status)} · {report.Validation.CompletedTraversalSteps}/{report.Validation.RequestedTraversalSteps} passos · sem score\nSoma leitura/cópia: {report.Validation.ReadSum?.ToString() ?? "indisponível"}/{report.Validation.CopySum?.ToString() ?? "indisponível"}\nSHA256 cópia: {report.Validation.CopySha256 ?? "indisponível"}"
        : "Nenhuma validação executada. Sem latência, GB/s ou score de RAM.";
    public async Task StartAsync(CancellationToken lifetime = default)
    {
        if (!CanStart) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime); active = stop;
        IsRunning = true; Report = null; Status = "Coletando contexto de hardware…"; NotifyAll();
        try
        {
            var snapshot = await Task.Run(() => hardware.CollectAsync(stop.Token), stop.Token);
            stop.Token.ThrowIfCancellationRequested(); SetStatus("Validando cadeia, soma e cópia em buffers pequenos…");
            var result = await runner.RunAsync(new(), stop.Token);
            if (disposed) return;
            Report = new("1.0", snapshot, result);
            SetStatus(Label(result.Status) + ". Correção de algoritmos; sem medição de desempenho.");
        }
        catch (OperationCanceledException) { SetStatus(stop.IsCancellationRequested ? "Validação cancelada." : "Coleta de hardware atingiu timeout."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or AggregateException)
        { SetStatus("Validação indisponível: " + e.Message); }
        finally { active = null; IsRunning = false; NotifyAll(); }
    }
    public void Cancel()
    {
        if (!CanCancel || active is not { } stop) return;
        SetStatus("Cancelando validação…"); NotifyAll();
        // Cancellation can complete an awaited runner synchronously. Publish the request first,
        // so its final cancelled status is never overwritten by a late "cancelling" message.
        stop.Cancel();
    }
    public async Task ExportAsync(string path, CancellationToken ct = default)
    {
        if (!CanExport || Report is not { } report) return;
        IsExporting = true; NotifyAll();
        try { await SnapshotReport.ExportAsync(report, path, ct); SetStatus("Validação de memória exportada para arquivo local."); }
        catch (OperationCanceledException) { SetStatus("Exportação cancelada."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetStatus("Não foi possível exportar. Escolha um arquivo novo: " + e.Message); }
        finally { IsExporting = false; NotifyAll(); }
    }
    public void SetStatus(string status) { if (!disposed) { Status = status; Notify(nameof(Status)); } }
    public void Dispose() { if (disposed) return; disposed = true; active?.Cancel(); }
    private static string Label(MemoryValidationStatus state) => state switch
    { MemoryValidationStatus.Completed => "Concluída", MemoryValidationStatus.Cancelled => "Cancelada", MemoryValidationStatus.TimedOut => "Timeout", _ => "Estado desconhecido" };
    private void Notify(string name) { if (!disposed) PropertyChanged?.Invoke(this, new(name)); }
    private void NotifyAll()
    {
        foreach (string name in new[] { nameof(IsRunning), nameof(IsExporting), nameof(CanStart), nameof(CanCancel), nameof(CanExport), nameof(Report), nameof(Status), nameof(Summary) }) Notify(name);
    }
}
