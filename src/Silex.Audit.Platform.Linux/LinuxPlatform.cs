using Silex.Audit.Contracts;

namespace Silex.Audit.Platform.Linux;

public static class LinuxPlatform
{
    public static LinuxHardwareInfoProvider CreateHardwareProvider() => new(new LinuxFiles(), CreateSensorProvider(), new DrmGpuInfoProvider(new DiagnosticSysfsFiles()));
    public static ISensorProvider CreateSensorProvider() => new LinuxSensorProvider(new HwmonSensorProvider(new HwmonFiles()), new KernelDiagnosticSensors(new DiagnosticSysfsFiles()));
}
