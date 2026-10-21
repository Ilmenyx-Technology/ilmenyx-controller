namespace Ilmenyx.Contracts;

public enum GameSimValidationStatus { Completed, Cancelled, TimedOut }
public sealed record GameSimValidationOptions
{
    public int Threads { get; init; } = 1;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}
public sealed record GameSimEntity(int Id, float X, float Y, float Vx, float Vy, float Health, float Damage, byte Team);
public sealed record GameSimState(int CompletedSteps, uint RngState, IReadOnlyList<GameSimEntity> Entities,
    IReadOnlyList<int> DrawOrder, IReadOnlyList<float> DrawDepthKeys);
public sealed record GameSimWorkload(string Id, int Entities, int Steps, float World, int GridSize, float DeltaTime, uint Seed, int WarmupSteps);
public sealed record GameSimValidationProgress(int CompletedSteps);
public sealed record GameSimValidationResult(string AlgorithmVersion, string ReferenceCommit, string HashFormat,
    GameSimWorkload Workload, GameSimValidationStatus Status, DateTimeOffset StartedAt, int Threads, int Chunks,
    double TimeoutSeconds, int CompletedSteps, string? StateSha256, GameSimState? State, double? Score,
    string PerformanceStatus, string Runtime, string Architecture, IReadOnlyList<string> Limitations);
public sealed record GameSimValidationReport(string SchemaVersion, HardwareSnapshot Hardware, GameSimValidationResult Validation);
public interface IGameSimValidationRunner
{
    Task<GameSimValidationResult> RunAsync(GameSimValidationOptions options, CancellationToken ct = default,
        IProgress<GameSimValidationProgress>? progress = null);
}
