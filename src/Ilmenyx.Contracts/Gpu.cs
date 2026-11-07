// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
namespace Ilmenyx.Contracts;

public sealed record GpuInfo(string Device, Reading<string> VendorId, Reading<string> DeviceId, Reading<string> PciClass,
    Reading<double?> BusyPercent, Reading<long?> VramTotal, Reading<long?> VramUsed);
public sealed record GpuSnapshot(string SchemaVersion, DateTimeOffset CollectedAt, string Status, string? Reason,
    IReadOnlyList<GpuInfo> Devices, IReadOnlyList<string> Limitations);
public interface IGpuInfoProvider
{
    Task<GpuSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}
