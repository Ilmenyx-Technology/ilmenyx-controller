// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Contracts;

/// <summary>Rates and percentages are null when unavailable (first sample, unreadable source); zero only when reported.</summary>
/// <param name="Uid">Owner (real uid); null when unknown.</param>
/// <param name="ParentPid">Parent process; null when unknown.</param>
/// <param name="StartTicks">Start time in clock ticks since boot: with the pid, identifies the process (pids are reused).</param>
/// <param name="AppUnit">systemd unit of the app that started it (from /proc/pid/cgroup), e.g. "app-firefox@…service"; null when none.</param>
/// <param name="ReadBps">Disk read rate (bytes/s, /proc/pid/io); only for the user's own processes, null otherwise.</param>
/// <param name="WriteBps">Disk write rate (bytes/s), same rules as <paramref name="ReadBps"/>.</param>
public sealed record ProcessUsage(int Pid, string Name, double? CpuPercent, long? ResidentBytes, int? Uid = null, int? ParentPid = null,
    long StartTicks = 0, string? AppUnit = null, double? ReadBps = null, double? WriteBps = null);
public sealed record UsageSample(DateTimeOffset At, string? CpuModel, double? CpuPercent, double? CpuMhz, double? CpuTempC,
    long? MemoryTotal, long? MemoryUsed, double? DiskReadBps, double? DiskWriteBps,
    double? NetRxBps, double? NetTxBps, string? NetInterface, int ProcessCount, IReadOnlyList<ProcessUsage> Processes,
    double? CpuMaxCorePercent = null, string? BusiestCore = null, double? CpuMaxMhz = null, double? CpuPowerW = null,
    long? SwapTotal = null, long? SwapUsed = null, IReadOnlyList<NetInterfaceUsage>? Interfaces = null);
/// <summary>One network interface: rates since the previous sample and its IP addresses (refreshed every few seconds).</summary>
public sealed record NetInterfaceUsage(string Name, double? RxBps, double? TxBps, IReadOnlyList<string> Addresses, bool Wireless);
public interface IUsageProvider
{
    Task<UsageSample> SampleAsync(CancellationToken cancellationToken = default);
    /// <summary>Extra per-process detail (disk counters) while a page that shows it is open; providers without it ignore the flag.</summary>
    bool DetailedProcesses { get => false; set { } }
}
