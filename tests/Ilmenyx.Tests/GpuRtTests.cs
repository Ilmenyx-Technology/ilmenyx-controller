// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// Ray tracing benchmark (adapted from RESET EXPLODE GpuRtBench, MIT): math, limits, adapter choice and CLI parsing.
// No test here creates a Vulkan device or renders, except the opt-in one guarded by ILMENYX_RUN_GPU_TESTS=1.
using System.Text.Json;
using Ilmenyx.Cli;
using Ilmenyx.Desktop;
using Ilmenyx.Gpu.Vulkan;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class GpuRtTests
{
    private static GpuRtResult Result(params double[] fps) =>
        new(GpuRunStatus.Completed, "GPU", "alta", 1920, 1080, fps.Select(f => new RtPass(f, f * 0.9, 100)).ToList(), 0, true, null);

    [Fact]
    public void MedianHandlesOddEvenAndEmpty()
    {
        Assert.Equal(2, GpuRtResult.Median([3, 1, 2]));
        Assert.Equal(2.5, GpuRtResult.Median([4, 1, 3, 2]));
        Assert.Equal(0, GpuRtResult.Median([]));
    }

    [Fact]
    public void ScoreIsMedianFpsTimesTenAndResistsOneBadPass()
    {
        var r = Result(50, 10, 51);
        Assert.Equal(50, r.MedianFps);
        Assert.Equal(500, r.Score);
        Assert.Equal(45, r.MedianLow1, 6);
    }

    [Fact]
    public void VariationIsSpreadOverMedianInPercent()
    {
        Assert.Equal((10.2 - 9.9) / 10 * 100, Result(10, 10.2, 9.9).VariationPct, 9);
        Assert.Equal(0, Result(10).VariationPct);
        Assert.Equal(0, Result(0, 0, 0).VariationPct);
    }

    [Fact]
    public void ReliableNeedsThreePassesBelowThreePercentAndACompletedRun()
    {
        Assert.True(Result(10, 10.1, 10.05).Reliable);
        Assert.False(Result(10, 10.3, 10).Reliable);           // exactly 3 % is not below 3 %
        Assert.False(Result(10, 10.01).Reliable);              // two passes
        Assert.False((Result(10, 10, 10) with { Status = GpuRunStatus.Cancelled }).Reliable);
    }

    [Fact]
    public void PassFromFrameTimesMatchesTheOriginalFormulas()
    {
        var even = GpuRtResult.PassFrom(Enumerable.Repeat(100.0, 10).ToList());
        Assert.Equal(10, even.AvgFps, 9); Assert.Equal(10, even.Low1Fps, 9); Assert.Equal(10, even.Frames);
        var times = Enumerable.Repeat(10.0, 98).Concat([50.0, 50.0]).ToList();   // two slow frames among 100
        var pass = GpuRtResult.PassFrom(times);
        Assert.Equal(100 / (98 * 0.01 + 0.1), pass.AvgFps, 9);
        Assert.Equal(20, pass.Low1Fps, 9);                     // 99th percentile = sorted[(int)(100 * 0.99)] = 50 ms
        Assert.Equal(new RtPass(0, 0, 0), GpuRtResult.PassFrom([]));
    }

    [Fact]
    public void PresetsAndResolutionsAreTheOriginalOnes()
    {
        Assert.Equal((2u, 2u), GpuRtPresets.Quality("normal"));
        Assert.Equal((4u, 3u), GpuRtPresets.Quality("alta"));
        Assert.Equal((8u, 4u), GpuRtPresets.Quality("extrema"));
        Assert.Null(GpuRtPresets.Quality("ultra"));
        Assert.Equal((1920, 1080), GpuRtPresets.Size("1080p"));
        Assert.Equal((2560, 1440), GpuRtPresets.Size("1440p"));
        Assert.Equal((3840, 2160), GpuRtPresets.Size("4K"));
        Assert.Null(GpuRtPresets.Size("720p"));
        Assert.Equal(12, GpuRtPresets.RaysPerPixel("normal"));
        Assert.Equal(36, GpuRtPresets.RaysPerPixel("alta"));
        Assert.Equal(96, GpuRtPresets.RaysPerPixel("extrema"));
        Assert.Equal("Extrema", GpuRtPresets.Label("extrema"));
    }

    [Fact]
    public void OptionsRefuseWithoutConsentAndOutsideTheLimits()
    {
        Assert.NotNull(GpuRtOptions.Validate(new GpuRtOptions()));
        Assert.Null(GpuRtOptions.Validate(new GpuRtOptions(AllowGpuLoad: true)));
        Assert.Null(GpuRtOptions.Validate(new GpuRtOptions(0, "normal", "4k", TimeSpan.Zero, TimeSpan.FromSeconds(60), 5, true)));
        var ok = new GpuRtOptions(AllowGpuLoad: true);
        Assert.NotNull(GpuRtOptions.Validate(ok with { AdapterIndex = -1 }));
        Assert.NotNull(GpuRtOptions.Validate(ok with { Preset = "ultra" }));
        Assert.NotNull(GpuRtOptions.Validate(ok with { Resolution = "8k" }));
        Assert.NotNull(GpuRtOptions.Validate(ok with { Warmup = TimeSpan.FromSeconds(61) }));
        Assert.NotNull(GpuRtOptions.Validate(ok with { Warmup = TimeSpan.FromSeconds(-1) }));
        Assert.NotNull(GpuRtOptions.Validate(ok with { PassDuration = TimeSpan.FromSeconds(0.5) }));
        Assert.NotNull(GpuRtOptions.Validate(ok with { PassDuration = TimeSpan.FromSeconds(61) }));
        Assert.NotNull(GpuRtOptions.Validate(ok with { Passes = 0 }));
        Assert.NotNull(GpuRtOptions.Validate(ok with { Passes = 6 }));
        Assert.Equal(TimeSpan.FromSeconds(10 + 3 * 30), ok.TotalDuration);
    }

    [Fact]
    public async Task InvalidOptionsNeverTouchTheGpu()
    {
        var engine = new GpuRtBench();
        var r = await engine.RunAsync(new GpuRtOptions(AllowGpuLoad: false), CancellationToken.None);
        Assert.Equal(GpuRunStatus.Unavailable, r.Status);
        Assert.Empty(r.Passes);
        Assert.Equal(0, engine.Frames);
        string existing = Path.GetTempFileName();
        try { Assert.Equal(GpuRunStatus.Unavailable, (await engine.SnapshotAsync(new GpuRtOptions(AllowGpuLoad: true), 0.1, existing, CancellationToken.None)).Status); }
        finally { File.Delete(existing); }
    }

    private static VulkanAdapter Adapter(int index, string name, string kind, bool rayQuery, bool software = false, bool vk13 = true) =>
        new(index, name, kind, 0x1002, 0x1506, vk13 ? "1.3.0" : "1.2.0", 1 << 30, software, vk13, rayQuery, rayQuery);

    [Fact]
    public void PickNeverChoosesLlvmpipeUnlessAskedExplicitly()
    {
        var llvmpipe = Adapter(1, "llvmpipe (LLVM 20)", "CPU (software)", true, software: true);
        var (none, why) = GpuRtSupport.Pick(new VulkanDeviceList([llvmpipe], null));
        Assert.Null(none);
        Assert.Contains("--adapter", why);
        var radv = Adapter(0, "AMD Radeon 610M (RADV)", "GPU integrada", true);
        Assert.Equal("AMD Radeon 610M (RADV)", GpuRtSupport.Pick(new VulkanDeviceList([llvmpipe, radv], null)).Adapter!.Name);
        Assert.Same(llvmpipe, GpuRtSupport.Pick(new VulkanDeviceList([radv, llvmpipe], null), 1).Adapter);
    }

    [Fact]
    public void PickExplainsMissingRayTracingLikeTigerLake()
    {
        var tigerLake = Adapter(0, "Intel(R) Xe Graphics (TGL GT2)", "GPU integrada", false);
        var (a, why) = GpuRtSupport.Pick(new VulkanDeviceList([tigerLake, Adapter(1, "llvmpipe", "CPU (software)", true, software: true)], null));
        Assert.Null(a);
        Assert.Contains("TGL GT2", why);
        Assert.Contains("VK_KHR_ray_query", why);
        Assert.Contains("RX 6000", why);
        Assert.Contains("Vulkan 1.3", GpuRtSupport.Missing(Adapter(0, "Old", "GPU dedicada", true, vk13: false)));
        Assert.Contains("inexistente", GpuRtSupport.Pick(new VulkanDeviceList([tigerLake], null), 3).Reason);
        Assert.Equal("sem loader", GpuRtSupport.Pick(new VulkanDeviceList([], "sem loader")).Reason);
        Assert.Contains("Nenhum", GpuRtSupport.Pick(new VulkanDeviceList([], null)).Reason);
    }

    [Fact]
    public void PickPrefersADedicatedGpu()
    {
        var igpu = Adapter(0, "iGPU", "GPU integrada", true);
        var dgpu = Adapter(1, "dGPU", "GPU dedicada", true);
        Assert.Same(dgpu, GpuRtSupport.Pick(new VulkanDeviceList([igpu, dgpu], null)).Adapter);
        var info = GpuRtInfo.Detect(new VulkanDeviceList([igpu, dgpu], null));
        Assert.Null(info.Reason); Assert.Equal(1, info.AdapterIndex); Assert.False(info.SharesCpuThermals);
        Assert.True(GpuRtInfo.Detect(new VulkanDeviceList([igpu], null)).SharesCpuThermals);
        Assert.NotNull(GpuRtInfo.Detect(new VulkanDeviceList([], null)).Reason);
    }

    [Fact]
    public void SceneIsTheDeterministicOriginalOne()
    {
        var s = GpuRtBench.SceneStats();
        Assert.Equal(4000, s.Objects);
        Assert.Equal(s, GpuRtBench.SceneStats());
        Assert.Equal(s.Objects - 1, s.Spheres + s.Tori + s.Columns + s.Boxes);   // everything but the ground
        Assert.True(s.Spheres > 7 && s.Tori > 0 && s.Columns > 0 && s.Boxes > 0);
        Assert.True(s.Metal >= 7 && s.Emissive > 0 && s.Triangles > 1_000_000);
        var (eye, target) = GpuRtBench.CameraAt(0.25f);
        Assert.Equal((eye, target), GpuRtBench.CameraAt(0.25f));
        Assert.InRange(eye.Y, 12, 22);
    }

    [Fact]
    public void ComputeShaderIsEmbeddedSpirV()
    {
        using var stream = typeof(VulkanDevices).Assembly.GetManifestResourceStream("Shaders.rt_trace.comp.spv");
        Assert.NotNull(stream);
        var header = new byte[8]; stream!.ReadExactly(header);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(header, 0));
    }

    [Fact]
    public void JsonCarriesTheDerivedValues()
    {
        string json = JsonSerializer.Serialize(Result(10, 10.1, 10.05));
        foreach (var key in new[] { "MedianFps", "VariationPct", "Reliable", "Score", "GigaRaysPerSec", "Passes" }) Assert.Contains($"\"{key}\"", json);
    }

    private static Dictionary<string, string?> Flags(params string[] args)
    {
        var d = new Dictionary<string, string?>();
        for (int i = 0; i < args.Length; i++) d[args[i]] = args[i] is "--allow-gpu-load" or "--json" ? null : args[++i];
        return d;
    }

    [Fact]
    public void CliNeedsConsentAndUsesTheQuickDefaults()
    {
        Assert.Contains("--allow-gpu-load", GpuRtCommand.Parse("gpu-rt", Flags()).Error);
        var (req, err) = GpuRtCommand.Parse("gpu-rt", Flags("--allow-gpu-load"));
        Assert.Null(err);
        Assert.Equal("alta", req!.Options.Preset); Assert.Equal("1080p", req.Options.Resolution); Assert.Null(req.Adapter);
        Assert.Equal(TimeSpan.FromSeconds(5), req.Options.EffectiveWarmup); Assert.Equal(TimeSpan.FromSeconds(10), req.Options.EffectivePass); Assert.Equal(3, req.Options.Passes);
        Assert.Equal(90, req.TempLimit); Assert.True(req.Options.AllowGpuLoad);
        Assert.Null(GpuRtOptions.Validate(req.Options));
    }

    [Fact]
    public void CliParsesEveryOptionWithinItsLimits()
    {
        var (req, err) = GpuRtCommand.Parse("gpu-rt", Flags("--allow-gpu-load", "--adapter", "1", "--preset", "NORMAL", "--resolution", "4K", "--warmup-seconds", "0", "--pass-seconds", "60", "--passes", "5", "--temp-limit", "85.5", "--json"));
        Assert.Null(err);
        Assert.Equal(1, req!.Adapter); Assert.Equal("normal", req.Options.Preset); Assert.Equal(3840, req.Options.Width);
        Assert.Equal(TimeSpan.Zero, req.Options.EffectiveWarmup); Assert.Equal(5, req.Options.Passes); Assert.Equal(85.5, req.TempLimit); Assert.True(req.Json);
        foreach (var bad in new[] { new[] { "--preset", "ultra" }, ["--resolution", "720p"], ["--passes", "6"], ["--passes", "0"], ["--pass-seconds", "61"], ["--pass-seconds", "0"],
                     ["--warmup-seconds", "61"], ["--adapter", "-1"], ["--adapter", "17"], ["--temp-limit", "40"], ["--temp-limit", "x"], ["--seconds", "5"], ["--output", "/tmp/a.png"] })
            Assert.NotNull(GpuRtCommand.Parse("gpu-rt", Flags(["--allow-gpu-load", .. bad])).Error);
    }

    [Fact]
    public void CliSnapshotNeedsANewAbsolutePng()
    {
        Assert.NotNull(GpuRtCommand.Parse("gpu-rt-snapshot", Flags("--allow-gpu-load")).Error);
        Assert.NotNull(GpuRtCommand.Parse("gpu-rt-snapshot", Flags("--allow-gpu-load", "--output", "rel.png")).Error);
        Assert.NotNull(GpuRtCommand.Parse("gpu-rt-snapshot", Flags("--allow-gpu-load", "--output", "/tmp/x.jpg")).Error);
        Assert.NotNull(GpuRtCommand.Parse("gpu-rt-snapshot", Flags("--allow-gpu-load", "--output", "/tmp/x.png", "--t", "2")).Error);
        var (req, err) = GpuRtCommand.Parse("gpu-rt-snapshot", Flags("--allow-gpu-load", "--output", "/tmp/x.png", "--t", "0.5"));
        Assert.Null(err); Assert.Equal("/tmp/x.png", req!.Output); Assert.Equal(0.5, req.T);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task CliRoutesGpuRtAndRefusesWithoutConsentBeforeAnyDevice()
    {
        Assert.True(HeavyBenchCommand.Handles(["bench", "gpu-rt"]));
        Assert.True(HeavyBenchCommand.Handles(["bench", "gpu-rt-snapshot"]));
        Assert.Contains("gpu-rt --allow-gpu-load", HeavyBenchCommand.Help);
        var err = Console.Error;
        try { Console.SetError(TextWriter.Null); Assert.Equal(2, await HeavyBenchCommand.RunAsync(["bench", "gpu-rt", "--preset", "normal"], CancellationToken.None)); }
        finally { Console.SetError(err); }
    }

    [Fact]
    public void ThermalProbeReadsCpuPackageAndTheMatchingDrmCard()
    {
        string root = Directory.CreateTempSubdirectory("ilx-thermal").FullName;
        try
        {
            void W(string rel, string text) { string p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }
            W("class/hwmon/hwmon0/name", "amdgpu\n"); W("class/hwmon/hwmon0/temp1_input", "99000\n");
            W("class/hwmon/hwmon4/name", "k10temp\n"); W("class/hwmon/hwmon4/temp1_input", "41000\n"); W("class/hwmon/hwmon4/temp1_label", "Tctl\n");
            W("class/hwmon/hwmon4/temp3_input", "39000\n"); W("class/hwmon/hwmon4/temp3_label", "Tccd1\n");
            W("class/drm/card1/device/vendor", "0x1002\n"); W("class/drm/card1/device/device", "0x1506\n");
            W("class/drm/card1/device/hwmon/hwmon6/temp1_input", "47000\n"); W("class/drm/card1/device/hwmon/hwmon6/temp2_input", "52000\n");
            W("class/drm/card0/device/vendor", "0x10de\n"); W("class/drm/card0/device/device", "0x2684\n");
            Assert.Equal(41, LinuxThermalProbe.CpuTempC(root));
            Assert.Equal(52, LinuxThermalProbe.GpuTempC(0x1002, 0x1506, root));
            Assert.Null(LinuxThermalProbe.GpuTempC(0x10de, 0x2684, root));      // no hwmon (e.g. proprietary NVIDIA): nothing inferred
            Assert.Null(LinuxThermalProbe.GpuTempC(0x8086, 0x9a49, root));
            Assert.Null(LinuxThermalProbe.CpuTempC(Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Opt-in only (ILMENYX_RUN_GPU_TESTS=1): one short real pass (normal, 1080p, no warm-up, 2 s). Without the variable it does nothing, so a pass here proves nothing about a GPU.</summary>
    [Fact]
    public async Task RealShortPassOnlyWithOptInEnvironmentVariable()
    {
        if (Environment.GetEnvironmentVariable("ILMENYX_RUN_GPU_TESTS") != "1") return;
        var (adapter, _) = GpuRtSupport.Pick(VulkanDevices.List());
        if (adapter is null) return;
        var r = await new GpuRtBench().RunAsync(new GpuRtOptions(adapter.Index, "normal", "1080p", TimeSpan.Zero, TimeSpan.FromSeconds(2), 1, true), CancellationToken.None);
        Assert.True(r.Status == GpuRunStatus.Completed, r.Reason);
        Assert.True(r.MedianFps > 0);
        Assert.Single(r.Passes);
    }
}
