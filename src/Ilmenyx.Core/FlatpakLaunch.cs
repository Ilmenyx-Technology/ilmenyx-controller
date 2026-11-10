// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>
/// A game installed as Flatpak runs in a sandbox: the host's mangohud (LD_PRELOAD) does not reach it. MangoHud for Flatpak is the runtime
/// extension org.freedesktop.Platform.VulkanLayer.MangoHud; its own mangohud script wraps the app's command (Vulkan and OpenGL), and the
/// sandbox needs to see Ilmenyx's MangoHud config and log folder. Validated with SuperTuxKart (OpenGL), 2026-11-10.
/// </summary>
public static class FlatpakLaunch
{
    public const string MangoHudExtension = "org.freedesktop.Platform.VulkanLayer.MangoHud";
    public const string MangoHudScript = "/usr/lib/extensions/vulkan/MangoHud/bin/mangohud";

    /// <summary>Application ID of <c>flatpak run [--option=value…] &lt;app&gt; [args]</c>, or null when the arguments are not that.</summary>
    public static string? AppId(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || args[0] != "run") return null;
        foreach (var a in args.Skip(1))
            if (!a.StartsWith('-')) return IsAppId(a) ? a : null;
        return null;
    }

    /// <summary>
    /// Rewrites <c>flatpak run</c> arguments so the app's command runs under the extension's mangohud, with MANGOHUD=1, Ilmenyx's config (read
    /// only) and log folder. <paramref name="defaultCommand"/> is the app's own command (metadata), used when the entry has no --command.
    /// </summary>
    public static IReadOnlyList<string> WithMangoHud(IReadOnlyList<string> args, string defaultCommand, string? configFile, string logFolder)
    {
        string app = AppId(args) ?? throw new ArgumentException(L.T("Não é um comando flatpak run."), nameof(args));
        int appAt = args.ToList().IndexOf(app, 1);
        var options = args.Skip(1).Take(appAt - 1).ToList();
        string command = options.FirstOrDefault(o => o.StartsWith("--command=", StringComparison.Ordinal))?["--command=".Length..] ?? defaultCommand;
        var result = new List<string> { "run" };
        result.AddRange(options.Where(o => !o.StartsWith("--command=", StringComparison.Ordinal)));
        result.Add("--env=MANGOHUD=1");
        if (configFile is not null)
        {
            result.Add("--env=MANGOHUD_CONFIGFILE=" + configFile);
            result.Add("--filesystem=" + Path.GetDirectoryName(configFile) + ":ro");
        }
        result.Add("--filesystem=" + logFolder);
        result.Add("--command=" + MangoHudScript);
        result.Add(app);
        result.Add(command);
        result.AddRange(args.Skip(appAt + 1));
        return result;
    }

    /// <summary>Reverse-DNS ID with at least three parts (org.example.App): what Flatpak accepts as an application ID.</summary>
    public static bool IsAppId(string s) =>
        s.Length is > 4 and <= 255 && s.Split('.') is { Length: >= 3 } parts && parts.All(p => p.Length > 0 && !char.IsAsciiDigit(p[0]) && p.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'));
}
