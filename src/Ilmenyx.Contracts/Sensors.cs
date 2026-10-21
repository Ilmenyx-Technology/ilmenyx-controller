namespace Ilmenyx.Contracts;

public sealed record SensorMeasurement(string Device, string Chip, string Channel, string Label,
    DateTimeOffset CollectedAt, string Quality, Reading<double?> Reading);
public sealed record SensorSnapshot(string SchemaVersion, DateTimeOffset CollectedAt, string Status, string? Reason,
    IReadOnlyList<SensorMeasurement> Readings, IReadOnlyList<string> Limitations,
    IReadOnlyList<PowerSupplyInfo>? PowerSupplies = null);
public interface ISensorProvider
{
    Task<SensorSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}

public sealed record PowerSupplyInfo(string Device, Reading<string> Type, Reading<string> Status, Reading<double?> Present);
