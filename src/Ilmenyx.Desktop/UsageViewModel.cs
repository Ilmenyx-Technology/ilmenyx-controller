// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.ComponentModel;
using System.Globalization;
using Ilmenyx.Contracts;

namespace Ilmenyx.Desktop;

public sealed record ProcessRow(string Name, string Cpu, string Memory);

/// <summary>Live monitor model. Samples stay in a bounded in-memory window (15 min at 1 s) for the charts: no persistence, no export.</summary>
public sealed class UsageViewModel(IUsageProvider provider, TimeSpan? interval = null) : INotifyPropertyChanged, IDisposable
{
    public const int MaxHistory = 900;
    private readonly TimeSpan period = interval ?? TimeSpan.FromSeconds(1);
    private readonly CancellationTokenSource stop = new();
    private readonly List<double> cpu = [], memory = [], disk = [], net = [], temp = [], power = [];
    private CancellationTokenSource? run;
    private Task? loop;
    private bool disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Updated;
    public UsageSample? Last { get; private set; }
    public bool IsRunning { get; private set; }
    public bool SortByMemory { get; private set; }
    public IReadOnlyList<double> CpuSeries => cpu; public IReadOnlyList<double> MemorySeries => memory;
    public IReadOnlyList<double> TempSeries => temp; public IReadOnlyList<double> PowerSeries => power; public IReadOnlyList<double> DiskSeries => disk; public IReadOnlyList<double> NetSeries => net;
    public IReadOnlyList<ProcessRow> Rows { get; private set; } = [];
    public string CpuLine { get; private set; } = "Aguardando amostras…";
    public string CpuModel { get; private set; } = "";
    public string MemoryLine { get; private set; } = "Aguardando amostras…";
    public string MemorySub { get; private set; } = "";
    public string DiskLine { get; private set; } = "Aguardando amostras…";
    public string NetLine { get; private set; } = "Aguardando amostras…";
    public string NetSub { get; private set; } = "";
    public string Status { get; private set; } = "Monitor pausado.";
    public string ProcessSummary { get; private set; } = "";

    public void Start(CancellationToken lifetime = default)
    {
        if (disposed || IsRunning) return;
        run = CancellationTokenSource.CreateLinkedTokenSource(lifetime, stop.Token);
        IsRunning = true; Status = "Monitor ativo (1 s). Amostras só na memória; nada é gravado."; Notify(nameof(IsRunning)); Notify(nameof(Status));
        var source = run;
        loop = LoopAsync(source);
    }

    public async Task StopAsync()
    {
        run?.Cancel();
        if (loop is { } task) await task;
    }

    public void ToggleSort() { SortByMemory = !SortByMemory; if (Last is { } l) Apply(l); }

    private async Task LoopAsync(CancellationTokenSource source)
    {
        using (source)
        using (var timer = new PeriodicTimer(period))
        {
            try
            {
                do { await SampleOnceAsync(source.Token); } while (await timer.WaitForNextTickAsync(source.Token));
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested) { }
            finally { run = null; IsRunning = false; if (!disposed) { Status = "Monitor pausado."; Notify(nameof(IsRunning)); Notify(nameof(Status)); } }
        }
    }

    public async Task SampleOnceAsync(CancellationToken ct)
    {
        try
        {
            var sample = await Task.Run(() => provider.SampleAsync(ct), ct);
            if (!disposed) Apply(sample);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { if (!disposed) { Status = "Leitura indisponível nesta amostra: " + e.Message; Notify(nameof(Status)); } }
    }

    private static void Push(List<double> list, double? value)
    {
        list.Add(value ?? 0);
        if (list.Count > MaxHistory) list.RemoveAt(0);
    }

    private void Apply(UsageSample s)
    {
        Last = s;
        Push(cpu, s.CpuPercent); Push(temp, s.CpuTempC); Push(power, s.CpuPowerW);
        Push(memory, s.MemoryTotal is > 0 && s.MemoryUsed is { } u ? 100.0 * u / s.MemoryTotal.Value : null);
        Push(disk, s.DiskReadBps is { } r && s.DiskWriteBps is { } w ? r + w : null);
        Push(net, s.NetRxBps is { } rx && s.NetTxBps is { } tx ? rx + tx : null);
        CpuLine = $"{Pct(s.CpuPercent)} / {(s.CpuMhz is { } mhz ? mhz.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "— MHz")} / {(s.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "temperatura indisponível")}";
        CpuModel = s.CpuModel ?? "Modelo não informado";
        MemoryLine = s.MemoryTotal is { } total && s.MemoryUsed is { } used
            ? $"{Pct(100.0 * used / total)} · {Bytes(used)} de {Bytes(total)}" : "Memória indisponível";
        MemorySub = s.MemoryTotal is { } t2 ? "Total " + Bytes(t2) : "";
        DiskLine = s.DiskReadBps is { } dr && s.DiskWriteBps is { } dw ? $"{Rate(dr)} leitura / {Rate(dw)} escrita" : "Taxa de disco: aguardando 2ª amostra ou indisponível";
        NetLine = s.NetRxBps is { } nr && s.NetTxBps is { } nt ? $"{Rate(nr)} recebido / {Rate(nt)} enviado" : "Taxa de rede: aguardando 2ª amostra ou indisponível";
        NetSub = s.NetInterface ?? "Nenhuma interface de rede ativa";
        ProcessSummary = $"{s.ProcessCount} processos visíveis · mostrando {Math.Min(s.Processes.Count, 40)}";
        var ordered = SortByMemory ? s.Processes.OrderByDescending(p => p.ResidentBytes ?? -1) : s.Processes.OrderByDescending(p => p.CpuPercent ?? -1).ThenByDescending(p => p.ResidentBytes ?? -1);
        Rows = ordered.Take(40).Select(p => new ProcessRow(p.Name, Pct(p.CpuPercent), p.ResidentBytes is { } b ? Bytes(b) : "—")).ToArray();
        foreach (var name in new[] { nameof(Last), nameof(CpuLine), nameof(CpuModel), nameof(MemoryLine), nameof(MemorySub), nameof(DiskLine), nameof(NetLine), nameof(NetSub), nameof(ProcessSummary), nameof(Rows) }) Notify(name);
        Updated?.Invoke();
    }

    public static string Pct(double? v) => v is { } p ? p.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";
    public static string Bytes(long b) => b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GiB" : (b / (double)(1L << 20)).ToString("0.00", CultureInfo.InvariantCulture) + " MiB";
    public static string Rate(double b) => b >= 1 << 20 ? (b / (1 << 20)).ToString("0.00", CultureInfo.InvariantCulture) + " MiB/s" : b >= 1024 ? (b / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " KiB/s" : b.ToString("0", CultureInfo.InvariantCulture) + " B/s";

    private void Notify(string property) { if (!disposed) PropertyChanged?.Invoke(this, new(property)); }
    public void Dispose() { if (disposed) return; disposed = true; stop.Cancel(); run?.Cancel(); stop.Dispose(); }
}
