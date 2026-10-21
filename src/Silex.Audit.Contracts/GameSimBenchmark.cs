namespace Silex.Audit.Contracts;

public sealed record GameSimBenchmarkOptions
{
    public int Threads { get; init; } = 1;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public bool AllowCpuLoad { get; init; }
}
public enum GameSimBenchmarkStatus { Completed, Cancelled, TimedOut, SampleLimitReached, InvalidTiming }
public sealed record GameSimBenchmarkResult(string AlgorithmVersion, string MeasurementVersion, string ReferenceCommit,
    string WorkloadId, GameSimBenchmarkStatus Status, DateTimeOffset StartedAt, int Entities, int Threads, int Chunks,
    int WarmupSteps, int MeasuredFrames, double WarmupSeconds, double ActualWarmupSeconds, double RequestedDurationSeconds, double TimeoutSeconds,
    int MaximumStoredFrameTimes, double MeasuredSeconds, double? AverageFps, double? Low1Fps, double? MainThreadMs,
    double? ParallelMs, double? ExperimentalScore, string ScoreComparability, string Runtime, string Architecture,
    IReadOnlyList<string> Limitations);
public sealed record GameSimBenchmarkReport(string SchemaVersion, HardwareSnapshot Hardware, GameSimBenchmarkResult Benchmark);
