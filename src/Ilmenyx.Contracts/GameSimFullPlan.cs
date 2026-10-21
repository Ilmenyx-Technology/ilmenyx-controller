namespace Ilmenyx.Contracts;

public sealed record GameSimFullPlanOptions
{
    public int Threads { get; init; } = 1;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>Reviewable configuration only. This is never a measured benchmark result.</summary>
public sealed record GameSimFullPlan(string SchemaVersion, string Kind, string ExecutionStatus,
    bool ExecutionAvailable, string ReferenceCommit, int Entities, float World, int GridSize,
    float DeltaTime, uint Seed, int Threads, int Chunks, double WarmupSeconds, double DurationSeconds,
    double TimeoutSeconds, int MaximumStoredFrameTimes, double? Score, string ScoreFormula,
    string Low1Definition, string ScoreComparability, string Runtime, string Architecture,
    IReadOnlyList<string> RequiredValidation, IReadOnlyList<string> Limitations);
