// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Installed Flatpak apps (user and system installations), read from their metadata files; read-only, no flatpak process.</summary>
public sealed class FlatpakApps(string? home = null)
{
    private readonly string home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string[] Roots => [Path.Combine(home, ".local/share/flatpak"), "/var/lib/flatpak"];

    /// <summary>The app's default command and runtime branch ("supertuxkart", "26.08") from its metadata; nulls when not installed.</summary>
    public (string? Command, string? RuntimeBranch) Metadata(string appId)
    {
        if (!FlatpakLaunch.IsAppId(appId)) return (null, null);
        foreach (var root in Roots)
        {
            string file = Path.Combine(root, "app", appId, "current/active/metadata");
            if (!File.Exists(file)) continue;
            string text = SysfsText.Read(file, 16384);
            return (Parse(text, "command"), Parse(text, "runtime")?.Split('/') is { Length: 3 } r ? r[2] : null);
        }
        return (null, null);
    }

    /// <summary>MangoHud's Flatpak extension for this runtime branch is installed (user or system).</summary>
    public bool HasMangoHud(string runtimeBranch) =>
        runtimeBranch.Length is > 0 and < 32 && runtimeBranch.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
        && Roots.Any(r => Directory.Exists(Path.Combine(r, "runtime", FlatpakLaunch.MangoHudExtension, "x86_64", runtimeBranch)));

    /// <summary>User-scope install of the extension for this branch (no password): the arguments for flatpak.</summary>
    public static IReadOnlyList<string> InstallMangoHud(string runtimeBranch) =>
        ["install", "--user", "-y", "--noninteractive", "flathub", FlatpakLaunch.MangoHudExtension + "//" + runtimeBranch];

    /// <summary>Value of a key in the [Application] group of a metadata file.</summary>
    public static string? Parse(string metadata, string key)
    {
        bool app = false;
        foreach (var raw in metadata.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { app = line == "[Application]"; continue; }
            if (app && line.StartsWith(key + "=", StringComparison.Ordinal)) return line[(key.Length + 1)..].Trim() is { Length: > 0 } v ? v : null;
        }
        return null;
    }
}
