// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Gpu.Vulkan;

namespace Ilmenyx.Desktop;

/// <summary>
/// Real calibration for the FPS estimate: the Vulkan game scene on the GPU the games use (same PCI ID as the estimate's GPU, else the
/// dedicated one) for <see cref="FpsModels.SceneSeconds"/> s, then GameSim on one CPU thread for <see cref="FpsModels.SimSeconds"/> s.
/// The page aborts it at <see cref="FpsModels.AbortTempC"/> through the token.
/// </summary>
public static class FpsCalibratorReal
{
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static FpsCalibrator Create(Func<FpsContext?> context, Func<double?> cpuTemp, Func<double?> gpuTemp) => new(async (progress, ct) =>
    {
        var ctx = context();
        progress.Report(("Procurando a GPU dos jogos…", 0.02));
        var devices = await Task.Run(() => VulkanDevices.List(), ct);
        var adapter = PickAdapter(devices.Adapters, ctx);
        double? score = null, fps = null; string? name = null, reason = null;
        if (adapter is null) reason = devices.Error ?? "nenhuma GPU com Vulkan 1.3 (só renderização por CPU ou Vulkan antigo)";
        else
        {
            progress.Report(($"Cena de GPU em {adapter.Name} ({FpsModels.SceneSeconds} s)…", 0.05));
            var scene = await new GpuScene().RunAsync(new GpuSceneOptions(adapter.Index, FpsModels.SceneWidth, FpsModels.SceneHeight, TimeSpan.FromSeconds(FpsModels.SceneSeconds),
                FpsModels.SceneShadow, FpsModels.SceneTrees, true), ct, new Progress<GpuSceneProgress>(p => progress.Report(($"Cena de GPU · {p.Fps:0} FPS", 0.05 + 0.55 * p.Fraction))));
            ct.ThrowIfCancellationRequested();
            if (scene.Status == GpuRunStatus.Completed && scene.Score is > 0) { score = scene.Score; fps = scene.AverageFps; name = scene.Adapter; }
            else reason = scene.Reason ?? "a cena de GPU não terminou";
        }
        progress.Report(($"Simulação de jogo na CPU, uma thread ({FpsModels.SimSeconds} s + 2 s de aquecimento)…", 0.62));
        using var tick = new CancellationTokenSource();
        var started = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            while (!tick.IsCancellationRequested)
            {
                try { await Task.Delay(500, tick.Token); } catch (OperationCanceledException) { break; }
                double f = Math.Min(1, (DateTime.UtcNow - started).TotalSeconds / (FpsModels.SimSeconds + 2));
                progress.Report(($"Simulação de jogo na CPU, uma thread ({FpsModels.SimSeconds} s + 2 s de aquecimento)…", 0.62 + 0.36 * f));
            }
        });
        GameSimBenchmarkResult sim;
        try
        {
            sim = await new GameSimBenchmarkRunner().RunAsync(new GameSimBenchmarkOptions { Threads = 1, Duration = TimeSpan.FromSeconds(FpsModels.SimSeconds), Timeout = TimeSpan.FromSeconds(FpsModels.SimTimeoutSeconds), AllowCpuLoad = true }, ct);
        }
        finally { tick.Cancel(); }
        ct.ThrowIfCancellationRequested();
        double? cpu = sim.Status == GameSimBenchmarkStatus.Completed && sim.AverageFps is > 0 ? sim.AverageFps : null;
        if (cpu is null) reason = (reason is null ? "" : reason + "; ") + $"simulação de CPU não concluída ({sim.Status})";
        progress.Report(("Concluído", 1));
        return new FpsCalibrationMeasurement(score, fps, name, cpu, reason);
    }, cpuTemp, gpuTemp);

    /// <summary>The adapter with the estimate GPU's PCI vendor/device, else the dedicated one, else any real Vulkan 1.3 GPU.</summary>
    public static VulkanAdapter? PickAdapter(IReadOnlyList<VulkanAdapter> adapters, FpsContext? ctx)
    {
        var real = adapters.Where(a => !a.IsSoftware && a.SupportsVulkan13).ToList();
        uint vendor = 0, device = 0;
        if (ctx is not null) { uint.TryParse(ctx.GpuVendorId, System.Globalization.NumberStyles.HexNumber, null, out vendor); uint.TryParse(ctx.GpuDeviceId, System.Globalization.NumberStyles.HexNumber, null, out device); }
        return real.FirstOrDefault(a => vendor != 0 && a.VendorId == vendor && a.DeviceId == device)
            ?? real.FirstOrDefault(a => a.Kind == "GPU dedicada") ?? real.FirstOrDefault();
    }
}
