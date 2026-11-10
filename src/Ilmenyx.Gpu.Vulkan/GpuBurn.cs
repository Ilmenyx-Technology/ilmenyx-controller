// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Adapted from the idea of RESET EXPLODE GpuBurn.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)): independent Vulkan implementation, not the original D3D11 shaders. See NOTICE.md.
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;

namespace Ilmenyx.Gpu.Vulkan;

public enum GpuRunStatus { Completed, Cancelled, TimedOut, Failed, Unavailable }

public sealed record GpuBurnOptions(int AdapterIndex = 0, int Width = 1920, int Height = 1080, int Msaa = 4, int Shells = 32, int Iterations = 24, TimeSpan? Duration = null, bool AllowGpuLoad = false)
{
    public const int MaxSeconds = 600;
    public TimeSpan EffectiveDuration => Duration ?? TimeSpan.FromSeconds(20);

    public static string? Validate(GpuBurnOptions o) =>
        !o.AllowGpuLoad ? "O stress de GPU exige consentimento explícito (gera carga máxima na GPU)."
        : o.AdapterIndex < 0 ? "Adaptador inválido."
        : o.Width is < 320 or > 3840 || o.Height is < 240 or > 2160 ? "Resolução de 320×240 a 3840×2160."
        : o.Msaa is not (1 or 2 or 4 or 8) ? "MSAA 1, 2, 4 ou 8."
        : o.Shells is < 1 or > 256 ? "Camadas de 1 a 256."
        : o.Iterations is < 0 or > 512 ? "Iterações de 0 a 512."
        : o.EffectiveDuration <= TimeSpan.Zero || o.EffectiveDuration > TimeSpan.FromSeconds(MaxSeconds) ? $"Duração de 1 a {MaxSeconds} s."
        : null;
}

public sealed record GpuBurnProgress(double Seconds, long Frames, double Fps);
public sealed record GpuBurnResult(GpuRunStatus Status, string Adapter, long Frames, double Seconds, double? AverageFps, string? Reason);

/// <summary>
/// FurMark-style stress: a furry torus drawn as inflated shells (massive overdraw) with a data-dependent ALU loop, rendered offscreen without
/// V-Sync and with a small frame queue. Starts only with AllowGpuLoad, stops on duration/cancel/timeout, and always frees the device.
/// </summary>
public sealed unsafe class GpuBurn
{
    private const int InFlight = 3;
    private const ulong FenceTimeoutNs = 5_000_000_000;
    private long frames;
    public long Frames => Interlocked.Read(ref frames);

    [StructLayout(LayoutKind.Sequential)]
    private struct Push { public Matrix4x4 ViewProj; public float Time, Shells; public int Iterations; public float Aspect; }

    public Task<GpuBurnResult> RunAsync(GpuBurnOptions options, CancellationToken ct, IProgress<GpuBurnProgress>? progress = null)
    {
        if (GpuBurnOptions.Validate(options) is { } why) return Task.FromResult(new GpuBurnResult(GpuRunStatus.Unavailable, "", 0, 0, null, why));
        var tcs = new TaskCompletionSource<GpuBurnResult>();
        new Thread(() => { try { tcs.SetResult(Run(options, ct, progress)); } catch (Exception e) { tcs.SetException(e); } }) { IsBackground = true, Name = "GPU burn" }.Start();
        return tcs.Task;
    }

    private GpuBurnResult Run(GpuBurnOptions o, CancellationToken ct, IProgress<GpuBurnProgress>? progress)
    {
        VulkanContext? cx = null;
        try
        {
            try { cx = new VulkanContext(o.AdapterIndex); }
            catch (VulkanException e) { return new(GpuRunStatus.Unavailable, "", 0, 0, null, e.Message); }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException)
            { return new(GpuRunStatus.Unavailable, "", 0, 0, null, "A biblioteca Vulkan (libvulkan.so.1) não foi encontrada."); }
            return Render(cx, o, ct, progress);
        }
        catch (VulkanException e) { return new(GpuRunStatus.Failed, cx?.AdapterName ?? "", Frames, 0, null, e.Message); }
        finally { cx?.Dispose(); }
    }

    private GpuBurnResult Render(VulkanContext cx, GpuBurnOptions o, CancellationToken ct, IProgress<GpuBurnProgress>? progress)
    {
        var vk = cx.Api; var dev = cx.Device;
        var samples = o.Msaa switch { 8 => SampleCountFlags.Count8Bit, 4 => SampleCountFlags.Count4Bit, 2 => SampleCountFlags.Count2Bit, _ => SampleCountFlags.Count1Bit };
        var color = cx.CreateImage((uint)o.Width, (uint)o.Height, Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit, samples, ImageAspectFlags.ColorBit);
        var depth = cx.CreateImage((uint)o.Width, (uint)o.Height, Format.D32Sfloat, ImageUsageFlags.DepthStencilAttachmentBit, samples, ImageAspectFlags.DepthBit);
        ShaderModule vert = default, frag = default; PipelineLayout layout = default; Pipeline pipeline = default;
        var fences = new Fence[InFlight]; var buffers = new CommandBuffer[InFlight]; bool fencesCreated = false;
        try
        {
            vert = cx.LoadShader("burn.vert.spv"); frag = cx.LoadShader("burn.frag.spv");
            var range = new PushConstantRange(ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(Push));
            var lci = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, PushConstantRangeCount = 1, PPushConstantRanges = &range };
            Check(vk.CreatePipelineLayout(dev, in lci, null, out layout), "layout");
            pipeline = CreatePipeline(cx, vert, frag, layout, samples);
            var fci = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
            for (int i = 0; i < InFlight; i++) Check(vk.CreateFence(dev, in fci, null, out fences[i]), "fence");
            fencesCreated = true;
            var ai = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = cx.Pool, Level = CommandBufferLevel.Primary, CommandBufferCount = InFlight };
            fixed (CommandBuffer* b = buffers) Check(vk.AllocateCommandBuffers(dev, in ai, b), "command buffers");

            var view = Matrix4x4.CreateLookAt(new Vector3(0, 1.6f, 3.4f), Vector3.Zero, Vector3.UnitY);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.2f, o.Width / (float)o.Height, 0.1f, 20f); proj.M22 *= -1; // Vulkan clip space has Y down
            var clock = Stopwatch.StartNew(); double nextReport = 0; int slot = 0; long done = 0;
            while (clock.Elapsed < o.EffectiveDuration)
            {
                if (ct.IsCancellationRequested) return Finish(cx, GpuRunStatus.Cancelled, done, clock, "Interrompido: nenhum valor é publicado.");
                var fence = fences[slot];
                var wait = vk.WaitForFences(dev, 1, in fence, true, FenceTimeoutNs);
                if (wait == Result.Timeout) return Finish(cx, GpuRunStatus.Failed, done, clock, "A GPU não respondeu em 5 s (pode ter travado). O teste foi abortado.");
                Check(wait, "Esperar a GPU");
                vk.ResetFences(dev, 1, in fence);
                var cmd = buffers[slot];
                Record(cx, cmd, o, color, depth, layout, pipeline, new Push { ViewProj = view * proj, Time = (float)clock.Elapsed.TotalSeconds, Shells = o.Shells, Iterations = o.Iterations, Aspect = o.Width / (float)o.Height });
                var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
                var sr = vk.QueueSubmit(cx.Queue, 1, in submit, fence);
                if (sr is Result.ErrorDeviceLost) return Finish(cx, GpuRunStatus.Failed, done, clock, "A GPU foi reiniciada pelo driver (DEVICE_LOST): carga alta demais para esta GPU. Reduza camadas/iterações/resolução.");
                Check(sr, "Enviar comandos");
                done++; Interlocked.Exchange(ref frames, done); slot = (slot + 1) % InFlight;
                if (clock.Elapsed.TotalSeconds >= nextReport) { nextReport = clock.Elapsed.TotalSeconds + 0.5; progress?.Report(new(clock.Elapsed.TotalSeconds, done, done / Math.Max(0.001, clock.Elapsed.TotalSeconds))); }
            }
            vk.DeviceWaitIdle(dev);
            return Finish(cx, GpuRunStatus.Completed, done, clock, null);
        }
        finally
        {
            vk.DeviceWaitIdle(dev);
            if (fencesCreated) foreach (var f in fences) vk.DestroyFence(dev, f, null);
            if (pipeline.Handle != 0) vk.DestroyPipeline(dev, pipeline, null);
            if (layout.Handle != 0) vk.DestroyPipelineLayout(dev, layout, null);
            if (vert.Handle != 0) vk.DestroyShaderModule(dev, vert, null);
            if (frag.Handle != 0) vk.DestroyShaderModule(dev, frag, null);
            Free(vk, dev, color); Free(vk, dev, depth);
        }
    }

    private static void Free(Vk vk, Device dev, (Image Image, DeviceMemory Memory, ImageView View) i) { vk.DestroyImageView(dev, i.View, null); vk.DestroyImage(dev, i.Image, null); vk.FreeMemory(dev, i.Memory, null); }

    private static GpuBurnResult Finish(VulkanContext cx, GpuRunStatus status, long frames, Stopwatch clock, string? reason)
    {
        double s = clock.Elapsed.TotalSeconds;
        return new(status, cx.AdapterName, frames, s, status == GpuRunStatus.Completed && s > 0 ? frames / s : null, reason);
    }

    private static void Check(Result r, string what) { if (r != Result.Success) throw new VulkanException($"{what} falhou ({r}).", r); }

    private static Pipeline CreatePipeline(VulkanContext cx, ShaderModule vert, ShaderModule frag, PipelineLayout layout, SampleCountFlags samples)
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
            var rs = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1 };
            var ms = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = samples };
            var ds = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = true, DepthWriteEnable = true, DepthCompareOp = CompareOp.Less };
            var attach = new PipelineColorBlendAttachmentState { ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit };
            var cb = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &attach };
            var dyn = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dyni = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dyn };
            var colorFormat = Format.R8G8B8A8Unorm;
            var rendering = new PipelineRenderingCreateInfo { SType = StructureType.PipelineRenderingCreateInfo, ColorAttachmentCount = 1, PColorAttachmentFormats = &colorFormat, DepthAttachmentFormat = Format.D32Sfloat };
            var gci = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo, PNext = &rendering, StageCount = 2, PStages = stages, PVertexInputState = &vi, PInputAssemblyState = &ia, PViewportState = &vp,
                PRasterizationState = &rs, PMultisampleState = &ms, PDepthStencilState = &ds, PColorBlendState = &cb, PDynamicState = &dyni, Layout = layout
            };
            Check(vk.CreateGraphicsPipelines(cx.Device, default, 1, in gci, null, out var pipeline), "Criar o pipeline gráfico");
            return pipeline;
        }
        finally { Marshal.FreeHGlobal((nint)name); }
    }

    private static void Record(VulkanContext cx, CommandBuffer cmd, GpuBurnOptions o, (Image Image, DeviceMemory Memory, ImageView View) color, (Image Image, DeviceMemory Memory, ImageView View) depth, PipelineLayout layout, Pipeline pipeline, Push push)
    {
        var vk = cx.Api;
        vk.ResetCommandBuffer(cmd, 0);
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        Check(vk.BeginCommandBuffer(cmd, in begin), "Iniciar o buffer de comandos");
        void Barrier(Image image, ImageAspectFlags aspect, ImageLayout to, AccessFlags access, PipelineStageFlags stage)
        {
            var b = new ImageMemoryBarrier { SType = StructureType.ImageMemoryBarrier, OldLayout = ImageLayout.Undefined, NewLayout = to, DstAccessMask = access, Image = image, SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1) };
            vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, stage, 0, 0, null, 0, null, 1, in b);
        }
        Barrier(color.Image, ImageAspectFlags.ColorBit, ImageLayout.ColorAttachmentOptimal, AccessFlags.ColorAttachmentWriteBit, PipelineStageFlags.ColorAttachmentOutputBit);
        Barrier(depth.Image, ImageAspectFlags.DepthBit, ImageLayout.DepthAttachmentOptimal, AccessFlags.DepthStencilAttachmentWriteBit, PipelineStageFlags.EarlyFragmentTestsBit);
        var ca = new RenderingAttachmentInfo { SType = StructureType.RenderingAttachmentInfo, ImageView = color.View, ImageLayout = ImageLayout.ColorAttachmentOptimal, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new ClearValue { Color = new ClearColorValue(0, 0, 0, 1) } };
        var da = new RenderingAttachmentInfo { SType = StructureType.RenderingAttachmentInfo, ImageView = depth.View, ImageLayout = ImageLayout.DepthAttachmentOptimal, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.DontCare, ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1, 0) } };
        var ri = new RenderingInfo { SType = StructureType.RenderingInfo, RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)o.Width, (uint)o.Height)), LayerCount = 1, ColorAttachmentCount = 1, PColorAttachments = &ca, PDepthAttachment = &da };
        vk.CmdBeginRendering(cmd, in ri);
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);
        var viewport = new Viewport(0, 0, o.Width, o.Height, 0, 1); var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)o.Width, (uint)o.Height));
        vk.CmdSetViewport(cmd, 0, 1, in viewport); vk.CmdSetScissor(cmd, 0, 1, in scissor);
        vk.CmdPushConstants(cmd, layout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(Push), &push);
        vk.CmdDraw(cmd, 192 * 96 * 6, (uint)o.Shells, 0, 0);
        vk.CmdEndRendering(cmd);
        Check(vk.EndCommandBuffer(cmd), "Finalizar o buffer de comandos");
    }
}
