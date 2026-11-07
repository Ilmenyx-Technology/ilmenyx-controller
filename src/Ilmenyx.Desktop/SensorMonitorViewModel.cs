// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.ComponentModel;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class SensorMonitorViewModel(ISensorProvider provider) : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? pollingStop;
    private Task? pollingTask;
    private bool disposed;
    private readonly CancellationTokenSource exportStop = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public SensorSnapshot? Snapshot { get; private set; }
    public bool IsRefreshing { get; private set; }
    public bool IsPolling { get; private set; }
    public bool IsExporting { get; private set; }
    public bool CanRefresh => !disposed && !IsRefreshing;
    public bool CanPoll => !disposed && !IsPolling;
    public bool CanExport => !disposed && Snapshot is not null && !IsRefreshing && !IsExporting;
    public string Status { get; private set; } = "Aguardando leitura de sensores Linux.";
    public string Text => Snapshot is { } snapshot ? SensorPresentation.Text(snapshot) : "Sem snapshot de sensores.";

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!CanRefresh) return;
        IsRefreshing = true;
        NotifyAll();
        try
        {
            var snapshot = await Task.Run(() => provider.CollectAsync(ct), ct);
            if (disposed) return;
            Snapshot = snapshot;
            Status = $"{snapshot.Readings.Count} canais · {snapshot.Status} · {snapshot.CollectedAt.LocalDateTime:T}";
        }
        catch (OperationCanceledException) { SetStatus("Leitura cancelada; último snapshot preservado."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { SetStatus("Leitura indisponível; último snapshot preservado: " + e.Message); }
        finally { IsRefreshing = false; NotifyAll(); }
    }
    public void StartPolling(CancellationToken lifetime = default)
    {
        if (!CanPoll) return;
        IsPolling = true;
        pollingStop = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var stop = pollingStop;
        pollingTask = PollAsync(stop);
        NotifyAll();
    }
    private async Task PollAsync(CancellationTokenSource stop)
    {
        using (stop)
        using (var timer = new PeriodicTimer(TimeSpan.FromSeconds(2)))
        {
            try
            {
                do { await RefreshAsync(stop.Token); } while (await timer.WaitForNextTickAsync(stop.Token));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally { pollingStop = null; IsPolling = false; NotifyAll(); }
        }
    }
    public async Task StopPollingAsync()
    {
        pollingStop?.Cancel();
        if (pollingTask is { } task) await task;
    }
    public async Task ExportAsync(bool html, Func<string, Task<string?>> chooseDestination, CancellationToken ct = default)
    {
        if (!CanExport || Snapshot is not { } selected) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, exportStop.Token);
        var token = linked.Token;
        IsExporting = true;
        NotifyAll();
        try
        {
            token.ThrowIfCancellationRequested();
            var path = await chooseDestination(html ? ".html" : ".json");
            token.ThrowIfCancellationRequested();
            if (path is null) { SetStatus("Exportação cancelada."); return; }
            if (!ReferenceEquals(selected, Snapshot) || IsRefreshing)
            {
                SetStatus("O snapshot mudou. Pare o monitor e selecione novamente para exportar.");
                return;
            }
            if (html) await Task.Run(() => SensorHtmlReport.ExportAsync(selected, path, token), token);
            else await Task.Run(() => SnapshotReport.ExportAsync(selected, path, token), token);
            SetStatus("Snapshot de sensores exportado para arquivo local.");
        }
        catch (OperationCanceledException) { SetStatus("Exportação cancelada."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetStatus("Não foi possível exportar: " + e.Message); }
        finally { IsExporting = false; NotifyAll(); }
    }
    public void SetStatus(string status) { if (!disposed) { Status = status; Notify(nameof(Status)); } }
    public void Dispose() { if (disposed) return; disposed = true; pollingStop?.Cancel(); exportStop.Cancel(); exportStop.Dispose(); }
    private void Notify(string property) { if (!disposed) PropertyChanged?.Invoke(this, new(property)); }
    private void NotifyAll()
    {
        foreach (string property in new[] { nameof(Snapshot), nameof(Text), nameof(Status), nameof(IsRefreshing), nameof(IsPolling), nameof(CanRefresh), nameof(CanPoll), nameof(CanExport), nameof(IsExporting) }) Notify(property);
    }
}
