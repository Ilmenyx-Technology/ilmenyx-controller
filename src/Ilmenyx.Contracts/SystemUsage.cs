// New Ilmenyx additions: see LICENSING.md.
namespace Ilmenyx.Contracts;

/// <summary>Rates and percentages are null when unavailable (first sample, unreadable source); zero only when reported.</summary>
public sealed record ProcessUsage(int Pid, string Name, double? CpuPercent, long? ResidentBytes);
public sealed record UsageSample(DateTimeOffset At, string? CpuModel, double? CpuPercent, double? CpuMhz, double? CpuTempC,
    long? MemoryTotal, long? MemoryUsed, double? DiskReadBps, double? DiskWriteBps,
    double? NetRxBps, double? NetTxBps, string? NetInterface, int ProcessCount, IReadOnlyList<ProcessUsage> Processes,
    double? CpuMaxCorePercent = null, string? BusiestCore = null, double? CpuMaxMhz = null);
public interface IUsageProvider
{
    Task<UsageSample> SampleAsync(CancellationToken cancellationToken = default);
}
