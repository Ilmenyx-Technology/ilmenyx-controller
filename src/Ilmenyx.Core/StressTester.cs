// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions, adapted from the CPU/RAM workers of the original StressTester (MIT, see NOTICE): see LICENSING.md.
// Real load: only ever started by an explicit user action behind a confirmation. Never started by tests, startup or CI.
namespace Ilmenyx.Core;

public interface IStressEngine
{
    bool Running { get; }
    long MemoryErrors { get; }
    long MemoryPasses { get; }
    long MemoryAllocatedBytes { get; }
    void Start(bool cpu, bool ram, double ramFraction, long availableMemoryBytes);
    void Stop();
}

public static class StressMemory
{
    public const long ReserveBytes = 1L << 30, MaxBytes = 64L << 30, ChunkBytes = 128L << 20, MinimumBytes = 256L << 20;

    /// <summary>Bytes to allocate: fraction (0.1–0.9) of the available memory, never leaving less than 1 GiB free, capped at 64 GiB, rounded down to whole chunks. 0 = refuse.</summary>
    public static long Plan(long availableBytes, double fraction, long chunkBytes = ChunkBytes, long minimum = MinimumBytes, long reserve = ReserveBytes)
    {
        if (availableBytes <= reserve) return 0;
        long wanted = (long)(availableBytes * Math.Clamp(fraction, 0.1, 0.9));
        long bytes = Math.Min(Math.Min(wanted, availableBytes - reserve), MaxBytes);
        bytes = bytes / chunkBytes * chunkBytes;
        return bytes >= minimum ? bytes : 0;
    }
}

public sealed class StressTester : IStressEngine
{
    private CancellationTokenSource? cts;
    private readonly List<Thread> threads = [];
    private long memErrors, memPasses;
    private readonly int chunkLongs;

    /// <param name="chunkLongs">Longs per memory block (128 MiB by default); smaller only in unit tests of the verifier.</param>
    public StressTester(int chunkLongs = 16 * 1024 * 1024) { this.chunkLongs = chunkLongs; }

    public bool Running => cts != null;
    public long MemoryErrors => Interlocked.Read(ref memErrors);
    public long MemoryPasses => Interlocked.Read(ref memPasses);
    public long MemoryAllocatedBytes { get; private set; }

    public void Start(bool cpu, bool ram, double ramFraction, long availableMemoryBytes)
    {
        if (Running) return;
        long plan = ram ? StressMemory.Plan(availableMemoryBytes, ramFraction, chunkLongs * 8L, Math.Min(StressMemory.MinimumBytes, chunkLongs * 8L)) : 0;
        if (ram && plan == 0) throw new InvalidOperationException("Pouca memória disponível para o teste de RAM (é preciso sobrar pelo menos 1 GiB livre).");
        cts = new CancellationTokenSource(); var token = cts.Token;
        Interlocked.Exchange(ref memErrors, 0); Interlocked.Exchange(ref memPasses, 0); MemoryAllocatedBytes = 0;
        int cpuThreads = Environment.ProcessorCount;
        if (cpu && ram && cpuThreads > 1) cpuThreads--; // one thread is reserved for the memory worker
        if (cpu) for (int i = 0; i < cpuThreads; i++) { int seed = i; StartThread(() => CpuWorker(token, seed), $"Stress CPU {seed}"); }
        if (ram) { MemoryAllocatedBytes = plan; StartThread(() => RamWorker(token, plan), "Stress RAM"); }
    }

    public void Stop()
    {
        if (cts == null) return;
        cts.Cancel();
        foreach (var t in threads) t.Join(2000);
        threads.Clear(); cts.Dispose(); cts = null;
        GC.Collect();
    }

    private void StartThread(ThreadStart body, string name)
    {
        // BelowNormal keeps the UI responsive without lowering the load (cores still run at 100 %).
        var t = new Thread(body) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        threads.Add(t); t.Start();
    }

    private static void CpuWorker(CancellationToken token, int seed)
    {
        // Mixes floating point (FMA/sqrt/sin) and integer work over a buffer that fits L1/L2.
        var buf = new double[4096];
        for (int i = 0; i < buf.Length; i++) buf[i] = i + seed + 1;
        ulong x = (ulong)(seed + 1) * 0x9E3779B97F4A7C15UL;
        while (!token.IsCancellationRequested)
        {
            for (int rep = 0; rep < 64; rep++)
                for (int i = 0; i < buf.Length; i++)
                {
                    double v = buf[i];
                    v = Math.FusedMultiplyAdd(v, 1.0000001, 0.5);
                    v = Math.Sqrt(v * v + 1.0) + Math.Sin(v) * 1e-3;
                    buf[i] = v > 1e12 ? i + 1 : v;
                    x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                }
            if (x == 0) buf[0] = 0; // keeps the JIT from eliminating the work
        }
    }

    /// <summary>Fills the block with the pass pattern.</summary>
    public static void FillPattern(long[] chunk, ulong pass)
    {
        long pattern = unchecked((long)(pass * 0x9E3779B97F4A7C15UL));
        for (int i = 0; i < chunk.Length; i++) chunk[i] = pattern ^ i;
    }

    /// <summary>Number of cells that no longer hold the pass pattern.</summary>
    public static long CountMismatches(long[] chunk, ulong pass)
    {
        long pattern = unchecked((long)(pass * 0x9E3779B97F4A7C15UL)), errors = 0;
        for (int i = 0; i < chunk.Length; i++) if (chunk[i] != (pattern ^ i)) errors++;
        return errors;
    }

    private void RamWorker(CancellationToken token, long totalBytes)
    {
        var chunks = new List<long[]>();
        try
        {
            long allocated = 0;
            try { while (allocated + chunkLongs * 8L <= totalBytes && !token.IsCancellationRequested) { chunks.Add(new long[chunkLongs]); allocated += chunkLongs * 8L; } }
            catch (OutOfMemoryException) { /* continue with what was allocated */ }
            MemoryAllocatedBytes = allocated;
            if (chunks.Count == 0) return;
            ulong pass = 0;
            while (!token.IsCancellationRequested)
            {
                pass++;
                foreach (var c in chunks) { FillPattern(c, pass); if (token.IsCancellationRequested) return; }
                foreach (var c in chunks)
                {
                    long errs = CountMismatches(c, pass);
                    if (errs > 0) Interlocked.Add(ref memErrors, errs);
                    if (token.IsCancellationRequested) return;
                }
                Interlocked.Increment(ref memPasses);
            }
        }
        finally { chunks.Clear(); }
    }
}

public sealed record StressLimits(double CpuTempLimit, double? GpuTempLimit, TimeSpan Duration);

/// <summary>Abort rules of the original stress tick, in the original order: CPU temperature, GPU temperature, memory errors, GPU window closed, time elapsed.</summary>
public static class StressGuard
{
    public static string? Check(StressLimits limits, bool cpuLoadRunning, double? cpuTemp, bool gpuLoadRunning, double? gpuTemp, long memoryErrors, bool gpuStopped, string? gpuError, TimeSpan elapsed)
    {
        if (cpuLoadRunning && cpuTemp is { } t && t >= limits.CpuTempLimit) return $"ABORTADO: CPU a {t:0.#} °C atingiu o limite de {limits.CpuTempLimit:0} °C";
        if (gpuLoadRunning && limits.GpuTempLimit is { } gl && gpuTemp is { } gt && gt >= gl) return $"ABORTADO: GPU a {gt:0.#} °C atingiu o limite de {gl:0} °C";
        if (memoryErrors > 0) return $"ABORTADO: {memoryErrors} erros de memória detectados";
        if (gpuLoadRunning && gpuStopped) return gpuError != null ? $"ABORTADO: falha na GPU ({gpuError})" : "Janela da GPU fechada pelo usuário";
        if (limits.Duration > TimeSpan.Zero && elapsed >= limits.Duration) return "Concluído (tempo esgotado)";
        return null;
    }

    public static string? Validate(string cpuLimit, string gpuLimit, bool gpu, string minutes, out StressLimits? limits)
    {
        limits = null;
        var ci = System.Globalization.CultureInfo.CurrentCulture;
        if (!double.TryParse(cpuLimit, System.Globalization.NumberStyles.Float, ci, out double cpu) || cpu < 40 || cpu > 110) return "Limite da CPU deve estar entre 40 e 110 °C.";
        double? g = null;
        if (gpu) { if (!double.TryParse(gpuLimit, System.Globalization.NumberStyles.Float, ci, out double gv) || gv < 40 || gv > 100) return "Limite da GPU deve estar entre 40 e 100 °C."; g = gv; }
        if (!double.TryParse(minutes, System.Globalization.NumberStyles.Float, ci, out double m) || m < 0 || m > 24 * 60) return "Duração inválida (0 a 1440 minutos; 0 = sem limite).";
        limits = new(cpu, g, TimeSpan.FromMinutes(m));
        return null;
    }
}
