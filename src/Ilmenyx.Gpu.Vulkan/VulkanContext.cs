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
    private bool disposed;

    public VulkanContext(int adapterIndex)
    {
        Api = Vk.GetApi();
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
        AdapterName = Marshal.PtrToStringUTF8((nint)props.DeviceName) ?? "?"; DeviceType = props.DeviceType.ToString();
        Api.GetPhysicalDeviceMemoryProperties(Physical, out var mem); Memory = mem;
        uint families = 0; Api.GetPhysicalDeviceQueueFamilyProperties(Physical, ref families, null);
        var qf = new QueueFamilyProperties[families];
        fixed (QueueFamilyProperties* q = qf) Api.GetPhysicalDeviceQueueFamilyProperties(Physical, ref families, q);
        int family = Array.FindIndex(qf, f => (f.QueueFlags & QueueFlags.GraphicsBit) != 0);
        if (family < 0) throw new VulkanException("A GPU não oferece fila gráfica.");
        QueueFamily = (uint)family;
        float priority = 1f;
        var qci = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = QueueFamily, QueueCount = 1, PQueuePriorities = &priority };
        var f13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features, DynamicRendering = true };
        var dci = new DeviceCreateInfo { SType = StructureType.DeviceCreateInfo, PNext = &f13, QueueCreateInfoCount = 1, PQueueCreateInfos = &qci };
        Check(Api.CreateDevice(Physical, in dci, null, out var device), "Criar o dispositivo Vulkan"); Device = device;
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
