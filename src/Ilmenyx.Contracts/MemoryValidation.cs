// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Contracts;

public enum MemoryValidationStatus { Completed, Cancelled, TimedOut }
public sealed record MemoryValidationOptions
{
    public int Threads { get; init; } = 1;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}
public sealed record MemoryValidationProgress(string Stage, int CompletedTraversalSteps);
public sealed record MemoryValidationResult(string WorkloadId, string AlgorithmVersion, string ReferenceCommit,
    MemoryValidationStatus Status, DateTimeOffset StartedAt, int Threads, double TimeoutSeconds, int ReadCopyRepetitions, long WorkingSetBytes,
    long MainBufferBytes, int WarmupSteps, int RequestedTraversalSteps, int CompletedTraversalSteps,
    int? FinalIndex, long? ReadSum, long? CopySum, string? RingSha256, string? CopySha256,
    double? Score, string PerformanceStatus, string Runtime, string Architecture, IReadOnlyList<string> Limitations);
public sealed record MemoryValidationReport(string SchemaVersion, HardwareSnapshot Hardware, MemoryValidationResult Validation);
public interface IMemoryValidationRunner
{
    Task<MemoryValidationResult> RunAsync(MemoryValidationOptions options, CancellationToken ct = default,
        IProgress<MemoryValidationProgress>? progress = null);
}
