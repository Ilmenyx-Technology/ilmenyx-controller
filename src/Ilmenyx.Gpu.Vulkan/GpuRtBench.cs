// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Adapted from RESET EXPLODE GpuRtBench.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)): same procedural scene, path tracer, measurement and score,
// ported from Direct3D 12 / DXR 1.1 to Vulkan (VK_KHR_acceleration_structure + VK_KHR_ray_query in a compute shader), offscreen without a window. See NOTICE.md.
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Ilmenyx.Core;

namespace Ilmenyx.Gpu.Vulkan;

/// <summary>One measured pass of the ray tracing test.</summary>
public sealed record RtPass(double AvgFps, double Low1Fps, int Frames);

/// <summary>Presets and resolutions of the original RESET BENCH ray tracing test.</summary>
public static class GpuRtPresets
{
    public static readonly string[] Presets = ["normal", "alta", "extrema"];
    public static readonly string[] Resolutions = ["1080p", "1440p", "4k"];

    /// <summary>Samples per pixel and bounces (same values as the original).</summary>
    public static (uint Spp, uint Bounces)? Quality(string preset) => preset switch
    {
        "normal" => (2u, 2u),
        "alta" => (4u, 3u),
        "extrema" => (8u, 4u),
        _ => null,
    };

    public static (int Width, int Height)? Size(string resolution) => resolution.ToLowerInvariant() switch
    {
        "1080p" => (1920, 1080),
        "1440p" => (2560, 1440),
        "4k" or "2160p" => (3840, 2160),
        _ => null,
    };

    /// <summary>Rays per pixel: 1 visibility/bounce + 1 shadow + 1 occlusion per bounce, per sample (the original's estimate).</summary>
    public static double RaysPerPixel(string preset) => Quality(preset) is { } q ? q.Spp * q.Bounces * 3.0 : 0;

    public static string Label(string preset) => preset switch { "normal" => "Normal", "extrema" => L.T("Extrema"), _ => L.T("Alta") };
}

public sealed record GpuRtOptions(int AdapterIndex = 0, string Preset = "alta", string Resolution = "1080p", TimeSpan? Warmup = null, TimeSpan? PassDuration = null, int Passes = 3, bool AllowGpuLoad = false)
{
    public const int MaxWarmupSeconds = 60, MaxPassSeconds = 60, MaxPasses = 5;
    public TimeSpan EffectiveWarmup => Warmup ?? TimeSpan.FromSeconds(10);
    public TimeSpan EffectivePass => PassDuration ?? TimeSpan.FromSeconds(30);
    public int Width => GpuRtPresets.Size(Resolution)?.Width ?? 0;
    public int Height => GpuRtPresets.Size(Resolution)?.Height ?? 0;
    public TimeSpan TotalDuration => EffectiveWarmup + EffectivePass * Passes;

    public static string? Validate(GpuRtOptions o) =>
        !o.AllowGpuLoad ? L.T("O benchmark de ray tracing exige consentimento explícito (gera carga máxima na GPU).")
        : o.AdapterIndex < 0 ? L.T("Adaptador inválido.")
        : GpuRtPresets.Quality(o.Preset) is null ? L.T("Preset: normal, alta ou extrema.")
        : GpuRtPresets.Size(o.Resolution) is null ? L.T("Resolução: 1080p, 1440p ou 4k.")
        : o.EffectiveWarmup < TimeSpan.Zero || o.EffectiveWarmup > TimeSpan.FromSeconds(MaxWarmupSeconds) ? L.T($"Aquecimento de 0 a {MaxWarmupSeconds} s.")
        : o.EffectivePass < TimeSpan.FromSeconds(1) || o.EffectivePass > TimeSpan.FromSeconds(MaxPassSeconds) ? L.T($"Passada de 1 a {MaxPassSeconds} s.")
        : o.Passes is < 1 or > MaxPasses ? L.T($"De 1 a {MaxPasses} passadas.")
        : null;
}

public sealed record GpuRtProgress(string Phase, double Fraction, double Fps);

/// <summary>
/// Ray tracing result: score from the MEDIAN of the passes (resists one bad pass) and the spread between them, which says whether the
/// result can be compared. Same formulas as the original.
/// </summary>
public sealed record GpuRtResult(GpuRunStatus Status, string Adapter, string Preset, int Width, int Height, IReadOnlyList<RtPass> Passes, double GigaRaysPerSec, bool GpuTimestamps, string? Reason)
{
    public static readonly string Comparability = L.T("Experimental: port Vulkan (ray query) do teste DXR 1.1 do original; não comparável a scores do Windows nem entre drivers sem validação.");
    public double MedianFps => Median(Passes.Select(p => p.AvgFps));
    public double MedianLow1 => Median(Passes.Select(p => p.Low1Fps));
    /// <summary>(highest − lowest) ÷ median, in %.</summary>
    public double VariationPct => Passes.Count < 2 || MedianFps <= 0 ? 0 : (Passes.Max(p => p.AvgFps) - Passes.Min(p => p.AvgFps)) / MedianFps * 100;
    public bool Reliable => Status == GpuRunStatus.Completed && Passes.Count >= 3 && VariationPct < 3;
    /// <summary>Score = median FPS × 10 (same scale as the game scene).</summary>
    public double Score => MedianFps * 10;

    public static double Median(IEnumerable<double> v)
    {
        var s = v.OrderBy(x => x).ToArray();
        return s.Length == 0 ? 0 : s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }

    /// <summary>One pass from its frame times in ms: average FPS over the summed frame time and "1% low" = 1000 / 99th percentile frame time (as the original).</summary>
    public static RtPass PassFrom(IReadOnlyList<double> frameMs)
    {
        var sorted = frameMs.OrderBy(x => x).ToArray();
        double p99 = sorted.Length > 0 ? sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))] : 0;
        double sum = frameMs.Sum() / 1000;
        return new RtPass(sum > 0 ? frameMs.Count / sum : 0, p99 > 0 ? 1000 / p99 : 0, frameMs.Count);
    }

    public static GpuRtResult Fail(GpuRunStatus status, GpuRtOptions o, string adapter, string? reason) => new(status, adapter, o.Preset, o.Width, o.Height, [], 0, false, reason);
}

/// <summary>Which adapter runs the test. Never picks a CPU (llvmpipe) adapter unless its index was requested explicitly.</summary>
public static class GpuRtSupport
{
    public static readonly string MissingHardware = L.T("não tem ray tracing por hardware no Vulkan (faltam VK_KHR_acceleration_structure e VK_KHR_ray_query). ") +
        L.T("Precisa de AMD Radeon RX 6000 (RDNA2) ou mais nova, NVIDIA RTX 20 ou mais nova, ou Intel Arc, com driver Vulkan atualizado.");

    /// <summary>Null when the adapter can run the test, otherwise the reason (readable, in Portuguese).</summary>
    public static string? Missing(VulkanAdapter a) =>
        !a.SupportsVulkan13 ? L.T($"{a.Name} só oferece Vulkan {a.ApiVersion}; o teste precisa de Vulkan 1.3.")
        : !a.SupportsRayQuery ? $"{a.Name} {MissingHardware}"
        : null;

    public static (VulkanAdapter? Adapter, string? Reason) Pick(VulkanDeviceList list, int? requested = null)
    {
        if (list.Error is { } error) return (null, error);
        if (list.Adapters.Count == 0) return (null, L.T("Nenhum adaptador Vulkan encontrado."));
        if (requested is int index)
        {
            var chosen = list.Adapters.FirstOrDefault(a => a.Index == index);
            if (chosen is null) return (null, L.T($"Adaptador {index} inexistente (há {list.Adapters.Count})."));
            return Missing(chosen) is { } why ? (null, why) : (chosen, null);
        }
        var real = list.Adapters.Where(a => !a.IsSoftware).ToList();
        var ok = real.Where(a => Missing(a) is null).OrderBy(a => a.Kind == L.T("GPU dedicada") ? 0 : 1).FirstOrDefault();
        if (ok is not null) return (ok, null);
        if (real.Count == 0) return (null, L.T("Só há renderização por CPU (llvmpipe/software), que não é escolhida automaticamente; para forçá-la, indique o adaptador (--adapter N)."));
        return (null, string.Join(" ", real.Select(a => Missing(a)!)));
    }
}

/// <summary>
/// RESET BENCH · Ray tracing, Vulkan port. Procedural scene with ~4,000 objects (spheres, tori, columns, boxes) in matte, metal and mirror materials plus
/// emissive lights. Per pixel, a path tracer with inline rays (GL_EXT_ray_query in a compute shader): visibility, soft sun shadow, ambient occlusion and
/// reflection/GI bounces. The top-level acceleration structure (TLAS) is rebuilt every frame with spinning objects. Frame time comes from GPU timestamps.
/// Warm-up without measuring, then N identical passes; score from the median, spread between passes. Offscreen (no window), 5 s fence timeout, a probe frame
/// refuses settings whose frames would take too long for the driver's watchdog, and every Vulkan object is freed in finally.
/// </summary>
public sealed unsafe class GpuRtBench
{
    private const int InFlight = 2;
    private const ulong FenceTimeoutNs = 5_000_000_000;
    /// <summary>Estimated full-frame time above which the run is refused (one frame is one GPU submission; drivers reset the GPU after ~10 s).</summary>
    public const double MaxFrameSeconds = 2.0;
    private long frames;
    public long Frames => Interlocked.Read(ref frames);

    // ------------------------------------------------------------------ scene (CPU), same as the original

    [StructLayout(LayoutKind.Sequential)] private struct Vtx { public Vector3 Pos; public Vector3 Nrm; }
    [StructLayout(LayoutKind.Sequential)] private struct MeshInfo { public uint IndexOffset, VertexOffset, Pad0, Pad1; }
    [StructLayout(LayoutKind.Sequential)] private struct InstMat { public Vector3 Albedo; public float Metal; public float Rough; public float Emissive; public uint Mesh; public float Pad; }

    /// <summary>VkAccelerationStructureInstanceKHR (64 bytes): 3x4 matrix, custom index(24)+mask(8), SBT offset(24)+flags(8), BLAS address.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceDesc
    {
        public float M00, M01, M02, M03, M10, M11, M12, M13, M20, M21, M22, M23;
        public uint IdMask, ContribFlags;
        public ulong Blas;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public Matrix4x4 InvViewProj;
        public Vector4 CamPosTime, SunDirFrame;
        public uint Width, Height, Spp, Bounces;
        public uint BandStart, BandRows, Pad0, Pad1;
    }

    private sealed class MeshBuilder
    {
        public readonly List<Vtx> V = new();
        public readonly List<uint> I = new();
        public readonly List<(uint IndexOffset, uint IndexCount, uint VertexOffset, uint VertexCount)> Meshes = new();
        private int vStart, iStart;
        public void Begin() { vStart = V.Count; iStart = I.Count; }
        public void End() => Meshes.Add(((uint)iStart, (uint)(I.Count - iStart), (uint)vStart, (uint)(V.Count - vStart)));
        public uint Add(Vector3 p, Vector3 n) { V.Add(new Vtx { Pos = p, Nrm = Vector3.Normalize(n) }); return (uint)(V.Count - 1 - vStart); }
        public void Tri(uint a, uint b, uint c) { I.Add(a); I.Add(b); I.Add(c); }
    }

    private static void Plane(MeshBuilder b, float size, int n)
    {
        b.Begin();
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                b.Add(new((x / (float)n - 0.5f) * size, 0, (z / (float)n - 0.5f) * size), Vector3.UnitY);
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                uint a = (uint)(z * (n + 1) + x), c = a + (uint)(n + 1);
                b.Tri(a, c, a + 1); b.Tri(a + 1, c, c + 1);
            }
        b.End();
    }

    private static void Sphere(MeshBuilder b, int seg)
    {
        b.Begin();
        int rings = seg / 2;
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= seg; s++)
            {
                float th = MathF.PI * r / rings, ph = 2 * MathF.PI * s / seg;
                var p = new Vector3(MathF.Sin(th) * MathF.Cos(ph), MathF.Cos(th), MathF.Sin(th) * MathF.Sin(ph));
                b.Add(p, p);
            }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                uint a = (uint)(r * (seg + 1) + s), c = a + (uint)(seg + 1);
                b.Tri(a, a + 1, c); b.Tri(a + 1, c + 1, c);
            }
        b.End();
    }

    private static void Torus(MeshBuilder b, int seg, int side, float bigR, float r)
    {
        b.Begin();
        for (int i = 0; i <= seg; i++)
            for (int j = 0; j <= side; j++)
            {
                float u = 2 * MathF.PI * i / seg, v = 2 * MathF.PI * j / side;
                var c = new Vector3(MathF.Cos(u) * bigR, 0, MathF.Sin(u) * bigR);
                var n = new Vector3(MathF.Cos(u) * MathF.Cos(v), MathF.Sin(v), MathF.Sin(u) * MathF.Cos(v));
                b.Add(c + n * r, n);
            }
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < side; j++)
            {
                uint a = (uint)(i * (side + 1) + j), c = a + (uint)(side + 1);
                b.Tri(a, c, a + 1); b.Tri(a + 1, c, c + 1);
            }
        b.End();
    }

    private static void Column(MeshBuilder b, int seg)
    {
        b.Begin();
        for (int i = 0; i <= seg; i++)
        {
            float a = 2 * MathF.PI * i / seg;
            // Flutes: the radius ripples like a Greek column (more triangles and varied normals).
            float rr = 1 + 0.06f * MathF.Cos(a * 12);
            var n = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            b.Add(new Vector3(MathF.Cos(a) * rr, 0, MathF.Sin(a) * rr), n);
            b.Add(new Vector3(MathF.Cos(a) * rr, 1, MathF.Sin(a) * rr), n);
        }
        for (int i = 0; i < seg; i++) { uint a = (uint)(i * 2); b.Tri(a, a + 1, a + 2); b.Tri(a + 1, a + 3, a + 2); }
        uint top = b.Add(new Vector3(0, 1, 0), Vector3.UnitY);
        for (int i = 0; i < seg; i++)
        {
            float a0 = 2 * MathF.PI * i / seg, a1 = 2 * MathF.PI * (i + 1) / seg;
            uint p0 = b.Add(new Vector3(MathF.Cos(a0), 1, MathF.Sin(a0)), Vector3.UnitY);
            uint p1 = b.Add(new Vector3(MathF.Cos(a1), 1, MathF.Sin(a1)), Vector3.UnitY);
            b.Tri(top, p1, p0);
        }
        b.End();
    }

    private static void Box(MeshBuilder b)
    {
        b.Begin();
        Vector3[] n = { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        foreach (var f in n)
        {
            var u = MathF.Abs(f.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var v = Vector3.Cross(f, u);
            uint a = b.Add(f - u - v, f), c = b.Add(f + u - v, f), d = b.Add(f + u + v, f), e = b.Add(f - u + v, f);
            b.Tri(a, c, d); b.Tri(a, d, e);
        }
        b.End();
    }

    private const int MeshGround = 0, MeshSphere = 1, MeshHeroSphere = 2, MeshTorus = 3, MeshColumn = 4, MeshBox = 5;
    public const int ObjectCount = 4000;
    private const float World = 220;

    private sealed record Obj(int Mesh, Vector3 Pos, float Scale, Vector3 Scale3, float Spin, Vector3 Axis, InstMat Mat);

    private static MeshBuilder BuildMeshes()
    {
        var mb = new MeshBuilder();
        Plane(mb, World + 60, 96);
        Sphere(mb, 32);
        Sphere(mb, 160);            // big sphere, ~25k triangles
        Torus(mb, 48, 24, 1, 0.35f);
        Column(mb, 48);
        Box(mb);
        return mb;
    }

    /// <summary>Objects scattered with a fixed seed: the same scene on any PC.</summary>
    private static List<Obj> BuildObjects()
    {
        var rnd = new Random(20261015);
        float R() => (float)rnd.NextDouble();
        var list = new List<Obj>
        {
            new(MeshGround, Vector3.Zero, 1, Vector3.One, 0, Vector3.UnitY,
                new InstMat { Albedo = new(0.55f, 0.55f, 0.58f), Metal = 0.15f, Rough = 0.35f, Mesh = MeshGround }),
        };
        // Big mirror and gold spheres in the centre (reflections of reflections).
        for (int i = 0; i < 7; i++)
        {
            float a = i / 7f * MathF.Tau;
            var pos = new Vector3(MathF.Cos(a) * 16, 6, MathF.Sin(a) * 16);
            bool gold = i % 2 == 1;
            list.Add(new(MeshHeroSphere, pos, 6, new Vector3(6), 0, Vector3.UnitY, new InstMat
            {
                Albedo = gold ? new(1.0f, 0.78f, 0.34f) : new(0.95f, 0.95f, 0.97f), Metal = 1, Rough = gold ? 0.12f : 0.0f, Mesh = MeshHeroSphere,
            }));
        }
        while (list.Count < ObjectCount)
        {
            var pos = new Vector3((R() - 0.5f) * World, 0, (R() - 0.5f) * World);
            if (new Vector2(pos.X, pos.Z).Length() < 26) continue; // keeps the centre free for the big spheres
            int mesh = R() switch { < 0.35f => MeshSphere, < 0.55f => MeshTorus, < 0.75f => MeshColumn, _ => MeshBox };
            float s = 0.6f + R() * 1.8f;
            Vector3 s3 = mesh == MeshColumn ? new(s * 0.6f, s * (2 + R() * 3), s * 0.6f) : new(s);
            pos.Y = mesh switch { MeshSphere => s, MeshTorus => s * 0.35f + 0.6f, MeshBox => s, _ => 0 };
            float roll = R();
            var mat = new InstMat
            {
                Albedo = Palette(R(), R()),
                Metal = roll < 0.3f ? 1 : 0, Rough = roll < 0.3f ? R() * 0.35f : 0.6f + R() * 0.4f,
                Emissive = roll > 0.97f ? 6 + R() * 10 : 0, Mesh = (uint)mesh,
            };
            float spin = mesh is MeshTorus or MeshBox && R() < 0.5f ? 0.3f + R() * 1.2f : 0;
            var axis = Vector3.Normalize(new Vector3(R() - 0.5f, 1, R() - 0.5f));
            list.Add(new(mesh, pos, s, s3, spin, axis, mat));
        }
        return list;
    }

    /// <summary>Saturated colours from a fixed palette, with varied brightness.</summary>
    private static Vector3 Palette(float pick, float shade)
    {
        Vector3[] p = { new(0.85f, 0.18f, 0.12f), new(0.95f, 0.55f, 0.08f), new(0.12f, 0.45f, 0.85f), new(0.1f, 0.65f, 0.4f),
                        new(0.9f, 0.85f, 0.75f), new(0.45f, 0.2f, 0.7f), new(0.08f, 0.08f, 0.1f), new(0.75f, 0.75f, 0.78f) };
        return p[(int)(pick * p.Length) % p.Length] * (0.55f + shade * 0.45f);
    }

    private static Matrix4x4 ObjMatrix(Obj o, float t) =>
        Matrix4x4.CreateScale(o.Scale3) *
        (o.Spin != 0 ? Matrix4x4.CreateFromAxisAngle(o.Axis, t * o.Spin) : Matrix4x4.Identity) *
        Matrix4x4.CreateTranslation(o.Pos);

    /// <summary>Camera on a fixed path over the pass time (0..1): the same image sequence on any GPU.</summary>
    public static (Vector3 Eye, Vector3 Target) CameraAt(float t)
    {
        float a = t * MathF.Tau * 0.75f + 0.4f;
        float r = 40 + 26 * MathF.Sin(t * MathF.PI);
        var eye = new Vector3(MathF.Cos(a) * r, 12 + 10 * (0.5f + 0.5f * MathF.Sin(t * MathF.Tau * 1.5f)), MathF.Sin(a) * r);
        var target = new Vector3(MathF.Cos(a + 0.6f) * 6, 3, MathF.Sin(a + 0.6f) * 6);
        return (eye, target);
    }

    /// <summary>Scene facts (CPU only, no GPU): objects, triangles, per-mesh counts, metals and emissive lights.</summary>
    public static (int Objects, long Triangles, int Spheres, int Tori, int Columns, int Boxes, int Metal, int Emissive) SceneStats()
    {
        var mb = BuildMeshes(); var objects = BuildObjects();
        long tris = objects.Sum(o => (long)mb.Meshes[o.Mesh].IndexCount / 3);
        return (objects.Count, tris, objects.Count(o => o.Mesh is MeshSphere or MeshHeroSphere), objects.Count(o => o.Mesh == MeshTorus), objects.Count(o => o.Mesh == MeshColumn),
            objects.Count(o => o.Mesh == MeshBox), objects.Count(o => o.Mat.Metal >= 1), objects.Count(o => o.Mat.Emissive > 0));
    }

    // ------------------------------------------------------------------ entry points

    public Task<GpuRtResult> RunAsync(GpuRtOptions options, CancellationToken ct, IProgress<GpuRtProgress>? progress = null)
    {
        if (GpuRtOptions.Validate(options) is { } why) return Task.FromResult(GpuRtResult.Fail(GpuRunStatus.Unavailable, options, "", why));
        return Start(() => Execute(options, ct, progress, null, 0));
    }

    /// <summary>Renders one frame at time fraction <paramref name="t"/> and writes it as a new PNG (explicit request; one frame of GPU work).</summary>
    public Task<GpuRtResult> SnapshotAsync(GpuRtOptions options, double t, string pngPath, CancellationToken ct)
    {
        if (GpuRtOptions.Validate(options) is { } why) return Task.FromResult(GpuRtResult.Fail(GpuRunStatus.Unavailable, options, "", why));
        if (string.IsNullOrWhiteSpace(pngPath) || File.Exists(pngPath)) return Task.FromResult(GpuRtResult.Fail(GpuRunStatus.Unavailable, options, "", L.T("Escolha um arquivo PNG novo (nunca sobrescreve).")));
        return Start(() => Execute(options, ct, null, pngPath, Math.Clamp(t, 0, 0.9999)));
    }

    private static Task<GpuRtResult> Start(Func<GpuRtResult> work)
    {
        var tcs = new TaskCompletionSource<GpuRtResult>();
        new Thread(() => { try { tcs.SetResult(work()); } catch (Exception e) { tcs.SetException(e); } }) { IsBackground = true, Name = "GPU ray tracing" }.Start();
        return tcs.Task;
    }

    private GpuRtResult Execute(GpuRtOptions o, CancellationToken ct, IProgress<GpuRtProgress>? progress, string? snapshotPath, double snapshotT)
    {
        var devices = VulkanDevices.List();
        var (adapter, why) = GpuRtSupport.Pick(devices, o.AdapterIndex);
        if (adapter is null) return GpuRtResult.Fail(GpuRunStatus.Unavailable, o, "", why);
        VulkanContext? cx = null;
        try
        {
            try { cx = new VulkanContext(o.AdapterIndex, rayQuery: true); }
            catch (VulkanException e) { return GpuRtResult.Fail(GpuRunStatus.Unavailable, o, adapter.Name, e.Message); }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException) { return GpuRtResult.Fail(GpuRunStatus.Unavailable, o, "", L.T("A biblioteca Vulkan (libvulkan.so.1) não foi encontrada.")); }
            return Render(cx, o, ct, progress, snapshotPath, snapshotT);
        }
        catch (VulkanException e) { return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx?.AdapterName ?? adapter.Name, e.Message); }
        finally { cx?.Dispose(); }
    }

    private static void Check(Result r, string what) { if (r != Result.Success) throw new VulkanException(L.T($"{what} falhou ({r})."), r); }

    /// <summary>VK_KHR_acceleration_structure entry points, loaded from the device (Silk.NET.Vulkan has the structs; the extension class lives in another package).</summary>
    private sealed class AccelFns
    {
        public delegate* unmanaged<Device, AccelerationStructureCreateInfoKHR*, AllocationCallbacks*, AccelerationStructureKHR*, Result> Create;
        public delegate* unmanaged<Device, AccelerationStructureKHR, AllocationCallbacks*, void> Destroy;
        public delegate* unmanaged<Device, AccelerationStructureBuildTypeKHR, AccelerationStructureBuildGeometryInfoKHR*, uint*, AccelerationStructureBuildSizesInfoKHR*, void> Sizes;
        public delegate* unmanaged<CommandBuffer, uint, AccelerationStructureBuildGeometryInfoKHR*, AccelerationStructureBuildRangeInfoKHR**, void> Build;
        public delegate* unmanaged<Device, AccelerationStructureDeviceAddressInfoKHR*, ulong> Address;

        public AccelFns(VulkanContext cx)
        {
            void* Load(string name)
            {
                var f = cx.Api.GetDeviceProcAddr(cx.Device, name);
                void* p = (void*)f.Handle;
                return p != null ? p : throw new VulkanException(L.T($"O driver não exporta {name}."));
            }
            Create = (delegate* unmanaged<Device, AccelerationStructureCreateInfoKHR*, AllocationCallbacks*, AccelerationStructureKHR*, Result>)Load("vkCreateAccelerationStructureKHR");
            Destroy = (delegate* unmanaged<Device, AccelerationStructureKHR, AllocationCallbacks*, void>)Load("vkDestroyAccelerationStructureKHR");
            Sizes = (delegate* unmanaged<Device, AccelerationStructureBuildTypeKHR, AccelerationStructureBuildGeometryInfoKHR*, uint*, AccelerationStructureBuildSizesInfoKHR*, void>)Load("vkGetAccelerationStructureBuildSizesKHR");
            Build = (delegate* unmanaged<CommandBuffer, uint, AccelerationStructureBuildGeometryInfoKHR*, AccelerationStructureBuildRangeInfoKHR**, void>)Load("vkCmdBuildAccelerationStructuresKHR");
            Address = (delegate* unmanaged<Device, AccelerationStructureDeviceAddressInfoKHR*, ulong>)Load("vkGetAccelerationStructureDeviceAddressKHR");
        }
    }

    private GpuRtResult Render(VulkanContext cx, GpuRtOptions o, CancellationToken ct, IProgress<GpuRtProgress>? progress, string? snapshotPath, double snapshotT)
    {
        var vk = cx.Api; var dev = cx.Device;
        var cleanup = new List<Action>();
        int w = o.Width, h = o.Height;
        var (spp, bounces) = GpuRtPresets.Quality(o.Preset)!.Value;
        bool gpuTimestamps = cx.TimestampValidBits > 0 && cx.TimestampPeriod > 0;
        ulong tsMask = cx.TimestampValidBits >= 64 ? ulong.MaxValue : (1UL << (int)cx.TimestampValidBits) - 1;
        try
        {
            var fns = new AccelFns(cx);
            var asProps = new PhysicalDeviceAccelerationStructurePropertiesKHR { SType = StructureType.PhysicalDeviceAccelerationStructurePropertiesKhr };
            var props2 = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &asProps };
            vk.GetPhysicalDeviceProperties2(cx.Physical, &props2);
            ulong scratchAlign = Math.Max(1u, asProps.MinAccelerationStructureScratchOffsetAlignment);

            (Buffer Buffer, DeviceMemory Memory) Buf(ulong size, BufferUsageFlags usage, MemoryPropertyFlags flags)
            {
                var b = cx.CreateBuffer(Math.Max(size, 16), usage, flags);
                cleanup.Add(() => { vk.DestroyBuffer(dev, b.Buffer, null); vk.FreeMemory(dev, b.Memory, null); });
                return b;
            }
            void* Map((Buffer Buffer, DeviceMemory Memory) b, ulong size) { void* p; Check(vk.MapMemory(dev, b.Memory, 0, size, 0, &p), L.T("Mapear memória")); return p; }
            ulong Aligned(ulong address) => (address + scratchAlign - 1) / scratchAlign * scratchAlign;
            const MemoryPropertyFlags Host = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
            const BufferUsageFlags AsInput = BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr | BufferUsageFlags.ShaderDeviceAddressBit;
            const BufferUsageFlags ScratchUsage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit;

            // ---- command buffers, fences, timestamps ----
            var cmds = new CommandBuffer[InFlight + 1];
            var ai = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = cx.Pool, Level = CommandBufferLevel.Primary, CommandBufferCount = (uint)cmds.Length };
            fixed (CommandBuffer* c = cmds) Check(vk.AllocateCommandBuffers(dev, in ai, c), L.T("Alocar buffers de comandos"));
            cleanup.Add(() => { fixed (CommandBuffer* c = cmds) vk.FreeCommandBuffers(dev, cx.Pool, (uint)cmds.Length, c); });
            var fences = new Fence[InFlight + 1];
            var fci = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            for (int i = 0; i < fences.Length; i++) { Check(vk.CreateFence(dev, in fci, null, out fences[i]), L.T("Criar fence")); var f = fences[i]; cleanup.Add(() => vk.DestroyFence(dev, f, null)); }
            var qpi = new QueryPoolCreateInfo { SType = StructureType.QueryPoolCreateInfo, QueryType = QueryType.Timestamp, QueryCount = 2 * (InFlight + 1) };
            Check(vk.CreateQueryPool(dev, in qpi, null, out var queries), L.T("Criar consultas de tempo")); cleanup.Add(() => vk.DestroyQueryPool(dev, queries, null));

            // ---- geometry and materials (uploaded once) ----
            var setup = cmds[InFlight];
            var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
            Check(vk.BeginCommandBuffer(setup, in begin), L.T("Iniciar comandos"));
            Buffer ToGpu<T>(T[] data, BufferUsageFlags usage) where T : unmanaged
            {
                ulong size = (ulong)(data.Length * sizeof(T));
                var staging = Buf(size, BufferUsageFlags.TransferSrcBit, Host);
                void* p = Map(staging, size);
                fixed (T* src = data) System.Buffer.MemoryCopy(src, p, (long)size, (long)size);
                vk.UnmapMemory(dev, staging.Memory);
                var gpu = Buf(size, usage | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit);
                var region = new BufferCopy(0, 0, size);
                vk.CmdCopyBuffer(setup, staging.Buffer, gpu.Buffer, 1, in region);
                return gpu.Buffer;
            }
            var mb = BuildMeshes();
            var objects = BuildObjects();
            var vb = ToGpu(mb.V.ToArray(), BufferUsageFlags.StorageBufferBit | AsInput);
            var ib = ToGpu(mb.I.ToArray(), BufferUsageFlags.StorageBufferBit | AsInput);
            var meshInfo = ToGpu(mb.Meshes.Select(m => new MeshInfo { IndexOffset = m.IndexOffset, VertexOffset = m.VertexOffset }).ToArray(), BufferUsageFlags.StorageBufferBit);
            var mats = ToGpu(objects.Select(x => x.Mat).ToArray(), BufferUsageFlags.StorageBufferBit);
            var copied = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.TransferWriteBit, DstAccessMask = AccessFlags.AccelerationStructureReadBitKhr | AccessFlags.ShaderReadBit };
            vk.CmdPipelineBarrier(setup, PipelineStageFlags.TransferBit, PipelineStageFlags.AccelerationStructureBuildBitKhr | PipelineStageFlags.ComputeShaderBit, 0, 1, in copied, 0, null, 0, null);

            // ---- BLAS: one per mesh, built once ----
            ulong vbAddr = cx.Address(vb), ibAddr = cx.Address(ib);
            int meshCount = mb.Meshes.Count;
            var blasAddr = new ulong[meshCount];
            var geos = new AccelerationStructureGeometryKHR[meshCount];
            var infos = new AccelerationStructureBuildGeometryInfoKHR[meshCount];
            var ranges = new AccelerationStructureBuildRangeInfoKHR[meshCount];
            fixed (AccelerationStructureGeometryKHR* g = geos)
            fixed (AccelerationStructureBuildGeometryInfoKHR* bi = infos)
            fixed (AccelerationStructureBuildRangeInfoKHR* br = ranges)
            {
                var rangePtrs = stackalloc AccelerationStructureBuildRangeInfoKHR*[meshCount];
                for (int m = 0; m < meshCount; m++)
                {
                    var (io, ic, vo, vc) = mb.Meshes[m];
                    g[m] = new AccelerationStructureGeometryKHR
                    {
                        SType = StructureType.AccelerationStructureGeometryKhr, GeometryType = GeometryTypeKHR.TrianglesKhr, Flags = GeometryFlagsKHR.OpaqueBitKhr,
                        Geometry = new AccelerationStructureGeometryDataKHR
                        {
                            Triangles = new AccelerationStructureGeometryTrianglesDataKHR
                            {
                                SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr, VertexFormat = Format.R32G32B32Sfloat,
                                VertexData = new DeviceOrHostAddressConstKHR { DeviceAddress = vbAddr + vo * (ulong)sizeof(Vtx) }, VertexStride = (ulong)sizeof(Vtx), MaxVertex = vc - 1,
                                IndexType = IndexType.Uint32, IndexData = new DeviceOrHostAddressConstKHR { DeviceAddress = ibAddr + io * 4UL },
                            },
                        },
                    };
                    bi[m] = new AccelerationStructureBuildGeometryInfoKHR
                    {
                        SType = StructureType.AccelerationStructureBuildGeometryInfoKhr, Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                        Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr, Mode = BuildAccelerationStructureModeKHR.BuildKhr, GeometryCount = 1, PGeometries = &g[m],
                    };
                    uint prims = ic / 3;
                    var sizes = new AccelerationStructureBuildSizesInfoKHR { SType = StructureType.AccelerationStructureBuildSizesInfoKhr };
                    fns.Sizes(dev, AccelerationStructureBuildTypeKHR.DeviceKhr, &bi[m], &prims, &sizes);
                    var storage = Buf(sizes.AccelerationStructureSize, BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit, MemoryPropertyFlags.DeviceLocalBit);
                    var blas = CreateAs(cx, fns, cleanup, storage.Buffer, sizes.AccelerationStructureSize, AccelerationStructureTypeKHR.BottomLevelKhr);
                    var scratch = Buf(sizes.BuildScratchSize + scratchAlign, ScratchUsage, MemoryPropertyFlags.DeviceLocalBit);
                    bi[m].DstAccelerationStructure = blas;
                    bi[m].ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = Aligned(cx.Address(scratch.Buffer)) };
                    br[m] = new AccelerationStructureBuildRangeInfoKHR { PrimitiveCount = prims };
                    rangePtrs[m] = &br[m];
                    var addrInfo = new AccelerationStructureDeviceAddressInfoKHR { SType = StructureType.AccelerationStructureDeviceAddressInfoKhr, AccelerationStructure = blas };
                    blasAddr[m] = fns.Address(dev, &addrInfo);
                }
                fns.Build(setup, (uint)meshCount, bi, rangePtrs);
            }
            var built = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.AccelerationStructureWriteBitKhr, DstAccessMask = AccessFlags.AccelerationStructureReadBitKhr };
            vk.CmdPipelineBarrier(setup, PipelineStageFlags.AccelerationStructureBuildBitKhr, PipelineStageFlags.AccelerationStructureBuildBitKhr, 0, 1, in built, 0, null, 0, null);

            // ---- TLAS: rebuilt every frame (spinning objects), one instance buffer per frame in flight ----
            int n = objects.Count; ulong instBytes = (ulong)(n * sizeof(InstanceDesc));
            var instMap = new InstanceDesc*[InFlight + 1]; var instAddr = new ulong[InFlight + 1];
            for (int i = 0; i < instMap.Length; i++)
            {
                var b = Buf(instBytes, AsInput, Host);
                instMap[i] = (InstanceDesc*)Map(b, instBytes);
                instAddr[i] = cx.Address(b.Buffer);
            }
            var tlasGeo = new AccelerationStructureGeometryKHR
            {
                SType = StructureType.AccelerationStructureGeometryKhr, GeometryType = GeometryTypeKHR.InstancesKhr,
                Geometry = new AccelerationStructureGeometryDataKHR { Instances = new AccelerationStructureGeometryInstancesDataKHR { SType = StructureType.AccelerationStructureGeometryInstancesDataKhr, ArrayOfPointers = false } },
            };
            var tlasInfo = new AccelerationStructureBuildGeometryInfoKHR
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr, Type = AccelerationStructureTypeKHR.TopLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastBuildBitKhr, Mode = BuildAccelerationStructureModeKHR.BuildKhr, GeometryCount = 1,
            };
            uint instCount = (uint)n;
            var tlasSizes = new AccelerationStructureBuildSizesInfoKHR { SType = StructureType.AccelerationStructureBuildSizesInfoKhr };
            var sizeGeo = tlasGeo; var sizeInfo = tlasInfo; uint sizeCount = instCount; sizeInfo.PGeometries = &sizeGeo;
            fns.Sizes(dev, AccelerationStructureBuildTypeKHR.DeviceKhr, &sizeInfo, &sizeCount, &tlasSizes);
            var tlasStorage = Buf(tlasSizes.AccelerationStructureSize, BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit, MemoryPropertyFlags.DeviceLocalBit);
            var tlas = CreateAs(cx, fns, cleanup, tlasStorage.Buffer, tlasSizes.AccelerationStructureSize, AccelerationStructureTypeKHR.TopLevelKhr);
            var tlasScratch = Buf(tlasSizes.BuildScratchSize + scratchAlign, ScratchUsage, MemoryPropertyFlags.DeviceLocalBit);
            ulong tlasScratchAddr = Aligned(cx.Address(tlasScratch.Buffer));

            // ---- output image (storage, kept in GENERAL) ----
            var output = cx.CreateImage((uint)w, (uint)h, Format.R8G8B8A8Unorm, ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit, SampleCountFlags.Count1Bit, ImageAspectFlags.ColorBit);
            cleanup.Add(() => { vk.DestroyImageView(dev, output.View, null); vk.DestroyImage(dev, output.Image, null); vk.FreeMemory(dev, output.Memory, null); });
            var toGeneral = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier, OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.General, DstAccessMask = AccessFlags.ShaderWriteBit, Image = output.Image,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            vk.CmdPipelineBarrier(setup, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ComputeShaderBit, 0, 0, null, 0, null, 1, in toGeneral);
            Check(vk.EndCommandBuffer(setup), L.T("Finalizar comandos"));
            Submit(cx, setup, fences[InFlight]);
            if (!WaitFence(cx, fences[InFlight])) return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx.AdapterName, L.T("A GPU não respondeu em 5 s ao montar a cena. O teste foi abortado."));

            // ---- descriptors and compute pipeline ----
            var bindings = stackalloc DescriptorSetLayoutBinding[6];
            bindings[0] = new DescriptorSetLayoutBinding { Binding = 0, DescriptorType = DescriptorType.AccelerationStructureKhr, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit };
            for (uint i = 1; i <= 4; i++) bindings[i] = new DescriptorSetLayoutBinding { Binding = i, DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit };
            bindings[5] = new DescriptorSetLayoutBinding { Binding = 5, DescriptorType = DescriptorType.StorageImage, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit };
            var lci = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 6, PBindings = bindings };
            Check(vk.CreateDescriptorSetLayout(dev, in lci, null, out var setLayout), L.T("Criar layout de descritores")); cleanup.Add(() => vk.DestroyDescriptorSetLayout(dev, setLayout, null));
            var poolSizes = stackalloc DescriptorPoolSize[3] { new(DescriptorType.AccelerationStructureKhr, 1), new(DescriptorType.StorageBuffer, 4), new(DescriptorType.StorageImage, 1) };
            var dpci = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 3, PPoolSizes = poolSizes };
            Check(vk.CreateDescriptorPool(dev, in dpci, null, out var descPool), L.T("Criar pool de descritores")); cleanup.Add(() => vk.DestroyDescriptorPool(dev, descPool, null));
            var layoutCopy = setLayout;
            var dsai = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = descPool, DescriptorSetCount = 1, PSetLayouts = &layoutCopy };
            Check(vk.AllocateDescriptorSets(dev, in dsai, out var set), L.T("Alocar descritores"));
            var tlasCopy = tlas;
            var asWrite = new WriteDescriptorSetAccelerationStructureKHR { SType = StructureType.WriteDescriptorSetAccelerationStructureKhr, AccelerationStructureCount = 1, PAccelerationStructures = &tlasCopy };
            var bufInfos = stackalloc DescriptorBufferInfo[4] { new(vb, 0, Vk.WholeSize), new(ib, 0, Vk.WholeSize), new(meshInfo, 0, Vk.WholeSize), new(mats, 0, Vk.WholeSize) };
            var imgInfo = new DescriptorImageInfo(default, output.View, ImageLayout.General);
            var writes = stackalloc WriteDescriptorSet[6];
            writes[0] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, PNext = &asWrite, DstSet = set, DstBinding = 0, DescriptorCount = 1, DescriptorType = DescriptorType.AccelerationStructureKhr };
            for (int i = 0; i < 4; i++) writes[i + 1] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = (uint)(i + 1), DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &bufInfos[i] };
            writes[5] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = 5, DescriptorCount = 1, DescriptorType = DescriptorType.StorageImage, PImageInfo = &imgInfo };
            vk.UpdateDescriptorSets(dev, 6, writes, 0, null);
            var range = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Push));
            var plci = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &layoutCopy, PushConstantRangeCount = 1, PPushConstantRanges = &range };
            Check(vk.CreatePipelineLayout(dev, in plci, null, out var layout), L.T("Criar layout do pipeline")); cleanup.Add(() => vk.DestroyPipelineLayout(dev, layout, null));
            var shader = cx.LoadShader("rt_trace.comp.spv"); cleanup.Add(() => vk.DestroyShaderModule(dev, shader, null));
            var entry = (byte*)Marshal.StringToHGlobalAnsi("main");
            Pipeline pipeline;
            try
            {
                var cpci = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo, Layout = layout,
                    Stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit, Module = shader, PName = entry },
                };
                Check(vk.CreateComputePipelines(dev, default, 1, in cpci, null, out pipeline), L.T("Criar o pipeline de ray tracing"));
            }
            finally { Marshal.FreeHGlobal((nint)entry); }
            cleanup.Add(() => vk.DestroyPipeline(dev, pipeline, null));

            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, w / (float)h, 0.1f, 2000);
            var sunDir = Vector3.Normalize(new Vector3(-0.4f, -0.75f, -0.35f));
            uint frameIndex = 0;

            void WriteInstances(int slot, float secs)
            {
                var dst = instMap[slot];
                for (int i = 0; i < n; i++)
                {
                    var obj = objects[i]; var m = ObjMatrix(obj, secs);
                    dst[i] = new InstanceDesc
                    {
                        M00 = m.M11, M01 = m.M21, M02 = m.M31, M03 = m.M41,
                        M10 = m.M12, M11 = m.M22, M12 = m.M32, M13 = m.M42,
                        M20 = m.M13, M21 = m.M23, M22 = m.M33, M23 = m.M43,
                        IdMask = (uint)i | 0xFF000000u, ContribFlags = 0x05000000u, // TRIANGLE_FACING_CULL_DISABLE | FORCE_OPAQUE
                        Blas = blasAddr[obj.Mesh],
                    };
                }
            }

            void Record(int slot, float t, float secs, uint bandStart, uint bandRows, Buffer? readback)
            {
                var cmd = cmds[slot];
                vk.ResetCommandBuffer(cmd, 0);
                Check(vk.BeginCommandBuffer(cmd, in begin), L.T("Iniciar comandos"));
                vk.CmdResetQueryPool(cmd, queries, (uint)(slot * 2), 2);
                // Full dependency on the previous frame (shared TLAS, scratch and output image); frames serialize on the GPU while the CPU queues ahead.
                var all = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit, DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit };
                vk.CmdPipelineBarrier(cmd, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit, 0, 1, in all, 0, null, 0, null);
                vk.CmdWriteTimestamp(cmd, PipelineStageFlags.TopOfPipeBit, queries, (uint)(slot * 2));
                var geo = tlasGeo; geo.Geometry.Instances.Data = new DeviceOrHostAddressConstKHR { DeviceAddress = instAddr[slot] };
                var info = tlasInfo; info.PGeometries = &geo; info.DstAccelerationStructure = tlas; info.ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = tlasScratchAddr };
                var tr = new AccelerationStructureBuildRangeInfoKHR { PrimitiveCount = instCount }; var trp = &tr;
                fns.Build(cmd, 1, &info, &trp);
                var ready = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.AccelerationStructureWriteBitKhr, DstAccessMask = AccessFlags.AccelerationStructureReadBitKhr };
                vk.CmdPipelineBarrier(cmd, PipelineStageFlags.AccelerationStructureBuildBitKhr, PipelineStageFlags.ComputeShaderBit, 0, 1, in ready, 0, null, 0, null);

                var (eye, target) = CameraAt(t);
                Matrix4x4.Invert(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * proj, out var inv);
                var push = new Push
                {
                    InvViewProj = inv, CamPosTime = new(eye, secs), SunDirFrame = new(sunDir, BitConverter.UInt32BitsToSingle(frameIndex++)),
                    Width = (uint)w, Height = (uint)h, Spp = spp, Bounces = bounces, BandStart = bandStart, BandRows = bandRows,
                };
                vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, pipeline);
                vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, layout, 0, 1, in set, 0, null);
                vk.CmdPushConstants(cmd, layout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Push), &push);
                vk.CmdDispatch(cmd, (uint)((w + 7) / 8), (bandRows + 7) / 8, 1);
                vk.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, queries, (uint)(slot * 2 + 1));
                if (readback is { } rb)
                {
                    var written = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.ShaderWriteBit, DstAccessMask = AccessFlags.TransferReadBit };
                    vk.CmdPipelineBarrier(cmd, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit, 0, 1, in written, 0, null, 0, null);
                    var copy = new BufferImageCopy { ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1), ImageExtent = new Extent3D((uint)w, (uint)h, 1) };
                    vk.CmdCopyImageToBuffer(cmd, output.Image, ImageLayout.General, rb, 1, in copy);
                }
                Check(vk.EndCommandBuffer(cmd), L.T("Finalizar comandos"));
            }

            (ulong Start, ulong End) Timestamps(int slot)
            {
                var data = stackalloc ulong[2];
                var r = vk.GetQueryPoolResults(dev, queries, (uint)(slot * 2), 2, 16, data, 8, QueryResultFlags.Result64Bit);
                return r == Result.Success ? (data[0] & tsMask, data[1] & tsMask) : (0, 0);
            }
            double TicksToMs(ulong ticks) => ticks * (double)cx.TimestampPeriod / 1e6;

            // ---- probe: a band of 1/8 of the rows, extrapolated; refuses settings this GPU cannot render in time ----
            uint probeRows = (uint)Math.Max(8, h / 8);
            WriteInstances(0, 0);
            Record(0, 0, 0, (uint)(h / 2) - probeRows / 2, probeRows, null);
            var probeWatch = Stopwatch.StartNew();
            Submit(cx, cmds[0], fences[0]);
            if (!WaitFence(cx, fences[0])) return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx.AdapterName, L.T("A GPU não respondeu em 5 s ao quadro de teste. O teste foi abortado."));
            double probeMs = probeWatch.Elapsed.TotalMilliseconds;
            if (gpuTimestamps && Timestamps(0) is var (s0, e0) && e0 > s0) probeMs = TicksToMs(e0 - s0);
            double estimated = probeMs / 1000 * h / probeRows;
            if (estimated > MaxFrameSeconds)
                return GpuRtResult.Fail(GpuRunStatus.Unavailable, o, cx.AdapterName, L.T(
                    $"Esta GPU levaria cerca de {estimated:0.0} s por quadro em {GpuRtPresets.Label(o.Preset)} {w}×{h}; o limite é {MaxFrameSeconds:0} s por quadro para não acionar o reinício do driver. Escolha um preset ou resolução menor."));

            if (snapshotPath is not null)
            {
                ulong bytes = (ulong)w * (ulong)h * 4;
                var readback = Buf(bytes, BufferUsageFlags.TransferDstBit, Host);
                float secs = (float)(snapshotT * o.EffectivePass.TotalSeconds);
                WriteInstances(0, secs);
                Record(0, (float)snapshotT, secs, 0, (uint)h, readback.Buffer);
                Check(vk.ResetFences(dev, 1, in fences[0]), L.T("Reiniciar fence"));
                Submit(cx, cmds[0], fences[0]);
                if (!WaitFence(cx, fences[0])) return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx.AdapterName, L.T("A GPU não respondeu em 5 s."));
                void* mapped = Map(readback, bytes);
                byte[] png;
                try { png = PngWriter.Encode(new ReadOnlySpan<byte>(mapped, (int)bytes), w, h); }
                finally { vk.UnmapMemory(dev, readback.Memory); }
                using (var file = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(png);
                return new GpuRtResult(GpuRunStatus.Completed, cx.AdapterName, o.Preset, w, h, [], 0, gpuTimestamps, null);
            }
            Check(vk.ResetFences(dev, 1, in fences[0]), L.T("Reiniciar fence"));

            // ---- warm-up (not measured) + N identical passes ----
            var passes = new List<RtPass>();
            var pending = new bool[InFlight];
            int totalPhases = o.Passes + (o.EffectiveWarmup > TimeSpan.Zero ? 1 : 0);
            long done = 0; double totalSeconds = o.TotalDuration.TotalSeconds, secondsBefore = 0;
            for (int phase = 0; phase < totalPhases; phase++)
            {
                bool warm = o.EffectiveWarmup > TimeSpan.Zero && phase == 0;
                var duration = warm ? o.EffectiveWarmup : o.EffectivePass;
                string label = warm ? L.T("Aquecendo") : L.T($"Passada {phase - (totalPhases - o.Passes) + 1} de {o.Passes}");
                var frameTimes = new List<double>(4096);
                var clock = Stopwatch.StartNew();
                double lastReport = 0, lastCpuEnd = 0; int reportFrames = 0; ulong lastTs = 0; long frame = 0; double currentFps = 0;
                double Fraction(double elapsed) => totalSeconds > 0 ? Math.Min(1, (secondsBefore + Math.Min(elapsed, duration.TotalSeconds)) / totalSeconds) : 0;
                progress?.Report(new(label, Fraction(0), 0));
                while (clock.Elapsed < duration)
                {
                    if (ct.IsCancellationRequested) return GpuRtResult.Fail(GpuRunStatus.Cancelled, o, cx.AdapterName, L.T("Interrompido: nenhum valor é publicado."));
                    int slot = (int)(frame % InFlight);
                    if (pending[slot])
                    {
                        if (!WaitFence(cx, fences[slot])) return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx.AdapterName, L.T("A GPU não respondeu em 5 s (pode ter travado). O teste foi abortado."));
                        pending[slot] = false; Check(vk.ResetFences(dev, 1, in fences[slot]), L.T("Reiniciar fence"));
                        // Time between the ends of two consecutive frames, on the GPU clock (CPU clock at fence completion when the queue has no timestamps).
                        double ms = -1;
                        if (gpuTimestamps) { ulong ts = Timestamps(slot).End; if (lastTs != 0 && ts > lastTs) ms = TicksToMs(ts - lastTs); if (ts != 0) lastTs = ts; }
                        else { double now = clock.Elapsed.TotalMilliseconds; if (lastCpuEnd > 0) ms = now - lastCpuEnd; lastCpuEnd = now; }
                        if (ms >= 0)
                        {
                            if (ms > MaxFrameSeconds * 1500) return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx.AdapterName, L.T($"Um quadro levou {ms / 1000:0.0} s (limite {MaxFrameSeconds * 1.5:0.0} s). O teste foi abortado para não travar a GPU."));
                            frameTimes.Add(ms);
                        }
                        reportFrames++;
                    }
                    // Camera and objects follow the pass time: the same scene at the same instant on any PC.
                    float t = (float)(clock.Elapsed.TotalSeconds / duration.TotalSeconds), secs = (float)clock.Elapsed.TotalSeconds;
                    WriteInstances(slot, secs);
                    Record(slot, t, secs, 0, (uint)h, null);
                    var sres = Submit(cx, cmds[slot], fences[slot], throwOnError: false);
                    if (sres == Result.ErrorDeviceLost) return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx.AdapterName, L.T("A GPU foi reiniciada pelo driver (DEVICE_LOST): carga alta demais para esta GPU. Escolha um preset ou resolução menor."));
                    Check(sres, L.T("Enviar comandos"));
                    pending[slot] = true; frame++; done++; Interlocked.Exchange(ref frames, done);
                    double elapsed = clock.Elapsed.TotalSeconds;
                    if (elapsed - lastReport >= 0.5)
                    {
                        currentFps = reportFrames / (elapsed - lastReport); reportFrames = 0; lastReport = elapsed;
                        progress?.Report(new(label, Fraction(elapsed), currentFps));
                    }
                }
                for (int i = 0; i < InFlight; i++)
                {
                    int slot = (int)((frame + i) % InFlight);
                    if (!pending[slot]) continue;
                    if (!WaitFence(cx, fences[slot])) return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx.AdapterName, L.T("A GPU não respondeu em 5 s (pode ter travado). O teste foi abortado."));
                    pending[slot] = false; Check(vk.ResetFences(dev, 1, in fences[slot]), L.T("Reiniciar fence"));
                }
                secondsBefore += duration.TotalSeconds;
                if (warm) continue;
                if (frameTimes.Count < 2) return GpuRtResult.Fail(GpuRunStatus.Failed, o, cx.AdapterName, L.T("Poucos quadros medidos na passada: a GPU é lenta demais para este preset/resolução nesta duração."));
                passes.Add(GpuRtResult.PassFrom(frameTimes));
            }
            var result = new GpuRtResult(GpuRunStatus.Completed, cx.AdapterName, o.Preset, w, h, passes, 0, gpuTimestamps, null);
            return result with { GigaRaysPerSec = result.MedianFps * w * h * GpuRtPresets.RaysPerPixel(o.Preset) / 1e9 };
        }
        finally
        {
            vk.DeviceWaitIdle(dev);
            for (int i = cleanup.Count - 1; i >= 0; i--) { try { cleanup[i](); } catch (Exception e) when (e is VulkanException or InvalidOperationException) { } }
        }
    }

    private static AccelerationStructureKHR CreateAs(VulkanContext cx, AccelFns fns, List<Action> cleanup, Buffer storage, ulong size, AccelerationStructureTypeKHR type)
    {
        var info = new AccelerationStructureCreateInfoKHR { SType = StructureType.AccelerationStructureCreateInfoKhr, Buffer = storage, Size = size, Type = type };
        AccelerationStructureKHR handle;
        Check(fns.Create(cx.Device, &info, null, &handle), L.T("Criar a estrutura de aceleração"));
        var dev = cx.Device; var created = handle;
        cleanup.Add(() => fns.Destroy(dev, created, null));
        return created;
    }

    private static Result Submit(VulkanContext cx, CommandBuffer cmd, Fence fence, bool throwOnError = true)
    {
        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
        var r = cx.Api.QueueSubmit(cx.Queue, 1, in submit, fence);
        if (throwOnError) Check(r, L.T("Enviar comandos"));
        return r;
    }

    /// <summary>False on the 5 s timeout; throws on any other error.</summary>
    private static bool WaitFence(VulkanContext cx, Fence fence)
    {
        var r = cx.Api.WaitForFences(cx.Device, 1, in fence, true, FenceTimeoutNs);
        if (r == Result.Timeout) return false;
        Check(r, L.T("Esperar a GPU"));
        return true;
    }
}
