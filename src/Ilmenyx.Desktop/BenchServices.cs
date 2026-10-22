// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Contracts;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>Everything the Bench page needs from the outside, injectable so the graphical smoke never starts real load.</summary>
public sealed record BenchServices(
    Func<CpuBenchmarkOptions, CancellationToken, IProgress<CpuBenchmarkProgress>?, Task<CpuRenderOutput>> Cpu,
    Func<long, TimeSpan, CancellationToken, IProgress<(string Phase, double Fraction)>?, Task<MemoryBenchResult>> Memory,
    Func<GameSimBenchmarkOptions, CancellationToken, Task<GameSimBenchmarkResult>> Sim,
    Func<DiskBenchOptions, CancellationToken, IProgress<(string Phase, double Fraction)>?, Task<DiskBenchResult>> Disk,
    Func<long> AvailableMemory,
    Func<string> MountsText)
{
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static BenchServices Real() => new(
        (options, ct, progress) => new CpuBenchmarkRunner().RunAsync(options, ct, progress),
        (available, timeout, ct, progress) => MemoryBench.RunAsync(available, timeout, ct, progress),
        (options, ct) => new GameSimBenchmarkRunner().RunAsync(options, ct),
        (options, ct, progress) => LinuxDiskBench.RunAsync(options, ct, progress),
        () => LinuxMemoryInfo.AvailableBytes(),
        () => { try { return File.ReadAllText("/proc/mounts"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; } });
}
