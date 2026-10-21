using Avalonia;

namespace Silex.Audit.Desktop;

public enum DisplayBackend { Auto, X11, Wayland }

/// <summary>Explicit composition-root choice. Native Wayland never falls back silently to X11.</summary>
public static class DesktopBackend
{
    public const string EnvironmentVariable = "SILEX_DISPLAY_BACKEND";
    public static DisplayBackend Parse(string? value) => value switch
    {
        null or "" or "auto" => DisplayBackend.Auto,
        "x11" => DisplayBackend.X11,
        "wayland" => DisplayBackend.Wayland,
        _ => throw new ArgumentException("SILEX_DISPLAY_BACKEND deve ser auto, x11 ou wayland.")
    };
    public static AppBuilder Configure(AppBuilder builder, DisplayBackend backend)
    {
        if (backend != DisplayBackend.Auto && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Seleção explícita X11/Wayland está disponível somente no Linux.");
        builder = builder.UsePlatformDetect(); // Also configures Skia and font services.
        return backend switch
        {
            DisplayBackend.Auto => builder,
            DisplayBackend.X11 => builder.UseX11(),
            DisplayBackend.Wayland => builder.UseWayland(),
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
    }
}
