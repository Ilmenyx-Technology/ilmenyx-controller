// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Adapted from RESET EXPLODE MemoryBench.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
using System.Diagnostics;

namespace Ilmenyx.Core.Benchmarks;

public enum BenchRunStatus { Completed, Cancelled, TimedOut, Unavailable }

/// <summary>Latency and bandwidth of main memory. Values are null unless the run completed.</summary>
public sealed record MemoryBenchResult(BenchRunStatus Status, double? LatencyNs, double? ReadGBs, double? CopyGBs,
    long AllocatedBytes, int Threads, string? Reason);

/// <summary>
/// Full RAM benchmark: pointer-chasing latency over 256 MiB and read/copy bandwidth over 1 GiB buffers (best of 3).
/// Needs about 2.3 GiB; refuses to run unless that plus a 1 GiB reserve is available. Never part of tests or startup.
/// </summary>
public static class MemoryBench
{
    public const long LatencyBytes = 256L << 20, BandwidthBytes = 1L << 30, ReserveBytes = 1L << 30;
    public const long RequiredBytes = LatencyBytes + 2 * BandwidthBytes + ReserveBytes;

    /// <summary>Null when the plan fits; otherwise the reason it must not run.</summary>
    public static string? Check(long availableBytes) =>
        availableBytes < RequiredBytes
            ? $"Memória disponível insuficiente: são necessários {RequiredBytes >> 20} MiB livres (2,3 GiB do teste + 1 GiB de reserva), há {availableBytes >> 20} MiB."
            : null;

    public static Task<MemoryBenchResult> RunAsync(long availableBytes, TimeSpan timeout, CancellationToken ct,
        IProgress<(string Phase, double Fraction)>? progress = null, long latencyBytes = LatencyBytes, long bandwidthBytes = BandwidthBytes,
        int latencySteps = 20_000_000)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        string? why = latencyBytes == LatencyBytes && bandwidthBytes == BandwidthBytes ? Check(availableBytes) : null;
        if (why is not null) return Task.FromResult(new MemoryBenchResult(BenchRunStatus.Unavailable, null, null, null, 0, 0, why));
        return Task.Run(() => Run(timeout, ct, progress, latencyBytes, bandwidthBytes, latencySteps));
    }

    private static MemoryBenchResult Run(TimeSpan timeout, CancellationToken external, IProgress<(string, double)>? progress,
        long latencyBytes, long bandwidthBytes, int latencySteps)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(external, deadline.Token);
        int threads = Environment.ProcessorCount;
        long allocated = latencyBytes;
        MemoryBenchResult Stopped() => new(external.IsCancellationRequested ? BenchRunStatus.Cancelled : BenchRunStatus.TimedOut,
            null, null, null, allocated, threads, "Interrompido: nenhum valor é publicado.");

        progress?.Report(("Latência da RAM", 0));
        double? ns = MeasureLatencyNs(stop.Token, latencyBytes, latencySteps);
        if (ns is null) return Stopped();
        progress?.Report(("Banda da RAM", 0.4));
        allocated = latencyBytes + 2 * bandwidthBytes;
        var bw = MeasureBandwidth(stop.Token, bandwidthBytes, threads, p => progress?.Report(("Banda da RAM", 0.4 + 0.6 * p)));
        if (bw is null) return Stopped();
        progress?.Report(("Concluído", 1));
        return new(BenchRunStatus.Completed, ns, bw.Value.Read, bw.Value.Copy, allocated, threads, null);
    }

    /// <summary>Single-cycle random permutation (Sattolo) walked with dependent loads; null if cancelled.</summary>
    public static double? MeasureLatencyNs(CancellationToken ct, long bytes = LatencyBytes, int steps = 20_000_000)
    {
        int n = checked((int)(bytes / sizeof(long)));
        var next = new long[n];
        int lines = n / 8;
        var order = new int[lines];
        for (int i = 0; i < lines; i++) order[i] = i;
        var rng = new Random(42);
        for (int i = lines - 1; i > 0; i--) { int j = rng.Next(i); (order[i], order[j]) = (order[j], order[i]); }
        for (int i = 0; i < lines; i++) next[order[i] * 8L] = order[(i + 1) % lines] * 8L;
        long p = 0;
        for (int i = 0; i < 200_000; i++) p = next[p];
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++)
        {
            p = next[p];
            if ((i & 0xFFFFF) == 0 && ct.IsCancellationRequested) return null;
        }
        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / steps;
        GC.KeepAlive(p);
        return ct.IsCancellationRequested ? null : ns;
    }

    /// <summary>Read (sum) and copy bandwidth in GB/s, best of three; copy counts both directions. Null if cancelled.</summary>
    public static (double Read, double Copy)? MeasureBandwidth(CancellationToken ct, long bytes = BandwidthBytes, int threads = 0, Action<double>? step = null)
    {
        threads = threads <= 0 ? Environment.ProcessorCount : threads;
        int n = checked((int)(bytes / sizeof(long)));
        var src = new long[n];
        var dst = new long[n];
        int per = n / threads;
        Parallel.For(0, threads, t => Array.Fill(src, t + 1L, t * per, per));
        double bestRead = 0, bestCopy = 0;
        for (int rep = 0; rep < 3; rep++)
        {
            if (ct.IsCancellationRequested) return null;
            var sw = Stopwatch.StartNew();
            long total = 0;
            Parallel.For(0, threads, () => 0L, (t, _, acc) =>
            {
                var span = new ReadOnlySpan<long>(src, t * per, per);
                long s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                for (int i = 0; i + 3 < span.Length; i += 4) { s0 += span[i]; s1 += span[i + 1]; s2 += span[i + 2]; s3 += span[i + 3]; }
                return acc + s0 + s1 + s2 + s3;
            }, acc => Interlocked.Add(ref total, acc));
            bestRead = Math.Max(bestRead, bytes / sw.Elapsed.TotalSeconds / 1e9);
            sw.Restart();
            Parallel.For(0, threads, t => Array.Copy(src, t * per, dst, t * per, per));
            bestCopy = Math.Max(bestCopy, 2.0 * bytes / sw.Elapsed.TotalSeconds / 1e9);
            step?.Invoke((rep + 1) / 3.0);
        }
        return ct.IsCancellationRequested ? null : (bestRead, bestCopy);
    }
}
