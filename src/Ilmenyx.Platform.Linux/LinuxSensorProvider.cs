// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public sealed class LinuxSensorProvider(ISensorProvider hwmon, ISensorProvider diagnostics, ISensorProvider? gpu = null) : ISensorProvider
{
    public async Task<SensorSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<SensorSnapshot>> { hwmon.CollectAsync(cancellationToken), diagnostics.CollectAsync(cancellationToken) };
        if (gpu is not null) tasks.Add(gpu.CollectAsync(cancellationToken));
        var parts = await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();
        string status = parts.All(s => s.Status == "available") ? "available" : parts.All(s => s.Status == "unavailable") ? "unavailable" : "partial";
        return new("1.1", DateTimeOffset.UtcNow, status, status == "available" ? null : L.T("Algumas fontes estão indisponíveis; consulte cada leitura e limitações."),
            parts.SelectMany(s => s.Readings).ToArray(), parts.SelectMany(s => s.Limitations).Distinct().ToArray(),
            parts.SelectMany(s => s.PowerSupplies ?? []).ToArray());
    }
}
