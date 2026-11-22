// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Security.Cryptography;
using System.Text;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Menu and desktop shortcuts that open Ilmenyx straight on the games ("--games") or on one game ("--play KEY"). Plain .desktop files in
/// the user's own folders: removing the file removes the shortcut. Nothing is written outside the user's home.
/// </summary>
public static class AppShortcuts
{
    public const string GamesArg = "--games", PlayArg = "--play";

    /// <summary>How to start this copy again: the Flatpak through flatpak run, the AppImage by its file, else this program.</summary>
    public static IReadOnlyList<string> SelfCommand(string appId)
    {
        if (HostSpawn.InFlatpak) return ["flatpak", "run", appId];
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image) return [image];
        string program = Environment.ProcessPath ?? "Ilmenyx.Desktop";
        // Under "dotnet Ilmenyx.Desktop.dll" (development) the assembly path comes first.
        return Path.GetFileNameWithoutExtension(program) == "dotnet" && Environment.GetCommandLineArgs() is [{ } dll, ..] ? [program, Path.GetFullPath(dll)] : [program];
    }

    /// <summary>The user's application menu folder (in Flatpak, the host's, not the sandbox's remapped data folder).</summary>
    public static string MenuFolder(string home) =>
        !HostSpawn.InFlatpak && Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d && Path.IsPathFullyQualified(d) ? Path.Combine(d, "applications") : Path.Combine(home, ".local", "share", "applications");

    /// <summary>The desktop folder from user-dirs.dirs ("Área de trabalho", "Desktop"…); null when there is none.</summary>
    public static string? DesktopFolder(string home)
    {
        string file = Path.Combine(home, ".config", "user-dirs.dirs");
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length < 16384)
                foreach (var line in File.ReadLines(file))
                    if (line.StartsWith("XDG_DESKTOP_DIR=", StringComparison.Ordinal) && line[16..].Trim('"') is { Length: > 0 } v)
                    {
                        string dir = v.StartsWith("$HOME", StringComparison.Ordinal) ? home + v[5..] : v;
                        return dir.TrimEnd('/') != home.TrimEnd('/') && Directory.Exists(dir) ? dir : null; // desktop = home means "no desktop"
                    }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        string fallback = Path.Combine(home, "Desktop");
        return Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>Stable file name for one game's shortcut (its library key can hold spaces and slashes).</summary>
    public static string GameFileName(string key) => "ilmenyx-jogo-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12] + ".desktop";

    /// <summary>A .desktop entry. Exec words are quoted by the Desktop Entry rules, so paths with spaces, quotes or % survive.</summary>
    public static string Entry(string name, IEnumerable<string> exec, string icon, string comment, string wmClass) =>
        "[Desktop Entry]\nType=Application\nName=" + OneLine(name) + "\nComment=" + OneLine(comment) + "\nExec=" + string.Join(' ', exec.Select(Quote)) +
        "\nIcon=" + OneLine(icon) + "\nTerminal=false\nCategories=Game;\nStartupWMClass=" + OneLine(wmClass) + "\n";

    public static string Quote(string word)
    {
        string escaped = word.Replace("%", "%%");
        if (escaped.Length > 0 && escaped.All(c => !char.IsWhiteSpace(c) && c is not ('"' or '\'' or '\\' or '>' or '<' or '~' or '|' or '&' or ';' or '$' or '*' or '?' or '#' or '(' or ')' or '`' or '='))) return escaped;
        // Inside quotes: \" \` \$ \\ escaped; then the whole value escapes its backslashes once more (string-level rule of the spec).
        string inner = new StringBuilder(escaped).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").ToString();
        return ("\"" + inner + "\"").Replace("\\", "\\\\");
    }

    /// <summary>Writes the entry (replacing an older one of the same name) with the execute bit desktops ask for. Returns the path.</summary>
    public static string Write(string folder, string fileName, string text)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName), tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    private static string OneLine(string s) => s.Replace('\n', ' ').Replace('\r', ' ').Trim();
}
