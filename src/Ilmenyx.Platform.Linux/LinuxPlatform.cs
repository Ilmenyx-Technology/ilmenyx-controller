// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Ilmenyx.Contracts;

namespace Ilmenyx.Platform.Linux;

public static class LinuxPlatform
{
    public static LinuxHardwareInfoProvider CreateHardwareProvider() => new(new LinuxFiles(), CreateSensorProvider(), new DrmGpuInfoProvider(new DiagnosticSysfsFiles()));
    public static ISensorProvider CreateSensorProvider() => new LinuxSensorProvider(new HwmonSensorProvider(new HwmonFiles()), new KernelDiagnosticSensors(new DiagnosticSysfsFiles()), new GpuTelemetrySensors());
}
