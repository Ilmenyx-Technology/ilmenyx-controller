// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core.Benchmarks;

public sealed class GameSimValidationRunner(TimeProvider? timeProvider = null) : IGameSimValidationRunner
{
    public const string AlgorithmVersion = "reset-gamesim-bounded-v1", HashFormat = "silex-gamesim-state-le-v1";
    public const int MaximumSteps = 16;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public static void Validate(GameSimValidationOptions options)
    {
        if (options.Threads is not (1 or 2 or 4)) throw new ArgumentException("GameSim reduzido permite somente 1, 2 ou 4 workers.");
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(10)) throw new ArgumentException("Timeout GameSim deve ser positivo e no máximo 10 segundos.");
    }
    public Task<GameSimValidationResult> RunAsync(GameSimValidationOptions options, CancellationToken ct = default, IProgress<GameSimValidationProgress>? progress = null)
    { Validate(options); return Task.Run(() => Run(options, ct, progress)); }
    private GameSimValidationResult Run(GameSimValidationOptions options, CancellationToken external, IProgress<GameSimValidationProgress>? progress)
    {
        using var deadline = new CancellationTokenSource(options.Timeout, clock);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(external, deadline.Token);
        var ct = stop.Token; int completed = 0; var started = clock.GetUtcNow();
        try
        {
            progress?.Report(new(0)); ct.ThrowIfCancellationRequested();
            var engine = new GameSimStateEngine(); engine.Reset();
            for (int step = 0; step < MaximumSteps; step++)
            {
                ct.ThrowIfCancellationRequested();
                // Cancellation is observed only at boundaries: never expose partially updated game logic.
                engine.Step(options.Threads); completed++;
                progress?.Report(new(completed)); ct.ThrowIfCancellationRequested();
            }
            var state = engine.Capture(); ValidateState(state);
            string hash = HashState(state, ct); ct.ThrowIfCancellationRequested();
            return Result(GameSimValidationStatus.Completed, state, hash);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { return Result(external.IsCancellationRequested ? GameSimValidationStatus.Cancelled : GameSimValidationStatus.TimedOut); }
        GameSimValidationResult Result(GameSimValidationStatus status, GameSimState? state = null, string? hash = null) =>
            new(AlgorithmVersion, "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6", HashFormat,
                new("silex-gamesim-validation-v1", GameSimStateEngine.Entities, MaximumSteps, GameSimStateEngine.World, 64, 1 / 60f, 12345, 0),
                status, started, options.Threads, options.Threads * 4, options.Timeout.TotalSeconds, completed, hash, state, null, "not-measured",
                RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
                ["Simulação sintética reduzida: 256 entidades e 16 passos, sem warmup por relógio, FPS, low1 ou score.",
                 "Não representa FPS de um jogo real ou desempenho da GPU; não é comparável ao benchmark Windows completo.",
                 "Matemática, RNG seed 12345, mundo 1000, grade 64x64 e dt=1/60 preservados; número de entidades e duração alterados explicitamente.",
                 "Cancelamento/timeout são cooperativos entre passos completos; resultados interrompidos não publicam estado/hash final.",
                 "Snapshot final copiado após física, lógica e ordenação; buffers internos mutáveis não são expostos."]);
    }
    internal static void ValidateState(GameSimState state)
    {
        if (state.Entities.Count != 256 || state.DrawOrder.Count != state.DrawDepthKeys.Count) throw new InvalidDataException("Snapshot GameSim incompleto.");
        foreach (var e in state.Entities)
            if (!float.IsFinite(e.X) || !float.IsFinite(e.Y) || !float.IsFinite(e.Vx) || !float.IsFinite(e.Vy) || !float.IsFinite(e.Health) || !float.IsFinite(e.Damage) || e.X < 0 || e.X > 1000 || e.Y < 0 || e.Y > 1000 || e.Health <= 0 || e.Health > 100 || e.Team > 1)
                throw new InvalidDataException("Estado GameSim fora do domínio.");
        if (state.DrawOrder.Any(i => i < 0 || i >= 256) || state.DrawOrder.Distinct().Count() != state.DrawOrder.Count || state.DrawDepthKeys.Any(k => !float.IsFinite(k)))
            throw new InvalidDataException("Ordenação GameSim inválida.");
    }
    internal static string HashState(GameSimState state, CancellationToken ct = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header, state.Entities.Count); BinaryPrimitives.WriteInt32LittleEndian(header[4..], state.CompletedSteps);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], state.RngState); BinaryPrimitives.WriteInt32LittleEndian(header[12..], state.DrawOrder.Count); hash.AppendData(header);
        Span<byte> item = stackalloc byte[29];
        foreach (var e in state.Entities)
        {
            ct.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteInt32LittleEndian(item, e.Id); BinaryPrimitives.WriteSingleLittleEndian(item[4..], e.X); BinaryPrimitives.WriteSingleLittleEndian(item[8..], e.Y);
            BinaryPrimitives.WriteSingleLittleEndian(item[12..], e.Vx); BinaryPrimitives.WriteSingleLittleEndian(item[16..], e.Vy); BinaryPrimitives.WriteSingleLittleEndian(item[20..], e.Health);
            BinaryPrimitives.WriteSingleLittleEndian(item[24..], e.Damage); item[28] = e.Team; hash.AppendData(item);
        }
        Span<byte> draw = stackalloc byte[8];
        for (int i = 0; i < state.DrawOrder.Count; i++)
        { ct.ThrowIfCancellationRequested(); BinaryPrimitives.WriteInt32LittleEndian(draw, state.DrawOrder[i]); BinaryPrimitives.WriteSingleLittleEndian(draw[4..], state.DrawDepthKeys[i]); hash.AppendData(draw); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
