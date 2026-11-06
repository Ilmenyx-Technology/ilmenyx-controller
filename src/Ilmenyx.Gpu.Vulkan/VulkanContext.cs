// New Ilmenyx additions: see LICENSING.md. Silk.NET (MIT) provides the Vulkan bindings.
using System.Reflection;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Ilmenyx.Gpu.Vulkan;

public sealed class VulkanException(string message, Result? result = null) : Exception(message)
{
    public Result? Result { get; } = result;
}

/// <summary>One Vulkan 1.3 device with a graphics queue, used for offscreen work only (no window, no swap chain). Disposal waits for the device and frees everything.</summary>
public sealed unsafe class VulkanContext : IDisposable
{
    public Vk Api { get; }
    public Instance Instance { get; private set; }
    public PhysicalDevice Physical { get; private set; }
    public Device Device { get; private set; }
    public Queue Queue { get; private set; }
    public uint QueueFamily { get; private set; }
    public CommandPool Pool { get; private set; }
    public PhysicalDeviceMemoryProperties Memory { get; private set; }
    public string AdapterName { get; private set; } = "";
    public string DeviceType { get; private set; } = "";
    /// <summary>True when the device was created with VK_KHR_acceleration_structure + VK_KHR_ray_query (inline ray tracing in compute shaders).</summary>
    public bool RayQuery { get; }
    /// <summary>Valid bits of the queue's timestamps (0 = no GPU timestamps) and nanoseconds per tick.</summary>
    public uint TimestampValidBits { get; private set; }
    public float TimestampPeriod { get; private set; }
    private bool disposed;

    /// <summary>Extensions enabled when <c>rayQuery</c> is requested (deferred host operations is required by acceleration structures).</summary>
    public static readonly string[] RayQueryExtensions = ["VK_KHR_acceleration_structure", "VK_KHR_ray_query", "VK_KHR_deferred_host_operations"];

    public VulkanContext(int adapterIndex, bool rayQuery = false)
    {
        RayQuery = rayQuery;
        try { Api = Vk.GetApi(); }
        catch (Exception e) when (e is DllNotFoundException or FileNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException) { throw new VulkanException("Biblioteca Vulkan (libvulkan.so.1) não encontrada. Instale o loader Vulkan da sua distribuição (vulkan-icd-loader, libvulkan1 ou vulkan-loader) e o driver da GPU."); }
        try { Create(adapterIndex); }
        catch { Dispose(); throw; }
    }

    private static void Check(Result r, string what) { if (r != Result.Success) throw new VulkanException($"{what} falhou ({r}).", r); }

    private void Create(int adapterIndex)
    {
        var app = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version13 };
        var ici = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &app };
        Check(Api.CreateInstance(in ici, null, out var instance), "Criar a instância Vulkan"); Instance = instance;
        uint count = 0; Api.EnumeratePhysicalDevices(Instance, ref count, null);
        if (adapterIndex < 0 || adapterIndex >= count) throw new VulkanException("Adaptador Vulkan inexistente.");
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices) Api.EnumeratePhysicalDevices(Instance, ref count, p);
        Physical = devices[adapterIndex];
        Api.GetPhysicalDeviceProperties(Physical, out var props);
        if (props.ApiVersion < Vk.Version13) throw new VulkanException($"A GPU só oferece Vulkan {VulkanDevices.VersionText(props.ApiVersion)}; é preciso 1.3 (renderização dinâmica). Atualize o driver.");
        AdapterName = Marshal.PtrToStringUTF8((nint)props.DeviceName) ?? "?"; DeviceType = props.DeviceType.ToString(); TimestampPeriod = props.Limits.TimestampPeriod;
        Api.GetPhysicalDeviceMemoryProperties(Physical, out var mem); Memory = mem;
        uint families = 0; Api.GetPhysicalDeviceQueueFamilyProperties(Physical, ref families, null);
        var qf = new QueueFamilyProperties[families];
        fixed (QueueFamilyProperties* q = qf) Api.GetPhysicalDeviceQueueFamilyProperties(Physical, ref families, q);
        var wanted = RayQuery ? QueueFlags.GraphicsBit | QueueFlags.ComputeBit : QueueFlags.GraphicsBit;
        int family = Array.FindIndex(qf, f => (f.QueueFlags & wanted) == wanted);
        if (family < 0) throw new VulkanException(RayQuery ? "A GPU não oferece fila gráfica com computação." : "A GPU não oferece fila gráfica.");
        QueueFamily = (uint)family; TimestampValidBits = qf[family].TimestampValidBits;
        float priority = 1f;
        var qci = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = QueueFamily, QueueCount = 1, PQueuePriorities = &priority };
        var rq = new PhysicalDeviceRayQueryFeaturesKHR { SType = StructureType.PhysicalDeviceRayQueryFeaturesKhr, RayQuery = true };
        var accel = new PhysicalDeviceAccelerationStructureFeaturesKHR { SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr, PNext = &rq, AccelerationStructure = true };
        var f12 = new PhysicalDeviceVulkan12Features { SType = StructureType.PhysicalDeviceVulkan12Features, PNext = &accel, BufferDeviceAddress = true };
        var f13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features, PNext = RayQuery ? &f12 : null, DynamicRendering = true };
        var names = RayQuery ? SilkMarshal.StringArrayToPtr(RayQueryExtensions) : 0;
        try
        {
            var dci = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo, PNext = &f13, QueueCreateInfoCount = 1, PQueueCreateInfos = &qci,
                EnabledExtensionCount = RayQuery ? (uint)RayQueryExtensions.Length : 0, PpEnabledExtensionNames = (byte**)names
            };
            Check(Api.CreateDevice(Physical, in dci, null, out var device), RayQuery ? "Criar o dispositivo Vulkan com ray tracing (ray query)" : "Criar o dispositivo Vulkan"); Device = device;
        }
        finally { if (names != 0) SilkMarshal.Free(names); }
        Api.GetDeviceQueue(Device, QueueFamily, 0, out var queue); Queue = queue;
        var pci = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = QueueFamily, Flags = CommandPoolCreateFlags.ResetCommandBufferBit };
        Check(Api.CreateCommandPool(Device, in pci, null, out var pool), "Criar o pool de comandos"); Pool = pool;
    }

    public uint FindMemoryType(uint typeBits, MemoryPropertyFlags wanted)
    {
        for (uint i = 0; i < Memory.MemoryTypeCount; i++)
            if ((typeBits & (1u << (int)i)) != 0 && (Memory.MemoryTypes[(int)i].PropertyFlags & wanted) == wanted) return i;
        throw new VulkanException("Nenhum tipo de memória adequado na GPU.");
    }

    public ShaderModule LoadShader(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shaders." + resourceName) ?? throw new VulkanException("Shader embutido ausente: " + resourceName);
        var bytes = new byte[stream.Length]; stream.ReadExactly(bytes);
        if (bytes.Length < 8 || bytes.Length % 4 != 0 || BitConverter.ToUInt32(bytes, 0) != 0x07230203) throw new VulkanException("SPIR-V inválido: " + resourceName);
        fixed (byte* code = bytes)
        {
            var info = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)bytes.Length, PCode = (uint*)code };
            Check(Api.CreateShaderModule(Device, in info, null, out var module), "Criar o shader " + resourceName);
            return module;
        }
    }

    /// <summary>Device-local image with its memory and a view; caller owns and must destroy all three.</summary>
    public (Image Image, DeviceMemory Memory, ImageView View) CreateImage(uint width, uint height, Format format, ImageUsageFlags usage, SampleCountFlags samples, ImageAspectFlags aspect)
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo, ImageType = ImageType.Type2D, Format = format, Extent = new Extent3D(width, height, 1), MipLevels = 1, ArrayLayers = 1,
            Samples = samples, Tiling = ImageTiling.Optimal, Usage = usage, SharingMode = SharingMode.Exclusive, InitialLayout = ImageLayout.Undefined
        };
        Check(Api.CreateImage(Device, in info, null, out var image), "Criar a imagem");
        Api.GetImageMemoryRequirements(Device, image, out var req);
        var alloc = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = req.Size, MemoryTypeIndex = FindMemoryType(req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit) };
        Check(Api.AllocateMemory(Device, in alloc, null, out var memory), "Alocar a memória da imagem");
        Check(Api.BindImageMemory(Device, image, memory, 0), "Associar a memória da imagem");
        var view = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo, Image = image, ViewType = ImageViewType.Type2D, Format = format,
            SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1)
        };
        Check(Api.CreateImageView(Device, in view, null, out var imageView), "Criar a visão da imagem");
        return (image, memory, imageView);
    }

    /// <summary>Buffer with its memory; caller owns and must destroy both. Host-visible buffers are returned unmapped.</summary>
    public (Silk.NET.Vulkan.Buffer Buffer, DeviceMemory Memory) CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
    {
        bool address = (usage & BufferUsageFlags.ShaderDeviceAddressBit) != 0;
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = usage, SharingMode = SharingMode.Exclusive };
        Check(Api.CreateBuffer(Device, in info, null, out var buffer), "Criar o buffer");
        Api.GetBufferMemoryRequirements(Device, buffer, out var req);
        var flags = new MemoryAllocateFlagsInfo { SType = StructureType.MemoryAllocateFlagsInfo, Flags = MemoryAllocateFlags.DeviceAddressBit };
        var alloc = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, PNext = address ? &flags : null, AllocationSize = req.Size, MemoryTypeIndex = FindMemoryType(req.MemoryTypeBits, properties) };
        Check(Api.AllocateMemory(Device, in alloc, null, out var memory), "Alocar a memória do buffer");
        Check(Api.BindBufferMemory(Device, buffer, memory, 0), "Associar a memória do buffer");
        return (buffer, memory);
    }

    /// <summary>GPU address of a buffer created with ShaderDeviceAddressBit (ray query device only).</summary>
    public ulong Address(Silk.NET.Vulkan.Buffer buffer)
    {
        var info = new BufferDeviceAddressInfo { SType = StructureType.BufferDeviceAddressInfo, Buffer = buffer };
        return Api.GetBufferDeviceAddress(Device, in info);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (Device.Handle != 0) { Api.DeviceWaitIdle(Device); if (Pool.Handle != 0) Api.DestroyCommandPool(Device, Pool, null); Api.DestroyDevice(Device, null); }
            if (Instance.Handle != 0) Api.DestroyInstance(Instance, null);
        }
        finally { Api.Dispose(); }
    }
}
