// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Files picked inside a Flatpak can come back as a document-portal alias ("/run/user/1000/doc/&lt;id&gt;/game.exe") that shows only that
/// one file: the folder name is a random ID and the game's other files are missing. The portal tells the real path in an attribute.
/// </summary>
public static partial class DocumentPortal
{
    [GeneratedRegex(@"^/run/user/\d+/doc/[^/]+/.")] private static partial Regex Alias();
    [DllImport("libc", SetLastError = true, EntryPoint = "getxattr")]
    private static extern nint GetXattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] value, nuint size);

    public static bool IsAlias(string path) => Alias().IsMatch(path);

    /// <summary>The path on the user's disk behind a portal alias; the path itself when it is not an alias or the portal does not say.</summary>
    public static string HostPath(string path, Func<string, string?>? read = null)
    {
        if (!IsAlias(path)) return path;
        string? real = (read ?? ReadAttribute)(path);
        return real is { Length: > 1 } && Path.IsPathFullyQualified(real) && !IsAlias(real) ? real : path;
    }

    /// <summary><see cref="HostPath"/> when this app can read the file there (older installs lack the permission); else the alias.</summary>
    public static string RealPath(string path, Func<string, string?>? read = null, Func<string, bool>? exists = null) =>
        HostPath(path, read) is var real && real != path && (exists ?? File.Exists)(real) ? real : path;

    /// <summary>Moves hand-added games saved under a portal alias to their real path, with their profile; a name that was the alias ID is redone.</summary>
    public static bool Migrate(IlmenyxSettings s, Func<string, string> realPath, Func<string, string> nameFor)
    {
        bool changed = false;
        foreach (var c in s.CustomGames.Where(c => IsAlias(c.Exe)).ToList())
        {
            string old = c.Exe, real = realPath(old);
            if (real == old || s.CustomGames.Any(o => o.Exe == real)) continue;
            if (c.Name == Path.GetFileName(Path.GetDirectoryName(old))) c.Name = nameFor(real);
            c.Exe = real;
            if (s.GameProfiles.Remove("Manual:" + old, out var p)) s.GameProfiles["Manual:" + real] = p;
            changed = true;
        }
        return changed;
    }

    private static string? ReadAttribute(string path)
    {
        try
        {
            var buffer = new byte[4096];
            nint n = GetXattr(path, "user.document-portal.host-path", buffer, (nuint)buffer.Length);
            return n > 0 ? Encoding.UTF8.GetString(buffer, 0, (int)n).TrimEnd('\0') : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }
}
