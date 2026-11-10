// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Timed methodology adapted from RESET EXPLODE GameSimBench.cs, MIT, GustavoReset (2026).
using System.Runtime.InteropServices;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core.Benchmarks;

internal readonly record struct GameSimStepTiming(double MainMs, double ParallelMs);
internal interface IGameSimTimedEngine { void Reset(); GameSimStepTiming Step(bool warmup); }
internal sealed class GameSimTimedEngine(int threads, TimeProvider clock) : IGameSimTimedEngine
{
    private readonly GameSimStateEngine engine = new(30_000);
    public void Reset() => engine.Reset();
    public GameSimStepTiming Step(bool warmup)
    {
        var timing = engine.StepMeasured(threads, clock);
        // Original warmup keeps _frames=0, advances positions/RNG, then resets only the frame counter.
        if (warmup) engine.ResetFrameCounter();
        return timing;
    }
}

public sealed class GameSimBenchmarkRunner
{
    public const int MaximumStoredFrameTimes = 200_000;
    private readonly TimeProvider clock;
    private readonly Func<int, IGameSimTimedEngine> createEngine;
    public GameSimBenchmarkRunner() : this(TimeProvider.System, null) { }
    internal GameSimBenchmarkRunner(TimeProvider clock, Func<int, IGameSimTimedEngine>? createEngine)
    { this.clock = clock; this.createEngine = createEngine ?? (threads => new GameSimTimedEngine(threads, clock)); }
    public static void Validate(GameSimBenchmarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.AllowCpuLoad) throw new ArgumentException("GameSim completo exige --allow-cpu-load; gera carga sustentada de CPU.");
        GameSimFullPlanner.Validate(new() { Threads = options.Threads, Duration = options.Duration, Timeout = options.Timeout });
    }
    public Task<GameSimBenchmarkResult> RunAsync(GameSimBenchmarkOptions options, CancellationToken ct = default)
    { Validate(options); return Task.Run(() => Run(options, ct)); }
    private GameSimBenchmarkResult Run(GameSimBenchmarkOptions options, CancellationToken external)
    {
        using var deadline = new CancellationTokenSource(options.Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(external, deadline.Token);
        var ct = linked.Token; var started = clock.GetUtcNow();
        int warmupSteps = 0; var frames = new List<double>(20_000);
        double main = 0, parallel = 0, seconds = 0, warmupSeconds = 0;
        try
        {
            ct.ThrowIfCancellationRequested();
            var engine = createEngine(options.Threads); engine.Reset(); ct.ThrowIfCancellationRequested();
            long warmup = clock.GetTimestamp();
            while (clock.GetElapsedTime(warmup).TotalSeconds < 2)
            { ct.ThrowIfCancellationRequested(); engine.Step(true); warmupSteps++; warmupSeconds = clock.GetElapsedTime(warmup).TotalSeconds; ct.ThrowIfCancellationRequested(); }
            ct.ThrowIfCancellationRequested();
            long start = clock.GetTimestamp();
            while (clock.GetElapsedTime(start) < options.Duration)
            {
                ct.ThrowIfCancellationRequested();
                if (frames.Count >= MaximumStoredFrameTimes) return Result(GameSimBenchmarkStatus.SampleLimitReached);
                long t0 = clock.GetTimestamp();
                var timing = engine.Step(false);
                double ms = clock.GetElapsedTime(t0).TotalMilliseconds;
                seconds = clock.GetElapsedTime(start).TotalSeconds;
                frames.Add(ms); main += timing.MainMs; parallel += timing.ParallelMs;
                ct.ThrowIfCancellationRequested();
                if (!double.IsFinite(ms) || ms <= 0 || !double.IsFinite(main) || !double.IsFinite(parallel) || timing.MainMs < 0 || timing.ParallelMs < 0)
                    return Result(GameSimBenchmarkStatus.InvalidTiming);
            }
            seconds = clock.GetElapsedTime(start).TotalSeconds;
            ct.ThrowIfCancellationRequested();
            if (frames.Count == 0 || !double.IsFinite(seconds) || seconds <= 0) return Result(GameSimBenchmarkStatus.InvalidTiming);
            frames.Sort();
            double avg = frames.Count / seconds;
            double low1 = 1000 / frames[Math.Min(frames.Count - 1, (int)(frames.Count * .99))];
            if (!double.IsFinite(avg) || !double.IsFinite(low1)) return Result(GameSimBenchmarkStatus.InvalidTiming);
            ct.ThrowIfCancellationRequested();
            return Result(GameSimBenchmarkStatus.Completed, avg, low1, main / frames.Count, parallel / frames.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { return Result(external.IsCancellationRequested ? GameSimBenchmarkStatus.Cancelled : GameSimBenchmarkStatus.TimedOut); }
        GameSimBenchmarkResult Result(GameSimBenchmarkStatus status, double? avg = null, double? low1 = null, double? mainMs = null, double? parallelMs = null) =>
            new("reset-gamesim-original-math-v1", "silex-gamesim-timed-v1", "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6",
                "reset-gamesim-30000-warmup2s", status, started, 30_000, options.Threads, options.Threads * 4,
                warmupSteps, frames.Count, 2, warmupSeconds, options.Duration.TotalSeconds, options.Timeout.TotalSeconds,
                MaximumStoredFrameTimes, seconds, avg, low1, mainMs, parallelMs, avg, "not-validated",
                RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
                ["FPS da simulação sintética de CPU, não FPS de jogo real nem desempenho da GPU.",
                 "Score experimental=FPS médio; low1 é recíproco de p99, não média do pior 1%.",
                 "Comparação Windows NÃO VALIDADA; JIT/runtime, workers limitados, scheduler e energia afetam medição.",
                 "Warmup 2 s avança mundo/RNG com câmera em frame zero; estado não é reinicializado após aquecimento.",
                 "Coleta, serialização e ordenação estatística fora do intervalo medido; overhead de controle do runner incluído.",
                 "Cancelamento/timeout cooperativos entre passos completos; interrompido/limite/tempo inválido suprime todas as métricas.",
                 "Sem proteção térmica ou controle de afinidade/energia. Execução completa real ainda NÃO TESTADA neste incremento."]);
    }
}
