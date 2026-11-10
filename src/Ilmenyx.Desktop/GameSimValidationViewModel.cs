// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.ComponentModel;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class GameSimValidationViewModel(IHardwareInfoProvider hardware, IGameSimValidationRunner runner) : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? active;
    private bool disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsRunning { get; private set; }
    public bool IsExporting { get; private set; }
    public bool CanStart => !disposed && !IsRunning && !IsExporting;
    public bool CanCancel => !disposed && IsRunning && active is { IsCancellationRequested: false };
    public bool CanExport => !disposed && !IsRunning && !IsExporting && Report is not null;
    public GameSimValidationReport? Report { get; private set; }
    public string Status { get; private set; } = L.T("Pronto. Simulação reduzida; sem FPS ou score.");
    public GameSimState? State => Report?.Validation.State;
    public bool HasState => State is not null;
    public string Summary => Report is { } report
        ? L.T($"{Label(report.Validation.Status)} · {report.Validation.CompletedSteps}/{report.Validation.Workload.Steps} passos · sem FPS ou score\nSHA256 estado: {report.Validation.StateSha256 ?? L.T("indisponível")}")
        : L.T("Nenhuma simulação executada. Sem FPS, low1 ou score.");
    public async Task StartAsync(CancellationToken lifetime = default)
    {
        if (!CanStart) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime); active = stop;
        IsRunning = true; Report = null; Status = L.T("Coletando contexto de hardware…"); NotifyAll();
        try
        {
            var snapshot = await Task.Run(() => hardware.CollectAsync(stop.Token), stop.Token);
            stop.Token.ThrowIfCancellationRequested(); SetStatus(L.T("Simulando 256 entidades por 16 passos fixos…"));
            var result = await runner.RunAsync(new(), stop.Token);
            if (disposed) return;
            Report = new("1.0", snapshot, result);
            SetStatus(Label(result.Status) + (result.State is null ? L.T(". Estado final indisponível; sem FPS ou score.") : L.T(". Estado final calculado; sem FPS ou score.")));
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
            SetStatus(L.T("Simulação reduzida exportada para arquivo local."));
        }
        catch (OperationCanceledException) { SetStatus(L.T("Exportação cancelada.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetStatus(L.T("Não foi possível exportar. Escolha um arquivo novo: ") + e.Message); }
        finally { IsExporting = false; NotifyAll(); }
    }
    public void SetStatus(string status) { if (!disposed) { Status = status; Notify(nameof(Status)); } }
    public void Dispose() { if (disposed) return; disposed = true; active?.Cancel(); }
    private static string Label(GameSimValidationStatus state) => state switch
    { GameSimValidationStatus.Completed => L.T("Concluída"), GameSimValidationStatus.Cancelled => L.T("Cancelada"), GameSimValidationStatus.TimedOut => "Timeout", _ => L.T("Estado desconhecido") };
    private void Notify(string name) { if (!disposed) PropertyChanged?.Invoke(this, new(name)); }
    private void NotifyAll()
    {
        foreach (string name in new[] { nameof(IsRunning), nameof(IsExporting), nameof(CanStart), nameof(CanCancel), nameof(CanExport), nameof(Report), nameof(Status), nameof(Summary), nameof(State), nameof(HasState) }) Notify(name);
    }
}
