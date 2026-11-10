// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// No test renders or creates a Vulkan device: GPU load only runs by explicit click/CLI flag.
using System.Reflection;
using Ilmenyx.Gpu.Vulkan;
using Xunit;

namespace Ilmenyx.Tests;

public class GpuVulkanTests
{
    [Fact]
    public void BurnRefusesWithoutConsent()
    {
        Assert.NotNull(GpuBurnOptions.Validate(new GpuBurnOptions()));
        Assert.Null(GpuBurnOptions.Validate(new GpuBurnOptions(AllowGpuLoad: true)));
    }

    [Theory]
    [InlineData(-1, 1920, 1080, 4, 32, 24, 10)]
    [InlineData(0, 100, 1080, 4, 32, 24, 10)]
    [InlineData(0, 1920, 9000, 4, 32, 24, 10)]
    [InlineData(0, 1920, 1080, 3, 32, 24, 10)]
    [InlineData(0, 1920, 1080, 4, 0, 24, 10)]
    [InlineData(0, 1920, 1080, 4, 999, 24, 10)]
    [InlineData(0, 1920, 1080, 4, 32, 9999, 10)]
    [InlineData(0, 1920, 1080, 4, 32, 24, 0)]
    [InlineData(0, 1920, 1080, 4, 32, 24, 9999)]
    public void BurnRejectsOutOfRangeSettings(int adapter, int w, int h, int msaa, int shells, int iterations, int seconds) =>
        Assert.NotNull(GpuBurnOptions.Validate(new GpuBurnOptions(adapter, w, h, msaa, shells, iterations, TimeSpan.FromSeconds(seconds), true)));

    [Fact]
    public async Task InvalidOptionsNeverTouchTheGpu()
    {
        var r = await new GpuBurn().RunAsync(new GpuBurnOptions(AllowGpuLoad: false), CancellationToken.None);
        Assert.Equal(GpuRunStatus.Unavailable, r.Status);
        Assert.Null(r.AverageFps);
        Assert.Equal(0, r.Frames);
    }

    [Theory]
    [InlineData("burn.vert.spv")]
    [InlineData("burn.frag.spv")]
    public void ShaderResourcesAreEmbeddedSpirV(string name)
    {
        using var stream = typeof(VulkanDevices).Assembly.GetManifestResourceStream("Shaders." + name);
        Assert.NotNull(stream);
        var header = new byte[8]; stream!.ReadExactly(header);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(header, 0));
        Assert.Equal(0, stream.Length % 4);
    }

    [Fact]
    public void ListingAdaptersNeverThrowsAndExplainsAMissingLoader()
    {
        var list = VulkanDevices.List();
        Assert.True(list.Adapters.Count > 0 || list.Error is not null);
        Assert.All(list.Adapters, a => Assert.False(string.IsNullOrEmpty(a.Name)));
    }

    [Fact]
    public void StressEngineIsUnavailableWithoutARealVulkan13Gpu()
    {
        Assert.Contains("Nenhum adaptador", new VulkanGpuStress(new VulkanDeviceList([], null)).UnavailableReason);
        Assert.Contains("libvulkan", new VulkanGpuStress(new VulkanDeviceList([], "libvulkan.so.1 ausente")).UnavailableReason);
        var software = new VulkanAdapter(0, "llvmpipe", "CPU (software)", 0x10005, 0, "1.3.0", 0, true, true, false);
        Assert.Contains("só renderização por CPU", new VulkanGpuStress(new VulkanDeviceList([software], null)).UnavailableReason);
        var old = new VulkanAdapter(0, "Old GPU", "GPU dedicada", 1, 1, "1.1.0", 1 << 30, false, false, false);
        Assert.NotNull(new VulkanGpuStress(new VulkanDeviceList([old], null)).UnavailableReason);
    }

    [Fact]
    public void StressEngineChoosesADedicatedGpuOverAnIntegratedOne()
    {
        var igpu = new VulkanAdapter(0, "iGPU", "GPU integrada", 0x8086, 1, "1.3.0", 1 << 30, false, true, false);
        var dgpu = new VulkanAdapter(1, "dGPU", "GPU dedicada", 0x10de, 2, "1.3.0", 8L << 30, false, true, true);
        var engine = new VulkanGpuStress(new VulkanDeviceList([igpu, dgpu], null));
        Assert.Null(engine.UnavailableReason);
        Assert.Equal("dGPU", engine.AdapterName);
        Assert.False(engine.SharesCpuThermals);
        Assert.True(new VulkanGpuStress(new VulkanDeviceList([igpu], null)).SharesCpuThermals);
        Assert.False(engine.Running);
    }

    [Fact]
    public void VersionTextDecodesPackedVersions() =>
        Assert.Equal("1.3.280", VulkanDevices.VersionText((1u << 22) | (3u << 12) | 280));

    /// <summary>Opt-in only (ILMENYX_RUN_GPU_TESTS=1): starts the real burn for ~2 s and stops it. Without the variable it does nothing, so a pass here proves nothing about a GPU.</summary>
    [Fact]
    public async Task RealStartStopOnlyWithOptInEnvironmentVariable()
    {
        if (Environment.GetEnvironmentVariable("ILMENYX_RUN_GPU_TESTS") != "1") return;
        var engine = new VulkanGpuStress();
        if (engine.UnavailableReason is not null) return;
        engine.Start();
        await Task.Delay(2500);
        Assert.True(engine.Running);
        Assert.True(engine.Fps > 0);
        var started = DateTime.UtcNow;
        engine.Stop();
        Assert.False(engine.Running);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8));
        Assert.Null(engine.Error);
    }
}
