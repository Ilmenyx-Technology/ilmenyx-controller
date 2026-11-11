// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.ComponentModel;
using System.Globalization;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

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
    public string CpuLine { get; private set; } = L.T("Aguardando amostras…");
    public string CpuModel { get; private set; } = "";
    public string MemoryLine { get; private set; } = L.T("Aguardando amostras…");
    public string MemorySub { get; private set; } = "";
    public string DiskLine { get; private set; } = L.T("Aguardando amostras…");
    public string NetLine { get; private set; } = L.T("Aguardando amostras…");
    public string NetSub { get; private set; } = "";
    /// <summary>Swap in use ("Swap 5,9 GiB de 15,3 GiB"), or that there is none.</summary>
    public string SwapLine { get; private set; } = "";
    /// <summary>Each active interface with its kind and first IP address.</summary>
    public string NetAddresses { get; private set; } = "";
    /// <summary>Rates per interface, only when more than one is active (otherwise the total says it all).</summary>
    public string NetPerInterface { get; private set; } = "";
    public string Status { get; private set; } = L.T("Monitor pausado.");
    public string ProcessSummary { get; private set; } = "";

    public void Start(CancellationToken lifetime = default)
    {
        if (disposed || IsRunning) return;
        run = CancellationTokenSource.CreateLinkedTokenSource(lifetime, stop.Token);
        IsRunning = true; Status = L.T("Monitor ativo (1 s). Amostras só na memória; nada é gravado."); Notify(nameof(IsRunning)); Notify(nameof(Status));
        var source = run;
        loop = LoopAsync(source);
    }

    public async Task StopAsync()
    {
        run?.Cancel();
        if (loop is { } task) await task;
    }

    public void ToggleSort() { SortByMemory = !SortByMemory; if (Last is { } l) Apply(l); }

    /// <summary>Asks the sampler for per-process disk counters while a page that shows them is open.</summary>
    public bool DetailedProcesses { get => provider.DetailedProcesses; set => provider.DetailedProcesses = value; }

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
            finally { run = null; IsRunning = false; if (!disposed) { Status = L.T("Monitor pausado."); Notify(nameof(IsRunning)); Notify(nameof(Status)); } }
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
        { if (!disposed) { Status = L.T("Leitura indisponível nesta amostra: ") + e.Message; Notify(nameof(Status)); } }
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
        CpuLine = $"{Pct(s.CpuPercent)} / {(s.CpuMhz is { } mhz ? mhz.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "— MHz")} / {(s.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : L.T("temperatura indisponível"))}";
        CpuModel = s.CpuModel ?? L.T("Modelo não informado");
        MemoryLine = s.MemoryTotal is { } total && s.MemoryUsed is { } used
            ? L.T($"{Pct(100.0 * used / total)} · {Bytes(used)} de {Bytes(total)}") : L.T("Memória indisponível");
        MemorySub = s.MemoryTotal is { } t2 ? "Total " + Bytes(t2) : "";
        DiskLine = s.DiskReadBps is { } dr && s.DiskWriteBps is { } dw ? L.T($"{Rate(dr)} leitura / {Rate(dw)} escrita") : L.T("Taxa de disco: aguardando 2ª amostra ou indisponível");
        NetLine = s.NetRxBps is { } nr && s.NetTxBps is { } nt ? L.T($"{Rate(nr)} recebido / {Rate(nt)} enviado") : L.T("Taxa de rede: aguardando 2ª amostra ou indisponível");
        NetSub = s.NetInterface ?? L.T("Nenhuma interface de rede ativa");
        SwapLine = s.SwapTotal is { } st && s.SwapUsed is { } su ? L.T($"Swap {Bytes(su)} de {Bytes(st)}") : L.T("Sem swap");
        var ifaces = s.Interfaces ?? [];
        NetAddresses = ifaces.Count == 0 ? NetSub : string.Join(" · ", ifaces.Select(i => i.Name + (i.Wireless ? " (Wi-Fi)" : "") + " " + (i.Addresses.Count > 0 ? i.Addresses[0] : L.T("sem IP"))));
        NetPerInterface = ifaces.Count < 2 ? "" : string.Join(" · ", ifaces.Select(i => i.Name + " ↓" + (i.RxBps is { } r ? Rate(r) : "—") + " ↑" + (i.TxBps is { } t ? Rate(t) : "—")));
        ProcessSummary = L.T($"{s.ProcessCount} processos visíveis · mostrando {Math.Min(s.Processes.Count, 40)}");
        var ordered = SortByMemory ? s.Processes.OrderByDescending(p => p.ResidentBytes ?? -1) : s.Processes.OrderByDescending(p => p.CpuPercent ?? -1).ThenByDescending(p => p.ResidentBytes ?? -1);
        Rows = ordered.Take(40).Select(p => new ProcessRow(p.Name, Pct(p.CpuPercent), p.ResidentBytes is { } b ? Bytes(b) : "—")).ToArray();
        foreach (var name in new[] { nameof(Last), nameof(CpuLine), nameof(CpuModel), nameof(MemoryLine), nameof(MemorySub), nameof(DiskLine), nameof(NetLine), nameof(NetSub), nameof(SwapLine), nameof(NetAddresses), nameof(NetPerInterface), nameof(ProcessSummary), nameof(Rows) }) Notify(name);
        Updated?.Invoke();
    }

    public static string Pct(double? v) => v is { } p ? p.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";
    public static string Bytes(long b) => b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GiB" : (b / (double)(1L << 20)).ToString("0.00", CultureInfo.InvariantCulture) + " MiB";
    public static string Rate(double b) => b >= 1 << 20 ? (b / (1 << 20)).ToString("0.00", CultureInfo.InvariantCulture) + " MiB/s" : b >= 1024 ? (b / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " KiB/s" : b.ToString("0", CultureInfo.InvariantCulture) + " B/s";

    private void Notify(string property) { if (!disposed) PropertyChanged?.Invoke(this, new(property)); }
    public void Dispose() { if (disposed) return; disposed = true; stop.Cancel(); run?.Cancel(); stop.Dispose(); }
}
