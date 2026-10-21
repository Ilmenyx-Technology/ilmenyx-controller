using Ilmenyx.Contracts;

namespace Ilmenyx.Platform.Linux;

public sealed class LinuxSensorProvider(ISensorProvider hwmon, ISensorProvider diagnostics) : ISensorProvider
{
    public async Task<SensorSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        var parts = await Task.WhenAll(hwmon.CollectAsync(cancellationToken), diagnostics.CollectAsync(cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        string status = parts.All(s => s.Status == "available") ? "available" : parts.All(s => s.Status == "unavailable") ? "unavailable" : "partial";
        return new("1.1", DateTimeOffset.UtcNow, status, status == "available" ? null : "Algumas fontes estão indisponíveis; consulte cada leitura e limitações.",
            parts.SelectMany(s => s.Readings).ToArray(), parts.SelectMany(s => s.Limitations).Distinct().ToArray(),
            parts.SelectMany(s => s.PowerSupplies ?? []).ToArray());
    }
}
