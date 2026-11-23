// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.ComponentModel;

namespace Ilmenyx.Platform.Linux;

/// <summary>Desktop notifications (notify-send, from libnotify) and whether the desktop shows tray icons (StatusNotifierItem).</summary>
public static class DesktopNotification
{
    public const string IconName = "io.github.ilmenyx_technology.IlmenyxController";

    /// <summary>Shows a system notification; false when notify-send is missing or fails (the app then uses its own toast).</summary>
    /// <param name="icon">Image file shown in the notification (an achievement icon); the app icon when null.</param>
    /// <param name="quiet">Low urgency: Plasma files it in the history without a popup (the app already showed its own toast).</param>
    /// <param name="urgent">Critical urgency, so the desktop shows it over fullscreen windows.</param>
    public static async Task<bool> SendAsync(string title, string body, CancellationToken ct = default, string? icon = null, bool urgent = false, bool quiet = false)
    {
        if (BoundedProcess.Find("notify-send") is null) return false;
        try
        {
            List<string> args = ["--app-name=Ilmenyx Controller", "--icon=" + (icon is { Length: > 0 } && Path.IsPathFullyQualified(icon) ? icon : IconName)];
            // Critical urgency is what makes Plasma show the notice over a fullscreen game; normal ones only reach the history.
            if (urgent) args.AddRange(["--urgency=critical", "--expire-time=8000"]);
            else if (quiet) args.Add("--urgency=low");
            args.AddRange([title, body]);
            var (code, _) = await BoundedProcess.RunAsync("notify-send", args, TimeSpan.FromSeconds(5), 4096, ct);
            return code == 0;
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or Win32Exception or OperationCanceledException) { return false; }
    }

    /// <summary>Plays the freedesktop "complete" sound with pw-play or paplay (the notification sound hint is ignored by Plasma); silent when no file or player exists.</summary>
    public static async Task PlaySoundAsync(CancellationToken ct = default)
    {
        // shortcut: an HDMI sink that was idle may swallow the first ~1 s of the sound (no pre-roll), add a silent lead-in if users report it.
        // shortcut: the /run/host path (Flatpak sees the host /usr there) is untested, check it in a Flatpak build.
        var file = new[] { "/usr/share/sounds/freedesktop/stereo/complete.oga", "/run/host/usr/share/sounds/freedesktop/stereo/complete.oga" }.FirstOrDefault(File.Exists);
        var player = new[] { "pw-play", "paplay" }.FirstOrDefault(p => BoundedProcess.Find(p) is not null);
        if (file is null || player is null) return;
        try { await BoundedProcess.RunAsync(player, [file], TimeSpan.FromSeconds(5), 1024, ct); }
        catch (Exception e) when (e is IOException or FileNotFoundException or Win32Exception or OperationCanceledException) { }
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
