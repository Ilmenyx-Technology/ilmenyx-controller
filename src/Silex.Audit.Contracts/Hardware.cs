namespace Silex.Audit.Contracts;

public sealed record Reading<T>(T? Value, string Unit, string Source, string Status, string? Reason)
{
    public static Reading<T> Available(T value, string unit, string source) => new(value, unit, source, "available", null);
    public static Reading<T> Missing(string unit, string source, string reason) => new(default, unit, source, "unavailable", reason);
}
public sealed record CpuInfo(Reading<string> Name, Reading<int?> LogicalProcessors);
public sealed record MemoryInfo(Reading<long?> Total, Reading<long?> Available);
public sealed record DiskInfo(string Device, Reading<long?> Capacity);
public sealed record Capability(string Name, string Status, string Reason);
public sealed record HardwareSnapshot(string SchemaVersion, string ApplicationVersion, DateTimeOffset CollectedAt,
    string Runtime, Reading<string> OperatingSystem, Reading<string> Kernel, CpuInfo Cpu, MemoryInfo Memory,
    Reading<IReadOnlyList<DiskInfo>> Disks, IReadOnlyList<Capability> Capabilities, IReadOnlyList<string> Limitations, SensorSnapshot? Sensors = null, GpuSnapshot? Gpus = null);
public interface IHardwareInfoProvider
{
    Task<HardwareSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}
