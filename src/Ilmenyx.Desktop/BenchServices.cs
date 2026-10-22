// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Contracts;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Gpu.Vulkan;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>Everything the Bench page needs from the outside, injectable so the graphical smoke never starts real load.</summary>
public sealed record BenchServices(
    Func<CpuBenchmarkOptions, CancellationToken, IProgress<CpuBenchmarkProgress>?, Task<CpuRenderOutput>> Cpu,
    Func<long, TimeSpan, CancellationToken, IProgress<(string Phase, double Fraction)>?, Task<MemoryBenchResult>> Memory,
    Func<GameSimBenchmarkOptions, CancellationToken, Task<GameSimBenchmarkResult>> Sim,
    Func<DiskBenchOptions, CancellationToken, IProgress<(string Phase, double Fraction)>?, Task<DiskBenchResult>> Disk,
    Func<long> AvailableMemory,
    Func<string> MountsText,
    Func<GpuSceneOptions, CancellationToken, IProgress<GpuSceneProgress>?, Task<GpuSceneResult>> GpuScene,
    Func<GpuBenchInfo> GpuInfo,
    Func<double?> GpuTemp)
{
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static BenchServices Real() => new(
        (options, ct, progress) => new CpuBenchmarkRunner().RunAsync(options, ct, progress),
        (available, timeout, ct, progress) => MemoryBench.RunAsync(available, timeout, ct, progress),
        (options, ct) => new GameSimBenchmarkRunner().RunAsync(options, ct),
        (options, ct, progress) => LinuxDiskBench.RunAsync(options, ct, progress),
        () => LinuxMemoryInfo.AvailableBytes(),
        () => { try { return File.ReadAllText("/proc/mounts"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; } },
        (options, ct, progress) => new GpuScene().RunAsync(options, ct, progress),
        () => GpuBenchInfo.Detect(),
        () => null);
}

/// <summary>What the GPU benchmark needs to know about the machine before it starts.</summary>
public sealed record GpuBenchInfo(string? UnavailableReason, string AdapterName, bool SharesCpuThermals, bool SupportsRayTracing)
{
    public static GpuBenchInfo Detect(VulkanDeviceList? devices = null)
    {
        devices ??= VulkanDevices.List();
        var adapter = devices.Adapters.Where(a => !a.IsSoftware && a.SupportsVulkan13).OrderBy(a => a.Kind == "GPU dedicada" ? 0 : 1).FirstOrDefault();
        if (adapter is null) return new(devices.Error ?? (devices.Adapters.Count == 0 ? "Nenhum adaptador Vulkan encontrado." : "Nenhum adaptador Vulkan 1.3 com GPU real (só renderização por CPU ou Vulkan antigo)."), "", false, false);
        return new(null, adapter.Name, adapter.Kind == "GPU integrada", adapter.SupportsRayTracingPipeline);
    }
}
