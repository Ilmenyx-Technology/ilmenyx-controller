// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Linux side of the gaming extras (see Ilmenyx.Core.GameExtras): the user's environment.d file for the bigger shader cache, the size of the
// shader cache folders, the Wayland session and the Proton build umu or Steam will use. Writes only the user's own file, after confirmation.
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>~/.config/environment.d/90-ilmenyx-shader-cache.conf (read by the systemd user session at login). Only a file with the Ilmenyx marker is changed or removed.</summary>
public sealed class LinuxShaderCache(string? home = null, string? configHome = null, string? cacheHome = null, Func<string, string?>? env = null)
{
    private readonly string home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly Func<string, string?> env = env ?? Environment.GetEnvironmentVariable;

    /// <summary>XDG_CONFIG_HOME when absolute (and not the Flatpak sandbox's own), else ~/.config.</summary>
    private string ConfigHome => configHome ?? (!HostSpawn.InFlatpak && env("XDG_CONFIG_HOME") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : Path.Combine(home, ".config"));
    private string CacheHome => cacheHome ?? (!HostSpawn.InFlatpak && env("XDG_CACHE_HOME") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : Path.Combine(home, ".cache"));

    public string ConfigPath => Path.Combine(ConfigHome, "environment.d", ShaderCacheTweak.FileName);

    private string? ReadFile() { try { return File.Exists(ConfigPath) && new FileInfo(ConfigPath).Length < 65536 ? File.ReadAllText(ConfigPath) : null; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; } }

    public bool IsOn => ShaderCacheTweak.IsOurs(ReadFile());
    /// <summary>A file with our name exists but was not written by the app: left alone.</summary>
    public bool ForeignFile => File.Exists(ConfigPath) && !IsOn;
    /// <summary>The Mesa limit this process inherited from the session (null = unset, the 1 GB default).</summary>
    public string? SessionValue => env(ShaderCacheTweak.MesaVariable) is { Length: > 0 } v ? v : null;

    /// <summary>Writes the file atomically (mode 0644). Null on success, otherwise the reason. Refuses to overwrite a file the app did not write.</summary>
    public string? Apply()
    {
        if (ForeignFile) return $"Já existe {ConfigPath}, criado por outra pessoa ou programa; não foi alterado.";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            string tmp = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, ShaderCacheTweak.FileContent(), new System.Text.UTF8Encoding(false));
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                File.Move(tmp, ConfigPath, overwrite: true);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "Não consegui gravar: " + e.Message; }
    }

    /// <summary>Deletes the file when it is the app's own. Null on success (also when there was nothing to undo).</summary>
    public string? Undo()
    {
        if (!File.Exists(ConfigPath)) return null;
        if (!IsOn) return $"{ConfigPath} não foi criado pelo Ilmenyx; não foi apagado.";
        try { File.Delete(ConfigPath); return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "Não consegui apagar: " + e.Message; }
    }

    /// <summary>Shader cache folders that exist, with their size (null = not found). Bounded walk: at most <paramref name="maxFiles"/> files each.</summary>
    public IReadOnlyList<(string Label, long? Bytes)> Caches(string? steamRoot, int maxFiles = 200_000)
    {
        var list = new List<(string, long?)>();
        long? mesa = null;
        try { foreach (var d in Directory.Exists(CacheHome) ? Directory.EnumerateDirectories(CacheHome, "mesa_shader_cache*") : []) mesa = (mesa ?? 0) + (DirSize(d, maxFiles) ?? 0); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        list.Add(("Mesa", mesa));
        list.Add(("NVIDIA", DirSize(Path.Combine(CacheHome, "nvidia", "GLCache"), maxFiles)));
        if (steamRoot is not null) list.Add(("Steam", DirSize(Path.Combine(steamRoot, "steamapps", "shadercache"), maxFiles)));
        return list;
    }

    public static long? DirSize(string dir, int maxFiles = 200_000)
    {
        if (!Directory.Exists(dir)) return null;
        long total = 0; int n = 0;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", options)) { total += f.Length; if (++n >= maxFiles) break; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return total;
    }
}

public static class LinuxGameExtras
{
    public static bool WaylandSession() => GameExtras.IsWaylandSession(Environment.GetEnvironmentVariable);

    /// <summary>Newest UMU-Proton/GE-Proton that umu-launcher already downloaded ($XDG_DATA_HOME/Steam/compatibilitytools.d on the host). Null before the first run.</summary>
    public static string? UmuProtonName(string? home = null, string? dataHome = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string data = dataHome ?? (!HostSpawn.InFlatpak && Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d && Path.IsPathRooted(d) ? d : Path.Combine(home, ".local", "share"));
        string dir = Path.Combine(data, "Steam", "compatibilitytools.d");
        try
        {
            if (!Directory.Exists(dir)) return null;
            return Directory.EnumerateDirectories(dir).Select(Path.GetFileName).OfType<string>()
                .Where(n => n.StartsWith("UMU-Proton", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => VersionKey(n), StringComparer.Ordinal).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Sortable key: every number padded ("UMU-Proton-10.0-4" → "UMU-Proton-000010.000000-000004").</summary>
    private static string VersionKey(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"\d+", m => m.Value.PadLeft(6, '0'));

    /// <summary>Context of a manually added game: Windows (.exe) or not, the session, the umu Proton, and whether it renders on the NVIDIA proprietary driver.</summary>
    public static ExtrasContext ManualContext(LibGame g, GameProfile p, string gpuVendor, bool multiGpu, string? umuProton) =>
        new(WaylandSession(), GameExtras.ParseProton(umuProton), gpuVendor == "NVIDIA" && (!multiGpu || p.GpuHigh), g.IsWindowsManual || (p.ExePath ?? g.ExePath ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
}
