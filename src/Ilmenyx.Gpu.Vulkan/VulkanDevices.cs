// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Silk.NET (MIT) provides the Vulkan bindings.
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Ilmenyx.Gpu.Vulkan;

public sealed record VulkanAdapter(int Index, string Name, string Kind, uint VendorId, uint DeviceId, string ApiVersion, ulong DeviceLocalBytes,
    bool IsSoftware, bool SupportsVulkan13, bool SupportsRayTracingPipeline, bool SupportsRayQuery = false)
{
    public string Description => $"{Name} · {Kind} · Vulkan {ApiVersion}" + (IsSoftware ? " · renderização por CPU" : "");
}

public sealed record VulkanDeviceList(IReadOnlyList<VulkanAdapter> Adapters, string? Error);

/// <summary>Lists Vulkan adapters (instance + enumeration only; no device is created, nothing is rendered).</summary>
public static unsafe class VulkanDevices
{
    public static string VersionText(uint v) => $"{v >> 22}.{(v >> 12) & 0x3FF}.{v & 0xFFF}";

    private static bool RayQueryFeatures(Vk vk, PhysicalDevice device)
    {
        var rq = new PhysicalDeviceRayQueryFeaturesKHR { SType = StructureType.PhysicalDeviceRayQueryFeaturesKhr };
        var accel = new PhysicalDeviceAccelerationStructureFeaturesKHR { SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr, PNext = &rq };
        var f12 = new PhysicalDeviceVulkan12Features { SType = StructureType.PhysicalDeviceVulkan12Features, PNext = &accel };
        var features = new PhysicalDeviceFeatures2 { SType = StructureType.PhysicalDeviceFeatures2, PNext = &f12 };
        vk.GetPhysicalDeviceFeatures2(device, &features);
        return rq.RayQuery && accel.AccelerationStructure && f12.BufferDeviceAddress;
    }

    public static VulkanDeviceList List()
    {
        Vk? vk = null; Instance instance = default; bool created = false;
        try
        {
            vk = Vk.GetApi();
            var app = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version13 };
            var info = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &app };
            var result = vk.CreateInstance(in info, null, out instance);
            if (result != Result.Success) return new([], $"Não foi possível criar a instância Vulkan ({result}). Instale o driver Vulkan da sua GPU (mesa-vulkan-* ou nvidia).");
            created = true;
            uint count = 0;
            vk.EnumeratePhysicalDevices(instance, ref count, null);
            var devices = new PhysicalDevice[count];
            fixed (PhysicalDevice* p = devices) vk.EnumeratePhysicalDevices(instance, ref count, p);
            var list = new List<VulkanAdapter>();
            for (int i = 0; i < count; i++)
            {
                vk.GetPhysicalDeviceProperties(devices[i], out var props);
                vk.GetPhysicalDeviceMemoryProperties(devices[i], out var mem);
                ulong local = 0;
                for (int h = 0; h < mem.MemoryHeapCount; h++) if ((mem.MemoryHeaps[h].Flags & MemoryHeapFlags.DeviceLocalBit) != 0) local = Math.Max(local, mem.MemoryHeaps[h].Size);
                uint ext = 0; vk.EnumerateDeviceExtensionProperties(devices[i], (byte*)null, ref ext, null);
                var names = new HashSet<string>(StringComparer.Ordinal);
                if (ext > 0)
                {
                    var props2 = new ExtensionProperties[ext];
                    fixed (ExtensionProperties* ep = props2) vk.EnumerateDeviceExtensionProperties(devices[i], (byte*)null, ref ext, ep);
                    foreach (var e in props2) if (Marshal.PtrToStringUTF8((nint)e.ExtensionName) is { } n) names.Add(n);
                }
                bool rt = names.Contains("VK_KHR_ray_tracing_pipeline");
                // Inline ray tracing (the RT benchmark): extensions present AND the features really enabled by the driver.
                bool rayQuery = props.ApiVersion >= Vk.Version13 && VulkanContext.RayQueryExtensions.All(names.Contains) && RayQueryFeatures(vk, devices[i]);
                string kind = props.DeviceType switch { PhysicalDeviceType.DiscreteGpu => "GPU dedicada", PhysicalDeviceType.IntegratedGpu => "GPU integrada", PhysicalDeviceType.VirtualGpu => "GPU virtual", PhysicalDeviceType.Cpu => "CPU (software)", _ => "outro" };
                list.Add(new(i, Marshal.PtrToStringUTF8((nint)props.DeviceName) ?? "?", kind, props.VendorID, props.DeviceID, VersionText(props.ApiVersion), local,
                    props.DeviceType == PhysicalDeviceType.Cpu, props.ApiVersion >= Vk.Version13, rt, rayQuery));
            }
            return new(list, null);
        }
        catch (Exception e) when (e is DllNotFoundException or FileNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException)
        {
            return new([], "A biblioteca Vulkan (libvulkan.so.1) não foi encontrada. Instale o loader e o driver Vulkan da sua distribuição.");
        }
        finally
        {
            if (created && vk is not null) vk.DestroyInstance(instance, null);
            vk?.Dispose();
        }
    }
}
