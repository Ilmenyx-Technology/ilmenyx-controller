// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;

namespace Ilmenyx.Gpu.Vulkan;

/// <summary>Continuous GPU burn for the stress page: runs <see cref="GpuBurn"/> in 60 s chunks until stopped, on the first non-software Vulkan 1.3 adapter.</summary>
public sealed class VulkanGpuStress : IGpuStressEngine
{
    private readonly VulkanAdapter? adapter;
    private CancellationTokenSource? cts;
    private Task? loop;
    private double fps;
    private volatile string? error;
    public string? UnavailableReason { get; }
    public string AdapterName => adapter?.Name ?? "";
    public bool SharesCpuThermals => adapter?.Kind == "GPU integrada";
    public bool Running => loop is { IsCompleted: false };
    public double Fps => fps;
    public string? Error => error;

    public VulkanGpuStress(VulkanDeviceList? devices = null)
    {
        devices ??= VulkanDevices.List();
        adapter = devices.Adapters.Where(a => !a.IsSoftware && a.SupportsVulkan13).OrderBy(a => a.Kind == "GPU dedicada" ? 0 : 1).FirstOrDefault();
        UnavailableReason = adapter is not null ? null : devices.Error ?? (devices.Adapters.Count == 0 ? "Nenhum adaptador Vulkan encontrado." : "Nenhum adaptador Vulkan 1.3 com GPU real (só renderização por CPU ou Vulkan antigo).");
    }

    public void Start()
    {
        if (adapter is null || Running) return;
        cts = new CancellationTokenSource(); var token = cts.Token; error = null; fps = 0;
        loop = Task.Run(async () =>
        {
            var progress = new Progress<GpuBurnProgress>(p => fps = p.Fps);
            while (!token.IsCancellationRequested)
            {
                var r = await new GpuBurn().RunAsync(new GpuBurnOptions(adapter.Index, Shells: 64, Iterations: 32, Duration: TimeSpan.FromSeconds(60), AllowGpuLoad: true), token, progress);
                if (r.Status is GpuRunStatus.Failed or GpuRunStatus.Unavailable) { error = r.Reason; return; }
                if (r.Status == GpuRunStatus.Cancelled) return;
            }
        });
    }

    public void Stop()
    {
        cts?.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(8)); } catch (AggregateException) { }
        cts?.Dispose(); cts = null; fps = 0;
    }
}
