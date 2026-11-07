// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
namespace Ilmenyx.Contracts;

public enum CpuWorkloadProfile { Validation, Full }
public enum CpuRunStatus { Completed, Cancelled, TimedOut }

public sealed record CpuBenchmarkOptions
{
    public CpuWorkloadProfile Profile { get; init; } = CpuWorkloadProfile.Validation;
    public int Threads { get; init; } = 1;
    public TimeSpan MinimumDuration { get; init; } = TimeSpan.Zero;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public bool AllowCpuLoad { get; init; }
}

public sealed record CpuWorkload(string Id, int Width, int Height, int SamplesPerPixel, int TileSize, int MaximumDepth);
public sealed record CpuBenchmarkProgress(long CompletedSamples, int CompletedTiles, int TotalTiles, int CompletedPasses);
public sealed record CpuBenchmarkEnvironment(string OperatingSystem, string Runtime, string Architecture,
    int RuntimeLogicalProcessors, string Configuration, int WarmupPasses);
public sealed record CpuBenchmarkResult(string AlgorithmVersion, string MeasurementVersion, string ReferenceCommit,
    CpuWorkload Workload, CpuRunStatus Status, DateTimeOffset StartedAt, double Seconds,
    int Threads, double RequestedMinimumSeconds, double TimeoutSeconds, int CompletedPasses, long CompletedSamples, double? ExperimentalScore,
    string ScoreComparability, string ImageSha256, CpuBenchmarkEnvironment Environment, IReadOnlyList<string> Limitations);
public sealed record CpuBenchmarkReport(string SchemaVersion, HardwareSnapshot Hardware, CpuBenchmarkResult Benchmark);
