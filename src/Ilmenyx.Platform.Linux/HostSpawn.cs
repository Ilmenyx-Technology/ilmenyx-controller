// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Diagnostics;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Flatpak bridge. Inside the Flatpak sandbox the system programs Ilmenyx reads (nvidia-smi, lspci, dpkg-query, pkexec, smartctl, Steam...)
/// live on the host, so they run through <c>flatpak-spawn --host</c> (permission <c>--talk-name=org.freedesktop.Flatpak</c>), and host files
/// under /usr, /etc and /lib are read from /run/host (permission <c>--filesystem=host-os:ro</c>). Outside Flatpak nothing changes.
/// </summary>
public static class HostSpawn
{
    /// <summary>Running in the Flatpak sandbox (flatpak writes /.flatpak-info into every sandbox). Tests may override.</summary>
    public static bool InFlatpak { get; set; } = File.Exists("/.flatpak-info");

    /// <summary>Programs shipped inside the Flatpak (/app) run in the sandbox; everything else runs on the host.</summary>
    public static bool RunsInSandbox(string path) => path.StartsWith("/app/", StringComparison.Ordinal);

    /// <summary>
    /// Pure: the program and arguments to start for <paramref name="path"/>. Native or a sandbox program: unchanged.
    /// Host program in Flatpak: <c>flatpak-spawn --host --watch-bus [--directory=D] [--env=K=V]... path args...</c>
    /// (--watch-bus ends the host process if Ilmenyx exits; the environment of the sandbox is not inherited, so the variables are passed).
    /// </summary>
    public static (string Program, IReadOnlyList<string> Arguments) Wrap(string path, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, string? workingDirectory, bool inFlatpak)
    {
        if (!inFlatpak || RunsInSandbox(path)) return (path, arguments);
        var list = new List<string> { "--host", "--watch-bus" };
        if (workingDirectory is { Length: > 0 } wd) list.Add("--directory=" + wd);
        if (environment is not null)
            foreach (var (key, value) in environment.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                if (key.Length == 0 || key.Contains('=')) throw new ArgumentException("Variável de ambiente inválida: " + key);
                list.Add("--env=" + key + "=" + value);
            }
        list.Add(path);
        list.AddRange(arguments);
        return ("/usr/bin/flatpak-spawn", list);
    }

    /// <summary>ProcessStartInfo for <paramref name="path"/>, bridged to the host when needed. Environment entries go to the process in both cases.</summary>
    public static ProcessStartInfo Start(string path, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, string? workingDirectory = null, bool redirect = true)
    {
        var (program, args) = Wrap(path, arguments, environment, workingDirectory, InFlatpak);
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = redirect, RedirectStandardError = redirect, CreateNoWindow = true };
        if (!InFlatpak || RunsInSandbox(path)) start.WorkingDirectory = workingDirectory ?? "";
        foreach (var a in args) start.ArgumentList.Add(a);
        if (environment is not null) foreach (var (k, v) in environment) start.Environment[k] = v;
        return start;
    }

    /// <summary>
    /// Host view of an absolute system path: in Flatpak, /usr, /etc, /lib, /lib64, /bin and /sbin of the host are mounted under /run/host
    /// (the sandbox's own /usr is the Flatpak runtime). Other paths (/sys, /proc, /dev, home) are shared and stay as they are.
    /// </summary>
    public static string HostPath(string path) => HostPath(path, InFlatpak);

    public static string HostPath(string path, bool inFlatpak)
    {
        if (!inFlatpak || !path.StartsWith('/')) return path;
        foreach (var root in HostRoots)
            if (path == root || path.StartsWith(root + "/", StringComparison.Ordinal)) return "/run/host" + path;
        return path;
    }

    /// <summary>Host path of the app's /app folder ([Instance] app-path in /.flatpak-info); null outside Flatpak or when unreadable.</summary>
    public static string? AppPathOnHost { get; set; } = InFlatpak ? AppPathFromInfo(ReadInfo()) : null;

    private static string ReadInfo() { try { return File.ReadAllText("/.flatpak-info"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; } }

    /// <summary>Pure: app-path of the [Instance] section of a /.flatpak-info text.</summary>
    public static string? AppPathFromInfo(string info)
    {
        bool instance = false;
        foreach (var raw in info.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { instance = line == "[Instance]"; continue; }
            if (instance && line.StartsWith("app-path=", StringComparison.Ordinal) && line[9..] is { Length: > 1 } v && v.StartsWith('/')) return v;
        }
        return null;
    }

    /// <summary>A sandbox path under /app as the host sees it (for scripts and layer manifests the host runs). Others unchanged.</summary>
    public static string ForHost(string sandboxPath, string? appPathOnHost) =>
        appPathOnHost is { } host && (sandboxPath == "/app" || sandboxPath.StartsWith("/app/", StringComparison.Ordinal)) ? host + sandboxPath[4..] : sandboxPath;

    private static readonly string[] HostRoots = ["/usr", "/etc", "/lib", "/lib64", "/bin", "/sbin"];
}
