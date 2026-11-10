// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.ComponentModel;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>Offline SMART import: explicit file + disk name, no smartctl, device or sysfs access.</summary>
public sealed class SmartImportViewModel(Func<string, string, CancellationToken, Task<SmartImportReport>> importer) : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private bool disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public SmartImportReport? Report { get; private set; }
    public bool IsImporting { get; private set; }
    public bool IsExporting { get; private set; }
    public bool CanImport => !disposed && !IsImporting && !IsExporting;
    public bool CanExport => !disposed && Report is not null && !IsImporting && !IsExporting;
    public string Status { get; private set; } = L.T("Nenhum relatório SMART importado. Nada é consultado no disco.");
    public string Text => Report is { } r
        ? L.T($"{r.Diagnostic.Device} · {r.Diagnostic.Status}\nRelato passed: {SnapshotReport.Describe(r.Diagnostic.ReportedPassed)}\n") + (SmartMetricText.Lines(r.Diagnostic) is { Count: > 0 } m ? L.T("Métricas brutas (sem veredito):\n") + string.Join("\n", m) + "\n" : "") + string.Join("\n", r.Limitations)
        : L.T("Sem relatório importado.");

    public async Task ImportAsync(string device, string path, CancellationToken ct = default)
    {
        if (!CanImport) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token);
        IsImporting = true; NotifyAll();
        try
        {
            var report = await Task.Run(() => importer(device.Trim(), path.Trim(), linked.Token), linked.Token);
            if (disposed) return;
            Report = report;
            Status = L.T("Relatório histórico importado e filtrado; data original da coleta não confirmada.");
        }
        catch (OperationCanceledException) { SetStatus(L.T("Importação cancelada; relatório anterior preservado.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException or NotSupportedException)
        { SetStatus(L.T("Importação recusada; relatório anterior preservado: ") + e.Message); }
        finally { IsImporting = false; NotifyAll(); }
    }

    public async Task ExportAsync(bool html, Func<string, Task<string?>> chooseDestination, CancellationToken ct = default)
    {
        if (!CanExport || Report is not { } selected) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token);
        var token = linked.Token;
        IsExporting = true; NotifyAll();
        try
        {
            token.ThrowIfCancellationRequested();
            var path = await chooseDestination(html ? ".html" : ".json");
            token.ThrowIfCancellationRequested();
            if (path is null) { SetStatus(L.T("Exportação cancelada.")); return; }
            if (!ReferenceEquals(selected, Report)) { SetStatus(L.T("O relatório mudou. Selecione novamente para exportar.")); return; }
            if (html) await Task.Run(() => SmartImportHtmlReport.ExportAsync(selected, path, token), token);
            else await Task.Run(() => SnapshotReport.ExportAsync(selected, path, token), token);
            SetStatus(L.T("Relatório SMART importado exportado para arquivo local."));
        }
        catch (OperationCanceledException) { SetStatus(L.T("Exportação cancelada.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetStatus(L.T("Não foi possível exportar: ") + e.Message); }
        finally { IsExporting = false; NotifyAll(); }
    }

    public void SetStatus(string status) { if (!disposed) { Status = status; Notify(nameof(Status)); } }
    public void Dispose() { if (disposed) return; disposed = true; stop.Cancel(); stop.Dispose(); }
    private void Notify(string property) { if (!disposed) PropertyChanged?.Invoke(this, new(property)); }
    private void NotifyAll()
    {
        foreach (string property in new[] { nameof(Report), nameof(Text), nameof(Status), nameof(IsImporting), nameof(IsExporting), nameof(CanImport), nameof(CanExport) }) Notify(property);
    }
}
