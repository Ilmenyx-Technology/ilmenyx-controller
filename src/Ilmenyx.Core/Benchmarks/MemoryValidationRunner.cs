// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// RAM algorithm reference: RESET EXPLODE MemoryBench.cs, MIT, copyright (c) 2026 Gustavo (GustavoReset).
// Bounded validation adaptation; see LICENSE and docs/memoria-metodologia.md.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core.Benchmarks;

/// <summary>Fixed small correctness workload. No elapsed-time, latency, bandwidth or performance score.</summary>
public sealed class MemoryValidationRunner(TimeProvider? timeProvider = null) : IMemoryValidationRunner
{
    public const int Elements = 32768, WorkingSetBytes = Elements * sizeof(long), WarmupSteps = 1024, TraversalSteps = 32768;
    public const string WorkloadId = "silex-memory-validation-v1", AlgorithmVersion = "sattolo64-seed42-sum4-copy-v1";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public static void Validate(MemoryValidationOptions options)
    {
        if (options.Threads is not (1 or 2 or 4)) throw new ArgumentException("Validação de memória permite somente 1, 2 ou 4 workers.");
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(10)) throw new ArgumentException("Timeout de memória deve ser positivo e no máximo 10 segundos.");
    }
    public Task<MemoryValidationResult> RunAsync(MemoryValidationOptions options, CancellationToken ct = default, IProgress<MemoryValidationProgress>? progress = null)
    {
        Validate(options);
        // Always enter Run so pre-cancellation is represented by a result, never a fake measurement.
        return Task.Run(() => Run(options, ct, progress));
    }
    private MemoryValidationResult Run(MemoryValidationOptions options, CancellationToken external, IProgress<MemoryValidationProgress>? progress)
    {
        using var deadline = new CancellationTokenSource(options.Timeout, clock);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(external, deadline.Token);
        var ct = stop.Token; var started = clock.GetUtcNow(); int completed = 0;
        try
        {
            Notify("ring"); ct.ThrowIfCancellationRequested();
            var next = BuildRing(ct);
            long p = 0;
            Notify("warmup"); ct.ThrowIfCancellationRequested();
            for (int i = 0; i < WarmupSteps; i++) p = next[p];
            Notify("traversal"); ct.ThrowIfCancellationRequested();
            for (int i = 0; i < TraversalSteps; i++)
            {
                p = next[p]; completed = i + 1;
                if ((completed & 1023) == 0) { Notify("traversal"); ct.ThrowIfCancellationRequested(); }
            }
            Notify("read-copy"); ct.ThrowIfCancellationRequested();
            var src = new long[Elements]; var dst = new long[Elements];
            int per = Elements / options.Threads;
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.Threads, CancellationToken = ct };
            Parallel.For(0, options.Threads, parallel, t => Array.Fill(src, (long)t + 1, t * per, per));
            long readSum = 0;
            Parallel.For(0, options.Threads, parallel, t =>
            {
                var span = new ReadOnlySpan<long>(src, t * per, per);
                long s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                for (int i = 0; i + 3 < span.Length; i += 4) { s0 += span[i]; s1 += span[i + 1]; s2 += span[i + 2]; s3 += span[i + 3]; }
                Interlocked.Add(ref readSum, s0 + s1 + s2 + s3);
            });
            Parallel.For(0, options.Threads, parallel, t => Array.Copy(src, t * per, dst, t * per, per));
            ct.ThrowIfCancellationRequested();
            long copySum = 0;
            for (int i = 0; i < dst.Length; i++) { if ((i & 1023) == 0) ct.ThrowIfCancellationRequested(); copySum += dst[i]; }
            string ringHash = Hash(next, ct), copyHash = Hash(dst, ct);
            ct.ThrowIfCancellationRequested();
            return Result(MemoryValidationStatus.Completed, checked((int)p), readSum, copySum, ringHash, copyHash);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { return Result(external.IsCancellationRequested ? MemoryValidationStatus.Cancelled : MemoryValidationStatus.TimedOut); }

        void Notify(string stage) => progress?.Report(new(stage, completed));
        MemoryValidationResult Result(MemoryValidationStatus status, int? finalIndex = null, long? readSum = null, long? copySum = null, string? ring = null, string? copy = null) =>
            new(WorkloadId, AlgorithmVersion, "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6", status, started,
                options.Threads, options.Timeout.TotalSeconds, 1, WorkingSetBytes, 3L * WorkingSetBytes + Elements / 8 * sizeof(int), WarmupSteps,
                TraversalSteps, completed, finalIndex, readSum, copySum, ring, copy, null, "not-measured",
                RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
                ["Validação pequena de algoritmos; não mede latência, GB/s, estabilidade ou saúde da RAM.",
                 "Buffers de 256 KiB podem caber em caches; resultados não são comparáveis ao benchmark completo Windows.",
                 "Semente 42, passos de 64 bytes; warmup reduzido para 1024, percurso 32768 e uma repetição de leitura/cópia.",
                 "Workers restritos a 1/2/4 para particionamento integral e sem sobra do laço desenrolado; soma/cópia variam com a inicialização por worker.",
                 "Buffers principais: 784 KiB, mais buffers de hash e overhead do runtime; não representa uso total do processo.",
                 "Não há perfil completo de RAM, GameSim, teste de disco ou rede neste comando."]);
    }
    internal static long[] BuildRing(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var next = new long[Elements]; int lines = Elements / 8; var order = new int[lines];
        for (int i = 0; i < lines; i++) order[i] = i;
        var rng = new Random(42);
        for (int i = lines - 1; i > 0; i--)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            int j = rng.Next(i); (order[i], order[j]) = (order[j], order[i]);
        }
        for (int i = 0; i < lines; i++) next[order[i] * 8] = order[(i + 1) % lines] * 8;
        ct.ThrowIfCancellationRequested(); return next;
    }
    private static string Hash(long[] values, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[8192];
        for (int start = 0; start < values.Length; start += buffer.Length / sizeof(long))
        {
            ct.ThrowIfCancellationRequested(); int count = Math.Min(buffer.Length / sizeof(long), values.Length - start);
            for (int i = 0; i < count; i++) BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(i * 8), values[start + i]);
            hash.AppendData(buffer.AsSpan(0, count * 8));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
