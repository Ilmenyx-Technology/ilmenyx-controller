// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// Adapted from the idea of RESET EXPLODE GpuGameBench.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)): independent Vulkan implementation, not the original D3D11 scene/shaders. See NOTICE.md.
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;

namespace Ilmenyx.Gpu.Vulkan;

public sealed record GpuSceneOptions(int AdapterIndex = 0, int Width = 1920, int Height = 1080, TimeSpan? Duration = null, int ShadowResolution = 4096, int TreeCount = 40_000, bool AllowGpuLoad = false)
{
    public const int MaxSeconds = 300;
    public TimeSpan EffectiveDuration => Duration ?? TimeSpan.FromSeconds(60);

    public static string? Validate(GpuSceneOptions o) =>
        !o.AllowGpuLoad ? "O benchmark de GPU exige consentimento explícito (gera carga máxima na GPU)."
        : o.AdapterIndex < 0 ? "Adaptador inválido."
        : o.Width is < 320 or > 3840 || o.Height is < 240 or > 2160 ? "Resolução de 320×240 a 3840×2160."
        : o.ShadowResolution is not (512 or 1024 or 2048 or 4096) ? "Mapa de sombras de 512 a 4096."
        : o.TreeCount is < 0 or > 200_000 ? "Árvores de 0 a 200.000."
        : o.EffectiveDuration <= TimeSpan.Zero || o.EffectiveDuration > TimeSpan.FromSeconds(MaxSeconds) ? $"Duração de 1 a {MaxSeconds} s."
        : null;
}

public sealed record GpuSceneResult(GpuRunStatus Status, string Adapter, int Width, int Height, long Frames, double Seconds, double? AverageFps, double? Low1Fps, double? Score, string? Reason)
{
    public const string Comparability = "Experimental: cena Vulkan independente do original; não comparável a scores do Windows nem entre drivers sem validação.";
}

public sealed record GpuSceneProgress(double Seconds, double Fraction, long Frames, double Fps);

/// <summary>
/// Game-like scene benchmark: procedural fBm terrain (512² grid), 40,000 instanced trees, 4096² shadow map with PCF, animated water, fog, HDR with tone mapping.
/// Fixed resolution and a camera path that depends only on time, so the work is the same on every GPU. Rendered offscreen without V-Sync (no window, no swap chain).
/// </summary>
public sealed unsafe class GpuScene
{
    private const int InFlight = 3, TerrainN = 512, VertsPerTree = 108;
    private const ulong FenceTimeoutNs = 8_000_000_000;
    private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.45f, -0.8f, -0.35f));
    private long frames;
    public long Frames => Interlocked.Read(ref frames);

    // ---- CPU mirror of common.glsl (camera clearance only; tiny float differences vs the GPU do not matter) ----
    private static float Fract(float x) => x - MathF.Floor(x);
    private static float Hash21(float px, float py)
    {
        px = Fract(px * 123.34f); py = Fract(py * 456.21f); float d = px * (px + 45.32f) + py * (py + 45.32f);
        px += d; py += d; return Fract(px * py);
    }
    private static float VNoise(float x, float y)
    {
        float ix = MathF.Floor(x), iy = MathF.Floor(y), fx = Fract(x), fy = Fract(y); fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash21(ix, iy), b = Hash21(ix + 1, iy), c = Hash21(ix, iy + 1), d = Hash21(ix + 1, iy + 1);
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }
    private static float Fbm(float x, float y) { float v = 0, a = 0.5f; for (int i = 0; i < 5; i++) { v += a * VNoise(x, y); x = x * 2.03f + 17; y = y * 2.03f + 9; a *= 0.5f; } return v; }
    public static float TerrainHeight(float x, float z)
    {
        float h = Fbm(x * 0.006f, z * 0.006f) * 70f - 18f, d = (x + 60f * MathF.Sin(z * 0.01f)) / 55f;
        float t = MathF.Exp(-d * d) * 0.9f; return h + (-4f - h) * t;
    }
    private static float CamHeight(Vector2 p) { float h = float.MinValue; for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) h = MathF.Max(h, TerrainHeight(p.X + dx * 10, p.Y + dz * 10)); return MathF.Max(h, 6f) + 28f; }

    /// <summary>Camera position and target at time fraction t in [0,1): a fixed closed path, identical on every machine.</summary>
    public static (Vector3 Eye, Vector3 Target) Camera(double t)
    {
        double a = t * 2 * Math.PI * 1.0, ahead = a + 0.08;
        var p = new Vector2((float)(230 * Math.Cos(a)), (float)(190 * Math.Sin(a))); var p2 = new Vector2((float)(230 * Math.Cos(ahead)), (float)(190 * Math.Sin(ahead)));
        return (new Vector3(p.X, CamHeight(p), p.Y), new Vector3(p2.X, CamHeight(p2) - 16, p2.Y));
    }

    [StructLayout(LayoutKind.Sequential)] private struct Push { public Matrix4x4 ViewProj, LightViewProj; }
    [StructLayout(LayoutKind.Sequential)] private struct FrameData { public Vector4 Eye, SunDir, Params; }

    private sealed class Res
    {
        public (Image Image, DeviceMemory Memory, ImageView View) Shadow, Hdr, Depth, Ldr;
        public Sampler Sampler; public DescriptorSetLayout SetLayout; public DescriptorPool Pool; public DescriptorSet Set; public PipelineLayout Layout;
        public Buffer Ubo; public DeviceMemory UboMemory; public byte* UboMap;
        public readonly List<ShaderModule> Shaders = []; public readonly List<Pipeline> Pipelines = [];
        public Pipeline ShadowTerrain, ShadowTrees, Terrain, Trees, Water, Tonemap;
        public readonly Fence[] Fences = new Fence[InFlight]; public readonly CommandBuffer[] Buffers = new CommandBuffer[InFlight]; public bool FencesCreated;
        public Buffer Readback; public DeviceMemory ReadbackMemory;
    }

    public Task<GpuSceneResult> RunAsync(GpuSceneOptions options, CancellationToken ct, IProgress<GpuSceneProgress>? progress = null)
    {
        if (GpuSceneOptions.Validate(options) is { } why) return Task.FromResult(new GpuSceneResult(GpuRunStatus.Unavailable, "", options.Width, options.Height, 0, 0, null, null, null, why));
        return Thread(() => Execute(options, ct, progress, null, 0));
    }

    /// <summary>Renders one frame at time fraction <paramref name="t"/> and writes it as a PNG (explicit request; a single frame of GPU work).</summary>
    public Task<GpuSceneResult> SnapshotAsync(GpuSceneOptions options, double t, string pngPath, CancellationToken ct)
    {
        if (GpuSceneOptions.Validate(options) is { } why) return Task.FromResult(new GpuSceneResult(GpuRunStatus.Unavailable, "", options.Width, options.Height, 0, 0, null, null, null, why));
        if (string.IsNullOrWhiteSpace(pngPath) || File.Exists(pngPath)) return Task.FromResult(new GpuSceneResult(GpuRunStatus.Unavailable, "", options.Width, options.Height, 0, 0, null, null, null, "Escolha um arquivo PNG novo (nunca sobrescreve)."));
        return Thread(() => Execute(options, ct, null, pngPath, Math.Clamp(t, 0, 0.9999)));
    }

    private static Task<GpuSceneResult> Thread(Func<GpuSceneResult> work)
    {
        var tcs = new TaskCompletionSource<GpuSceneResult>();
        new System.Threading.Thread(() => { try { tcs.SetResult(work()); } catch (Exception e) { tcs.SetException(e); } }) { IsBackground = true, Name = "GPU scene" }.Start();
        return tcs.Task;
    }

    private GpuSceneResult Execute(GpuSceneOptions o, CancellationToken ct, IProgress<GpuSceneProgress>? progress, string? snapshotPath, double snapshotT)
    {
        VulkanContext? cx = null;
        try
        {
            try { cx = new VulkanContext(o.AdapterIndex); }
            catch (VulkanException e) { return Unavailable(o, e.Message); }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException) { return Unavailable(o, "A biblioteca Vulkan (libvulkan.so.1) não foi encontrada."); }
            return Render(cx, o, ct, progress, snapshotPath, snapshotT);
        }
        catch (VulkanException e) { return new(GpuRunStatus.Failed, cx?.AdapterName ?? "", o.Width, o.Height, Frames, 0, null, null, null, e.Message); }
        finally { cx?.Dispose(); }
    }

    private static GpuSceneResult Unavailable(GpuSceneOptions o, string why) => new(GpuRunStatus.Unavailable, "", o.Width, o.Height, 0, 0, null, null, null, why);
    private static void Check(Result r, string what) { if (r != Result.Success) throw new VulkanException($"{what} falhou ({r}).", r); }

    private GpuSceneResult Render(VulkanContext cx, GpuSceneOptions o, CancellationToken ct, IProgress<GpuSceneProgress>? progress, string? snapshotPath, double snapshotT)
    {
        var vk = cx.Api; var dev = cx.Device; var r = new Res();
        uint w = (uint)o.Width, h = (uint)o.Height, sr = (uint)o.ShadowResolution;
        try
        {
            r.Shadow = cx.CreateImage(sr, sr, Format.D32Sfloat, ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit, SampleCountFlags.Count1Bit, ImageAspectFlags.DepthBit);
            r.Hdr = cx.CreateImage(w, h, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit, SampleCountFlags.Count1Bit, ImageAspectFlags.ColorBit);
            r.Depth = cx.CreateImage(w, h, Format.D32Sfloat, ImageUsageFlags.DepthStencilAttachmentBit, SampleCountFlags.Count1Bit, ImageAspectFlags.DepthBit);
            r.Ldr = cx.CreateImage(w, h, Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, SampleCountFlags.Count1Bit, ImageAspectFlags.ColorBit);
            CreateDescriptors(cx, r);
            CreatePipelines(cx, r);
            var fci = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
            for (int i = 0; i < InFlight; i++) Check(vk.CreateFence(dev, in fci, null, out r.Fences[i]), "fence");
            r.FencesCreated = true;
            var ai = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = cx.Pool, Level = CommandBufferLevel.Primary, CommandBufferCount = InFlight };
            fixed (CommandBuffer* b = r.Buffers) Check(vk.AllocateCommandBuffers(dev, in ai, b), "command buffers");

            if (snapshotPath is not null) return Snapshot(cx, r, o, snapshotPath, snapshotT);

            var times = new List<double>(8192); var clock = Stopwatch.StartNew(); double last = 0, nextReport = 0; int slot = 0; long done = 0;
            while (clock.Elapsed < o.EffectiveDuration)
            {
                if (ct.IsCancellationRequested) return Finish(cx, o, GpuRunStatus.Cancelled, done, clock, times, "Interrompido: nenhum valor é publicado.");
                var fence = r.Fences[slot];
                var wait = vk.WaitForFences(dev, 1, in fence, true, FenceTimeoutNs);
                if (wait == Result.Timeout) return Finish(cx, o, GpuRunStatus.Failed, done, clock, times, "A GPU não respondeu em 8 s (pode ter travado). O teste foi abortado.");
                Check(wait, "Esperar a GPU");
                double now = clock.Elapsed.TotalSeconds; if (done > 0 && times.Count < 200_000) times.Add(now - last); last = now;
                vk.ResetFences(dev, 1, in fence);
                double frac = clock.Elapsed.TotalSeconds / o.EffectiveDuration.TotalSeconds;
                RecordFrame(cx, r, o, slot, frac, (float)clock.Elapsed.TotalSeconds);
                var cmd = r.Buffers[slot]; var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
                var sres = vk.QueueSubmit(cx.Queue, 1, in submit, fence);
                if (sres is Result.ErrorDeviceLost) return Finish(cx, o, GpuRunStatus.Failed, done, clock, times, "A GPU foi reiniciada pelo driver (DEVICE_LOST): carga alta demais para esta GPU.");
                Check(sres, "Enviar comandos");
                done++; Interlocked.Exchange(ref frames, done); slot = (slot + 1) % InFlight;
                if (clock.Elapsed.TotalSeconds >= nextReport) { nextReport = clock.Elapsed.TotalSeconds + 0.5; progress?.Report(new(clock.Elapsed.TotalSeconds, Math.Min(1, frac), done, done / Math.Max(0.001, clock.Elapsed.TotalSeconds))); }
            }
            vk.DeviceWaitIdle(dev);
            return Finish(cx, o, GpuRunStatus.Completed, done, clock, times, null);
        }
        finally { vk.DeviceWaitIdle(dev); Destroy(cx, r); }
    }

    private static GpuSceneResult Finish(VulkanContext cx, GpuSceneOptions o, GpuRunStatus status, long frames, Stopwatch clock, List<double> times, string? reason)
    {
        double s = clock.Elapsed.TotalSeconds;
        if (status != GpuRunStatus.Completed || s <= 0 || frames == 0 || times.Count == 0) return new(status, cx.AdapterName, o.Width, o.Height, frames, s, null, null, null, reason);
        var worst = times.OrderByDescending(x => x).Take(Math.Max(1, times.Count / 100)).Average();
        double avg = frames / s;
        return new(status, cx.AdapterName, o.Width, o.Height, frames, s, avg, worst > 0 ? 1.0 / worst : null, avg * 10, null);
    }

    private GpuSceneResult Snapshot(VulkanContext cx, Res r, GpuSceneOptions o, string path, double t)
    {
        var vk = cx.Api; var dev = cx.Device; int w = o.Width, h = o.Height; ulong bytes = (ulong)w * (ulong)h * 4;
        (r.Readback, r.ReadbackMemory) = cx.CreateBuffer(bytes, BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        var sw = Stopwatch.StartNew();
        RecordFrame(cx, r, o, 0, t, (float)(t * 60), r.Readback);
        var cmd = r.Buffers[0]; var fence = r.Fences[0]; vk.ResetFences(dev, 1, in fence);
        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
        Check(vk.QueueSubmit(cx.Queue, 1, in submit, fence), "Enviar comandos");
        var wait = vk.WaitForFences(dev, 1, in fence, true, FenceTimeoutNs);
        if (wait == Result.Timeout) return new(GpuRunStatus.Failed, cx.AdapterName, w, h, 0, 0, null, null, null, "A GPU não respondeu em 8 s.");
        Check(wait, "Esperar a GPU");
        void* mapped; Check(vk.MapMemory(dev, r.ReadbackMemory, 0, bytes, 0, &mapped), "Mapear o buffer");
        byte[] png;
        try { png = PngWriter.Encode(new ReadOnlySpan<byte>(mapped, (int)bytes), w, h); }
        finally { vk.UnmapMemory(dev, r.ReadbackMemory); }
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(png);
        return new(GpuRunStatus.Completed, cx.AdapterName, w, h, 1, sw.Elapsed.TotalSeconds, null, null, null, null);
    }

    private static void Destroy(VulkanContext cx, Res r)
    {
        var vk = cx.Api; var dev = cx.Device;
        if (r.FencesCreated) foreach (var f in r.Fences) vk.DestroyFence(dev, f, null);
        foreach (var p in r.Pipelines) vk.DestroyPipeline(dev, p, null);
        foreach (var s in r.Shaders) vk.DestroyShaderModule(dev, s, null);
        if (r.Layout.Handle != 0) vk.DestroyPipelineLayout(dev, r.Layout, null);
        if (r.Pool.Handle != 0) vk.DestroyDescriptorPool(dev, r.Pool, null);
        if (r.SetLayout.Handle != 0) vk.DestroyDescriptorSetLayout(dev, r.SetLayout, null);
        if (r.Sampler.Handle != 0) vk.DestroySampler(dev, r.Sampler, null);
        if (r.UboMemory.Handle != 0) { vk.UnmapMemory(dev, r.UboMemory); vk.FreeMemory(dev, r.UboMemory, null); }
        if (r.Ubo.Handle != 0) vk.DestroyBuffer(dev, r.Ubo, null);
        if (r.ReadbackMemory.Handle != 0) vk.FreeMemory(dev, r.ReadbackMemory, null);
        if (r.Readback.Handle != 0) vk.DestroyBuffer(dev, r.Readback, null);
        foreach (var i in new[] { r.Shadow, r.Hdr, r.Depth, r.Ldr }) if (i.Image.Handle != 0) { vk.DestroyImageView(dev, i.View, null); vk.DestroyImage(dev, i.Image, null); vk.FreeMemory(dev, i.Memory, null); }
    }

    private static void CreateDescriptors(VulkanContext cx, Res r)
    {
        var vk = cx.Api; var dev = cx.Device;
        var sci = new SamplerCreateInfo { SType = StructureType.SamplerCreateInfo, MagFilter = Filter.Nearest, MinFilter = Filter.Nearest, AddressModeU = SamplerAddressMode.ClampToEdge, AddressModeV = SamplerAddressMode.ClampToEdge, AddressModeW = SamplerAddressMode.ClampToEdge, MaxLod = 0 };
        Check(vk.CreateSampler(dev, in sci, null, out r.Sampler), "sampler");
        var bindings = stackalloc DescriptorSetLayoutBinding[3];
        bindings[0] = new DescriptorSetLayoutBinding { Binding = 0, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit };
        bindings[1] = new DescriptorSetLayoutBinding { Binding = 1, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit };
        bindings[2] = new DescriptorSetLayoutBinding { Binding = 2, DescriptorType = DescriptorType.UniformBufferDynamic, DescriptorCount = 1, StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit };
        var lci = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 3, PBindings = bindings };
        Check(vk.CreateDescriptorSetLayout(dev, in lci, null, out r.SetLayout), "descriptor set layout");
        var sizes = stackalloc DescriptorPoolSize[2] { new(DescriptorType.CombinedImageSampler, 2), new(DescriptorType.UniformBufferDynamic, 1) };
        var pci = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 2, PPoolSizes = sizes };
        Check(vk.CreateDescriptorPool(dev, in pci, null, out r.Pool), "descriptor pool");
        var layout = r.SetLayout; var ai = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = r.Pool, DescriptorSetCount = 1, PSetLayouts = &layout };
        Check(vk.AllocateDescriptorSets(dev, in ai, out r.Set), "descriptor set");
        (r.Ubo, r.UboMemory) = cx.CreateBuffer(256 * InFlight, BufferUsageFlags.UniformBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        void* map; Check(vk.MapMemory(dev, r.UboMemory, 0, 256 * InFlight, 0, &map), "map ubo"); r.UboMap = (byte*)map;
        var shadowInfo = new DescriptorImageInfo(r.Sampler, r.Shadow.View, ImageLayout.DepthStencilReadOnlyOptimal);
        var hdrInfo = new DescriptorImageInfo(r.Sampler, r.Hdr.View, ImageLayout.ShaderReadOnlyOptimal);
        var bufInfo = new DescriptorBufferInfo(r.Ubo, 0, (ulong)sizeof(FrameData));
        var writes = stackalloc WriteDescriptorSet[3];
        writes[0] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = r.Set, DstBinding = 0, DescriptorCount = 1, DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = &shadowInfo };
        writes[1] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = r.Set, DstBinding = 1, DescriptorCount = 1, DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = &hdrInfo };
        writes[2] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = r.Set, DstBinding = 2, DescriptorCount = 1, DescriptorType = DescriptorType.UniformBufferDynamic, PBufferInfo = &bufInfo };
        vk.UpdateDescriptorSets(dev, 3, writes, 0, null);
        var range = new PushConstantRange(ShaderStageFlags.VertexBit, 0, (uint)sizeof(Push));
        var set = r.SetLayout; var plci = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &set, PushConstantRangeCount = 1, PPushConstantRanges = &range };
        Check(vk.CreatePipelineLayout(dev, in plci, null, out r.Layout), "pipeline layout");
    }

    private static void CreatePipelines(VulkanContext cx, Res r)
    {
        ShaderModule Load(string n) { var m = cx.LoadShader(n); r.Shaders.Add(m); return m; }
        var terrainV = Load("scene_terrain.vert.spv"); var treesV = Load("scene_trees.vert.spv"); var waterV = Load("scene_water.vert.spv"); var toneV = Load("tonemap.vert.spv");
        var shadowF = Load("scene_shadow.frag.spv"); var litF = Load("scene_lit.frag.spv"); var waterF = Load("scene_water.frag.spv"); var toneF = Load("tonemap.frag.spv");
        r.ShadowTerrain = Make(cx, r, terrainV, shadowF, colorFormat: null, depth: true, depthWrite: true, blend: false, bias: true);
        r.ShadowTrees = Make(cx, r, treesV, shadowF, null, true, true, false, true);
        r.Terrain = Make(cx, r, terrainV, litF, Format.R16G16B16A16Sfloat, true, true, false, false);
        r.Trees = Make(cx, r, treesV, litF, Format.R16G16B16A16Sfloat, true, true, false, false);
        r.Water = Make(cx, r, waterV, waterF, Format.R16G16B16A16Sfloat, true, false, true, false);
        r.Tonemap = Make(cx, r, toneV, toneF, Format.R8G8B8A8Unorm, false, false, false, false);
    }

    private static Pipeline Make(VulkanContext cx, Res r, ShaderModule vert, ShaderModule frag, Format? colorFormat, bool depth, bool depthWrite, bool blend, bool bias)
    {
        var vk = cx.Api; var name = (byte*)Marshal.StringToHGlobalAnsi("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = vert, PName = name };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = frag, PName = name };
            var vi = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
            var ia = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };
            var vp = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };
            var rs = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1, DepthBiasEnable = bias, DepthBiasConstantFactor = 1.5f, DepthBiasSlopeFactor = 2.0f };
            var ms = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit };
            var ds = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = depth, DepthWriteEnable = depthWrite, DepthCompareOp = CompareOp.Less };
            var attach = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit, BlendEnable = blend,
                SrcColorBlendFactor = BlendFactor.SrcAlpha, DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha, ColorBlendOp = BlendOp.Add, SrcAlphaBlendFactor = BlendFactor.One, DstAlphaBlendFactor = BlendFactor.Zero, AlphaBlendOp = BlendOp.Add
            };
            var cb = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = colorFormat is null ? 0u : 1u, PAttachments = &attach };
            var dyn = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dyni = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dyn };
            var cf = colorFormat ?? Format.Undefined;
            var rendering = new PipelineRenderingCreateInfo { SType = StructureType.PipelineRenderingCreateInfo, ColorAttachmentCount = colorFormat is null ? 0u : 1u, PColorAttachmentFormats = &cf, DepthAttachmentFormat = depth ? Format.D32Sfloat : Format.Undefined };
            var gci = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo, PNext = &rendering, StageCount = 2, PStages = stages, PVertexInputState = &vi, PInputAssemblyState = &ia, PViewportState = &vp,
                PRasterizationState = &rs, PMultisampleState = &ms, PDepthStencilState = &ds, PColorBlendState = &cb, PDynamicState = &dyni, Layout = r.Layout
            };
            Check(vk.CreateGraphicsPipelines(cx.Device, default, 1, in gci, null, out var pipeline), "Criar o pipeline gráfico");
            r.Pipelines.Add(pipeline);
            return pipeline;
        }
        finally { Marshal.FreeHGlobal((nint)name); }
    }

    private static void RecordFrame(VulkanContext cx, Res r, GpuSceneOptions o, int slot, double t, float seconds, Buffer? readback = null)
    {
        var vk = cx.Api; var cmd = r.Buffers[slot]; uint w = (uint)o.Width, h = (uint)o.Height, sr = (uint)o.ShadowResolution;
        // Per-frame values (this slot's 256-byte region; the fence wait guarantees the GPU is done with it).
        var (eye, target) = Camera(t);
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, w / (float)h, 0.5f, 2000f); proj.M22 *= -1;
        var lightView = Matrix4x4.CreateLookAt(-SunDirection * 500f, Vector3.Zero, Vector3.UnitY);
        var lightProj = Matrix4x4.CreateOrthographic(1000, 1000, 1, 1100); lightProj.M22 *= -1;
        var viewProj = view * proj; var lightViewProj = lightView * lightProj;
        var frame = new FrameData { Eye = new(eye, 1), SunDir = new(SunDirection, 0), Params = new(seconds, 1f / sr, 0, 0) };
        *(FrameData*)(r.UboMap + slot * 256) = frame;
        uint dynamicOffset = (uint)(slot * 256);

        vk.ResetCommandBuffer(cmd, 0);
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        Check(vk.BeginCommandBuffer(cmd, in begin), "Iniciar o buffer de comandos");
        void Barrier(Image image, ImageAspectFlags aspect, ImageLayout from, ImageLayout to, PipelineStageFlags srcStage, AccessFlags srcAccess, PipelineStageFlags dstStage, AccessFlags dstAccess)
        {
            var b = new ImageMemoryBarrier { SType = StructureType.ImageMemoryBarrier, OldLayout = from, NewLayout = to, SrcAccessMask = srcAccess, DstAccessMask = dstAccess, Image = image, SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1) };
            vk.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, in b);
        }
        const PipelineStageFlags All = PipelineStageFlags.AllCommandsBit;
        const AccessFlags AnyAccess = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit;
        // Full dependency on whatever the previous frame still does with these images (frames serialize on the GPU; the CPU still queues ahead).
        Barrier(r.Shadow.Image, ImageAspectFlags.DepthBit, ImageLayout.Undefined, ImageLayout.DepthAttachmentOptimal, All, AnyAccess, PipelineStageFlags.EarlyFragmentTestsBit, AccessFlags.DepthStencilAttachmentWriteBit);
        Barrier(r.Hdr.Image, ImageAspectFlags.ColorBit, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal, All, AnyAccess, PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit);
        Barrier(r.Depth.Image, ImageAspectFlags.DepthBit, ImageLayout.Undefined, ImageLayout.DepthAttachmentOptimal, All, AnyAccess, PipelineStageFlags.EarlyFragmentTestsBit, AccessFlags.DepthStencilAttachmentWriteBit);
        Barrier(r.Ldr.Image, ImageAspectFlags.ColorBit, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal, All, AnyAccess, PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit);

        var set = r.Set; vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, r.Layout, 0, 1, in set, 1, in dynamicOffset);
        void SetViewport(uint vw, uint vh)
        {
            var viewport = new Viewport(0, 0, vw, vh, 0, 1); var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(vw, vh));
            vk.CmdSetViewport(cmd, 0, 1, in viewport); vk.CmdSetScissor(cmd, 0, 1, in scissor);
        }
        void Push(Matrix4x4 vpMatrix) { var p = new Push { ViewProj = vpMatrix, LightViewProj = lightViewProj }; vk.CmdPushConstants(cmd, r.Layout, ShaderStageFlags.VertexBit, 0, (uint)sizeof(Push), &p); }
        const uint TerrainVerts = (TerrainN - 1) * (TerrainN - 1) * 6;

        // 1) Shadow map
        var sd = new RenderingAttachmentInfo { SType = StructureType.RenderingAttachmentInfo, ImageView = r.Shadow.View, ImageLayout = ImageLayout.DepthAttachmentOptimal, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1, 0) } };
        var shadowRi = new RenderingInfo { SType = StructureType.RenderingInfo, RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(sr, sr)), LayerCount = 1, PDepthAttachment = &sd };
        vk.CmdBeginRendering(cmd, in shadowRi); SetViewport(sr, sr); Push(lightViewProj);
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, r.ShadowTerrain); vk.CmdDraw(cmd, TerrainVerts, 1, 0, 0);
        if (o.TreeCount > 0) { vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, r.ShadowTrees); vk.CmdDraw(cmd, VertsPerTree, (uint)o.TreeCount, 0, 0); }
        vk.CmdEndRendering(cmd);
        Barrier(r.Shadow.Image, ImageAspectFlags.DepthBit, ImageLayout.DepthAttachmentOptimal, ImageLayout.DepthStencilReadOnlyOptimal, PipelineStageFlags.LateFragmentTestsBit, AccessFlags.DepthStencilAttachmentWriteBit, PipelineStageFlags.FragmentShaderBit, AccessFlags.ShaderReadBit);

        // 2) Main pass (HDR)
        var hc = new RenderingAttachmentInfo { SType = StructureType.RenderingAttachmentInfo, ImageView = r.Hdr.View, ImageLayout = ImageLayout.ColorAttachmentOptimal, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new ClearValue { Color = new ClearColorValue(0.77f, 0.91f, 1.12f, 1) } };
        var hd = new RenderingAttachmentInfo { SType = StructureType.RenderingAttachmentInfo, ImageView = r.Depth.View, ImageLayout = ImageLayout.DepthAttachmentOptimal, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.DontCare, ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1, 0) } };
        var mainRi = new RenderingInfo { SType = StructureType.RenderingInfo, RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(w, h)), LayerCount = 1, ColorAttachmentCount = 1, PColorAttachments = &hc, PDepthAttachment = &hd };
        vk.CmdBeginRendering(cmd, in mainRi); SetViewport(w, h); Push(viewProj);
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, r.Terrain); vk.CmdDraw(cmd, TerrainVerts, 1, 0, 0);
        if (o.TreeCount > 0) { vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, r.Trees); vk.CmdDraw(cmd, VertsPerTree, (uint)o.TreeCount, 0, 0); }
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, r.Water); vk.CmdDraw(cmd, 6, 1, 0, 0);
        vk.CmdEndRendering(cmd);
        Barrier(r.Hdr.Image, ImageAspectFlags.ColorBit, ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal, PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit, PipelineStageFlags.FragmentShaderBit, AccessFlags.ShaderReadBit);

        // 3) Tone mapping to LDR
        var lc = new RenderingAttachmentInfo { SType = StructureType.RenderingAttachmentInfo, ImageView = r.Ldr.View, ImageLayout = ImageLayout.ColorAttachmentOptimal, LoadOp = AttachmentLoadOp.DontCare, StoreOp = AttachmentStoreOp.Store };
        var toneRi = new RenderingInfo { SType = StructureType.RenderingInfo, RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(w, h)), LayerCount = 1, ColorAttachmentCount = 1, PColorAttachments = &lc };
        vk.CmdBeginRendering(cmd, in toneRi); SetViewport(w, h);
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, r.Tonemap); vk.CmdDraw(cmd, 3, 1, 0, 0);
        vk.CmdEndRendering(cmd);

        if (readback is { } rb)
        {
            Barrier(r.Ldr.Image, ImageAspectFlags.ColorBit, ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal, PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit, PipelineStageFlags.TransferBit, AccessFlags.TransferReadBit);
            var copy = new BufferImageCopy { ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1), ImageExtent = new Extent3D(w, h, 1) };
            vk.CmdCopyImageToBuffer(cmd, r.Ldr.Image, ImageLayout.TransferSrcOptimal, rb, 1, in copy);
        }
        Check(vk.EndCommandBuffer(cmd), "Finalizar o buffer de comandos");
    }
}
