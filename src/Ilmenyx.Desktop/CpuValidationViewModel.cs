// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.ComponentModel;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;

namespace Ilmenyx.Desktop;

/// <summary>UI-thread model. The desktop exposes only the bounded validation profile.</summary>
public sealed class CpuValidationViewModel(IHardwareInfoProvider hardware, ICpuBenchmarkRunner runner) : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? active;
    private bool disposed;
    private int generation;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsRunning { get; private set; }
    public bool IsExporting { get; private set; }
    public bool CanStart => !disposed && !IsRunning && !IsExporting;
    public bool CanCancel => IsRunning && active is { IsCancellationRequested: false };
    public bool CanExport => !disposed && !IsRunning && !IsExporting && Report is not null;
    public string Status { get; private set; } = "Pronto. Validação reduzida, sem score.";
    public long CompletedSamples { get; private set; }
    public const int MaximumSamples = 4608;
    public CpuBenchmarkReport? Report { get; private set; }
    public CpuRenderOutput? Frame { get; private set; }
    public string Summary => Report is { } report
        ? $"{StateLabel(report.Benchmark.Status)} · {report.Benchmark.CompletedSamples:N0} amostras · sem score\nSHA256: {report.Benchmark.ImageSha256}"
        : "Nenhuma validação concluída. Não há pontuação de desempenho neste modo.";

    public async Task StartAsync(CancellationToken lifetime = default)
    {
        if (!CanStart) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        active = cancellation;
        int run = ++generation;
        IsRunning = true;
        Report = null;
        Frame = null;
        CompletedSamples = 0;
        Status = "Coletando contexto de hardware…";
        NotifyAll();
        try
        {
            var snapshot = await Task.Run(() => hardware.CollectAsync(cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            SetStatus("Renderizando 32×18 pixels, 8 amostras por pixel…");
            // Progress<T> captures the UI synchronization context. Late/old callbacks are ignored.
            var observer = new Progress<CpuBenchmarkProgress>(value =>
            {
                if (disposed || generation != run || !IsRunning || cancellation.IsCancellationRequested) return;
                CompletedSamples = Math.Max(CompletedSamples, Math.Clamp(value.CompletedSamples, 0, MaximumSamples));
                Notify(nameof(CompletedSamples));
            });
            var frame = await runner.RunAsync(new CpuBenchmarkOptions(), cancellation.Token, observer);
            if (disposed) return;
            Frame = frame;
            Report = new CpuBenchmarkReport("1.0", snapshot, frame.Result);
            CompletedSamples = frame.Result.CompletedSamples;
            Status = StateLabel(frame.Result.Status) + ". Validação reduzida, sem score.";
        }
        catch (OperationCanceledException)
        {
            SetStatus(cancellation.IsCancellationRequested ? "Validação cancelada." : "Coleta de hardware atingiu o timeout.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or AggregateException or ArgumentException)
        {
            SetStatus("Validação indisponível: " + e.Message);
        }
        finally
        {
            active = null;
            IsRunning = false;
            NotifyAll();
        }
    }

    public void Cancel()
    {
        if (!CanCancel) return;
        // Status first: cancelling may run the worker's catch at once, and its final status must not be overwritten.
        var cts = active!;
        SetStatus("Cancelando e aguardando encerramento dos workers…");
        cts.Cancel();
        Notify(nameof(CanCancel));
    }

    public Task ExportAsync(string path, CancellationToken ct = default) => ExportReportAsync(path, false, ct);
    public Task ExportHtmlAsync(string path, CancellationToken ct = default) => ExportReportAsync(path, true, ct);
    private async Task ExportReportAsync(string path, bool html, CancellationToken ct)
    {
        if (!CanExport || Report is not { } report) return;
        IsExporting = true;
        NotifyAll();
        try
        {
            if (html) await Task.Run(() => ValidationHtmlReport.ExportAsync(report, path, ct), ct);
            else await SnapshotReport.ExportAsync(report, path, ct);
            SetStatus("Relatório CPU exportado para arquivo local.");
        }
        catch (OperationCanceledException) { SetStatus("Exportação cancelada."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { SetStatus("Não foi possível exportar. Escolha um arquivo novo: " + e.Message); }
        finally { IsExporting = false; NotifyAll(); }
    }

    public void SetStatus(string status)
    {
        if (disposed) return;
        Status = status;
        Notify(nameof(Status));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        generation++;
        active?.Cancel();
        // The active operation disposes its CTS only after all awaited workers return.
    }

    private static string StateLabel(CpuRunStatus status) => status switch
    {
        CpuRunStatus.Completed => "Concluída",
        CpuRunStatus.Cancelled => "Cancelada (imagem parcial)",
        CpuRunStatus.TimedOut => "Timeout (imagem parcial)",
        _ => "Estado desconhecido"
    };
    private void Notify(string property) { if (!disposed) PropertyChanged?.Invoke(this, new(property)); }
    private void NotifyAll()
    {
        foreach (string property in new[] { nameof(IsRunning), nameof(IsExporting), nameof(CanStart), nameof(CanCancel), nameof(CanExport),
            nameof(Status), nameof(CompletedSamples), nameof(Frame), nameof(Report), nameof(Summary) }) Notify(property);
    }
}
