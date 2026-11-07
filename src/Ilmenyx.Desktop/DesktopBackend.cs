// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Avalonia;

namespace Ilmenyx.Desktop;

public enum DisplayBackend { Auto, X11, Wayland }

/// <summary>Explicit composition-root choice. Native Wayland never falls back silently to X11.</summary>
public static class DesktopBackend
{
    public const string EnvironmentVariable = "ILMENYX_DISPLAY_BACKEND";
    public const string LegacyEnvironmentVariable = "SILEX_DISPLAY_BACKEND";
    /// <summary>Reverse-DNS app ID (packaging/linux): X11 WM_CLASS and Wayland app_id, so the desktop matches windows to the .desktop entry and icon.</summary>
    public const string AppId = "io.github.bismuth_technology.Ilmenyx";
    public static string? ReadSelection() => Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable), Environment.GetEnvironmentVariable(LegacyEnvironmentVariable));
    public static string? Resolve(string? current, string? legacy) => current ?? legacy;
    public static DisplayBackend Parse(string? value) => value switch
    {
        null or "" or "auto" => DisplayBackend.Auto,
        "x11" => DisplayBackend.X11,
        "wayland" => DisplayBackend.Wayland,
        _ => throw new ArgumentException("ILMENYX_DISPLAY_BACKEND deve ser auto, x11 ou wayland.")
    };
    /// <summary>
    /// What "auto" means for a session: X11 whenever DISPLAY exists (the decided default, also under XWayland); native Wayland when only
    /// WAYLAND_DISPLAY exists. Without this, a Wayland-only session (no XWayland) aborted with "XOpenDisplay failed" (seen 2026-11-06).
    /// </summary>
    public static DisplayBackend ForSession(DisplayBackend requested, string? display, string? waylandDisplay) =>
        requested == DisplayBackend.Auto && string.IsNullOrEmpty(display) && !string.IsNullOrEmpty(waylandDisplay) ? DisplayBackend.Wayland : requested;

    public static AppBuilder Configure(AppBuilder builder, DisplayBackend backend)
    {
        if (OperatingSystem.IsLinux())
            backend = ForSession(backend, Environment.GetEnvironmentVariable("DISPLAY"), Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (backend != DisplayBackend.Auto && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Seleção explícita X11/Wayland está disponível somente no Linux.");
        builder = builder.UsePlatformDetect() // Also configures Skia and font services.
            .WithInterFont().With(new Avalonia.Media.FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" }) // brand typeface (MANUAL-ILMENYX), bundled (OFL-1.1)
            .With(new X11PlatformOptions { WmClass = AppId })
            .With(new WaylandPlatformOptions { AppId = AppId });
        return backend switch
        {
            DisplayBackend.Auto => builder,
            DisplayBackend.X11 => builder.UseX11(),
            DisplayBackend.Wayland => builder.UseWayland(),
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
    }
}
