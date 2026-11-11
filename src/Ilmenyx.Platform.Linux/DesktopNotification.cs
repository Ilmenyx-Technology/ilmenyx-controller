// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.ComponentModel;

namespace Ilmenyx.Platform.Linux;

/// <summary>Desktop notifications (notify-send, from libnotify) and whether the desktop shows tray icons (StatusNotifierItem).</summary>
public static class DesktopNotification
{
    public const string IconName = "io.github.ilmenyx_technology.IlmenyxController";

    /// <summary>Shows a system notification; false when notify-send is missing or fails (the app then uses its own toast).</summary>
    public static async Task<bool> SendAsync(string title, string body, CancellationToken ct = default)
    {
        if (BoundedProcess.Find("notify-send") is null) return false;
        try
        {
            var (code, _) = await BoundedProcess.RunAsync("notify-send", ["--app-name=Ilmenyx Controller", "--icon=" + IconName, title, body], TimeSpan.FromSeconds(5), 4096, ct);
            return code == 0;
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or Win32Exception or OperationCanceledException) { return false; }
    }

    /// <summary>
    /// True when a StatusNotifierItem host is running (KDE, XFCE, Cinnamon; GNOME only with the AppIndicator extension). Without one the
    /// tray icon would be invisible, so the app never hides its window to it.
    /// </summary>
    public static async Task<bool> TrayAvailableAsync(CancellationToken ct = default)
    {
        if (BoundedProcess.Find("gdbus") is null) return false;
        try
        {
            var (code, output) = await BoundedProcess.RunAsync("gdbus", ["call", "--session", "--dest", "org.kde.StatusNotifierWatcher", "--object-path", "/StatusNotifierWatcher",
                "--method", "org.freedesktop.DBus.Properties.Get", "org.kde.StatusNotifierWatcher", "IsStatusNotifierHostRegistered"], TimeSpan.FromSeconds(3), 1024, ct);
            return code == 0 && output.Contains("true", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or Win32Exception or OperationCanceledException) { return false; }
    }
}
