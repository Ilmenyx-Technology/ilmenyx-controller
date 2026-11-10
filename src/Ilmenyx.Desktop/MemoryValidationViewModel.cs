// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.ComponentModel;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

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
    public string Status { get; private set; } = L.T("Pronto. Validação pequena; não mede desempenho da RAM.");
    public string Summary => Report is { } report
        ? L.T($"{Label(report.Validation.Status)} · {report.Validation.CompletedTraversalSteps}/{report.Validation.RequestedTraversalSteps} passos · sem score\nSoma leitura/cópia: {report.Validation.ReadSum?.ToString() ?? L.T("indisponível")}/{report.Validation.CopySum?.ToString() ?? L.T("indisponível")}\nSHA256 cópia: {report.Validation.CopySha256 ?? L.T("indisponível")}")
        : L.T("Nenhuma validação executada. Sem latência, GB/s ou score de RAM.");
    public async Task StartAsync(CancellationToken lifetime = default)
    {
        if (!CanStart) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime); active = stop;
        IsRunning = true; Report = null; Status = L.T("Coletando contexto de hardware…"); NotifyAll();
        try
        {
            var snapshot = await Task.Run(() => hardware.CollectAsync(stop.Token), stop.Token);
            stop.Token.ThrowIfCancellationRequested(); SetStatus(L.T("Validando cadeia, soma e cópia em buffers pequenos…"));
            var result = await runner.RunAsync(new(), stop.Token);
            if (disposed) return;
            Report = new("1.0", snapshot, result);
            SetStatus(Label(result.Status) + L.T(". Correção de algoritmos; sem medição de desempenho."));
        }
        catch (OperationCanceledException) { SetStatus(stop.IsCancellationRequested ? L.T("Validação cancelada.") : L.T("Coleta de hardware atingiu timeout.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or AggregateException)
        { SetStatus(L.T("Validação indisponível: ") + e.Message); }
        finally { active = null; IsRunning = false; NotifyAll(); }
    }
    public void Cancel()
    {
        if (!CanCancel || active is not { } stop) return;
        SetStatus(L.T("Cancelando validação…")); NotifyAll();
        // Cancellation can complete an awaited runner synchronously. Publish the request first,
        // so its final cancelled status is never overwritten by a late "cancelling" message.
        stop.Cancel();
    }
    public Task ExportAsync(string path, CancellationToken ct = default) => ExportReportAsync(path, false, ct);
    public Task ExportHtmlAsync(string path, CancellationToken ct = default) => ExportReportAsync(path, true, ct);
    private async Task ExportReportAsync(string path, bool html, CancellationToken ct)
    {
        if (!CanExport || Report is not { } report) return;
        IsExporting = true; NotifyAll();
        try
        {
            if (html) await Task.Run(() => ValidationHtmlReport.ExportAsync(report, path, ct), ct);
            else await SnapshotReport.ExportAsync(report, path, ct);
            SetStatus(L.T("Validação de memória exportada para arquivo local."));
        }
        catch (OperationCanceledException) { SetStatus(L.T("Exportação cancelada.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetStatus(L.T("Não foi possível exportar. Escolha um arquivo novo: ") + e.Message); }
        finally { IsExporting = false; NotifyAll(); }
    }
    public void SetStatus(string status) { if (!disposed) { Status = status; Notify(nameof(Status)); } }
    public void Dispose() { if (disposed) return; disposed = true; active?.Cancel(); }
    private static string Label(MemoryValidationStatus state) => state switch
    { MemoryValidationStatus.Completed => L.T("Concluída"), MemoryValidationStatus.Cancelled => L.T("Cancelada"), MemoryValidationStatus.TimedOut => "Timeout", _ => L.T("Estado desconhecido") };
    private void Notify(string name) { if (!disposed) PropertyChanged?.Invoke(this, new(name)); }
    private void NotifyAll()
    {
        foreach (string name in new[] { nameof(IsRunning), nameof(IsExporting), nameof(CanStart), nameof(CanCancel), nameof(CanExport), nameof(Report), nameof(Status), nameof(Summary) }) Notify(name);
    }
}
