// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Third-party components retain their terms (MangoHud: MIT, OpenRGB: GPL-2.0-only).
using System.Text;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Programs that travel inside the Ilmenyx package (components/): MangoHud (overlay layer), OpenRGB (AppImage) and a built-in GameMode fallback.
/// The system copy wins when present (same distro libraries, full GameMode daemon); the bundled copy is the fallback so nothing needs a separate install.
/// Wrappers are small shell scripts Ilmenyx writes into its own data folder; nothing is installed system-wide and no root is used.
/// </summary>
public static class BundledComponents
{
    public const string RootVariable = "ILMENYX_COMPONENTS_DIR", PreferBundledVariable = "ILMENYX_PREFER_BUNDLED";

    public static string? Root()
    {
        string? over = Environment.GetEnvironmentVariable(RootVariable);
        string root = over is { Length: > 0 } && Path.IsPathRooted(over) ? over : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "components"));
        return Directory.Exists(root) ? root : null;
    }

    public static bool PreferBundled => Environment.GetEnvironmentVariable(PreferBundledVariable) == "1";

    public static string DataFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d && Path.IsPathRooted(d) ? d : Path.Combine(home, ".local", "share");
        return Path.Combine(data, "ilmenyx", "components");
    }

    public static string MangoHudLibraryFolder(string root) => Path.Combine(root, "mangohud", "lib64");
    public static string OpenRgbImage(string root) => Path.Combine(root, "openrgb", "OpenRGB.AppImage");
    public static string UmuZipapp(string root) => Path.Combine(root, "umu", "umu-run");

    /// <summary>True when the program is available only because the package carries it (or a built-in replacement).</summary>
    public static bool IsProvided(string name) => name switch
    {
        "gamemoderun" => true,
        "mangohud" => Root() is { } r && File.Exists(Path.Combine(MangoHudLibraryFolder(r), "libMangoHud.so")),
        "openrgb" => Root() is { } r2 && File.Exists(OpenRgbImage(r2)),
        "umu-run" => Root() is { } r3 && File.Exists(UmuZipapp(r3)),
        _ => false,
    };

    /// <summary>Wrapper script for a bundled program, created on demand in the data folder; null if the package does not carry it.</summary>
    public static string? Resolve(string name) => Root() is { } r ? ResolveIn(name, r, DataFolder()) : name == "gamemoderun" ? ResolveIn(name, "", DataFolder()) : null;

    internal static string? ResolveIn(string name, string root, string data)
    {
        try
        {
            return name switch
            {
                "gamemoderun" => Write(data, name, GameModeScript()),
                "mangohud" when File.Exists(Path.Combine(MangoHudLibraryFolder(root), "libMangoHud.so")) && File.Exists(Path.Combine(MangoHudLibraryFolder(root), "libMangoHud_shim.so")) => ResolveMangoHud(root, data),
                "openrgb" when File.Exists(OpenRgbImage(root)) => Write(data, name, OpenRgbScript(HostSpawn.ForHost(OpenRgbImage(root), HostSpawn.AppPathOnHost))),
                "umu-run" when File.Exists(UmuZipapp(root)) => Write(data, name, UmuScript(HostSpawn.ForHost(UmuZipapp(root), HostSpawn.AppPathOnHost))),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    internal const string MangoHudLayer = "VK_LAYER_MANGOHUD_overlay_x86_64";

    private static string ResolveMangoHud(string root, string data)
    {
        // Flatpak: games run on the host, which sees /app under its own path: the manifest and script name the host path (unchanged natively).
        string lib = HostSpawn.ForHost(MangoHudLibraryFolder(root), HostSpawn.AppPathOnHost);
        // Explicit layer requested by name: VK_ADD_LAYER_PATH + VK_INSTANCE_LAYERS. The earlier implicit layer relied on VK_ADD_IMPLICIT_LAYER_PATH, which the
        // loader of Ubuntu 24.04 / Mint 22 (1.3.275) ignores, so the overlay never loaded there (seen on real hardware, 2026-11-06). Neither variable replaces
        // the system search paths, so system layers (Mesa device_select, Steam, OBS) keep loading.
        string layers = Path.Combine(data, "vulkan", "explicit_layer.d");
        Directory.CreateDirectory(layers);
        try { File.Delete(Path.Combine(data, "vulkan", "implicit_layer.d", "MangoHud.json")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // stale file of older versions
        string manifest = "{\n  \"file_format_version\": \"1.0.0\",\n  \"layer\": {\n    \"name\": \"" + MangoHudLayer + "\",\n    \"type\": \"GLOBAL\",\n    \"api_version\": \"1.3.0\",\n    \"library_path\": " + System.Text.Json.JsonSerializer.Serialize(Path.Combine(lib, "libMangoHud.so")) + ",\n    \"implementation_version\": \"1\",\n    \"description\": \"Vulkan Hud Overlay (MangoHud, bundled with Ilmenyx)\",\n    \"functions\": { \"vkNegotiateLoaderLayerInterfaceVersion\": \"vkNegotiateLoaderLayerInterfaceVersion\" }\n  }\n}\n";
        WriteAtomic(Path.Combine(layers, "MangoHud.json"), manifest, executable: false);
        string shim = GameLaunchOptions.ShellQuote(Path.Combine(lib, "libMangoHud_shim.so"));
        string script = "#!/bin/sh\n# Generated by Ilmenyx: runs a program with the MangoHud copy that ships in the Ilmenyx package (MIT).\n"
            + "[ \"$#\" -gt 0 ] || { echo 'Uso: mangohud <programa>' >&2; exit 1; }\n"
            + "SHIM=" + shim + "\n"
            + "case \":${LD_PRELOAD-}:\" in *\":$SHIM:\"*) ;; *) LD_PRELOAD=\"${LD_PRELOAD:+$LD_PRELOAD:}$SHIM\" ;; esac\n"
            + "VK_ADD_LAYER_PATH=\"${VK_ADD_LAYER_PATH:+$VK_ADD_LAYER_PATH:}" + layers.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`") + "\"\n"
            + "case \":${VK_INSTANCE_LAYERS-}:\" in *\":" + MangoHudLayer + ":\"*) ;; *) VK_INSTANCE_LAYERS=\"${VK_INSTANCE_LAYERS:+$VK_INSTANCE_LAYERS:}" + MangoHudLayer + "\" ;; esac\n"
            + "exec env MANGOHUD=1 LD_PRELOAD=\"$LD_PRELOAD\" VK_ADD_LAYER_PATH=\"$VK_ADD_LAYER_PATH\" VK_INSTANCE_LAYERS=\"$VK_INSTANCE_LAYERS\" \"$@\"\n";
        return Write(data, "mangohud", script);
    }

    /// <summary>Built-in GameMode replacement: what GameMode does without a daemon or root — highest I/O priority and a lower nice value for the game, when the system allows it.</summary>
    private static string GameModeScript() =>
        "#!/bin/sh\n# Generated by Ilmenyx: user-level game boost (no daemon, no root). Used only when the system gamemoderun is absent.\n"
        + "[ \"$#\" -gt 0 ] || { echo 'Uso: gamemoderun <programa>' >&2; exit 1; }\n"
        + "command -v ionice >/dev/null 2>&1 && ionice -c 2 -n 0 -p $$ >/dev/null 2>&1\n"
        + "command -v renice >/dev/null 2>&1 && renice -n -5 -p $$ >/dev/null 2>&1\n"
        + "exec \"$@\"\n";

    private static string OpenRgbScript(string image) =>
        "#!/bin/sh\n# Generated by Ilmenyx: runs the OpenRGB AppImage that ships in the package (GPL-2.0-only), extracting to a temporary folder (no FUSE needed).\n"
        + "APPIMAGE_EXTRACT_AND_RUN=1 exec " + GameLaunchOptions.ShellQuote(image) + " \"$@\"\n";

    /// <summary>umu-launcher zipapp (GPL-3.0) run with the system python3: the same file works on every distro. Started through python3, so a
    /// package on a noexec mount still works.</summary>
    internal static string UmuScript(string zipapp) =>
        "#!/bin/sh\n# Generated by Ilmenyx: runs the umu-launcher that ships in the package (GPL-3.0) to start Windows games with Proton.\n"
        + "command -v python3 >/dev/null 2>&1 || { echo 'Ilmenyx: o Proton fora da Steam precisa do python3 (pacote python3 ou python da sua distro).' >&2; exit 127; }\n"
        + "exec python3 " + GameLaunchOptions.ShellQuote(zipapp) + " \"$@\"\n";

    private static string Write(string data, string name, string script)
    {
        string path = Path.Combine(data, "bin", name);
        if (!File.Exists(path) || File.ReadAllText(path) != script) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); WriteAtomic(path, script, executable: true); }
        return path;
    }

    private static void WriteAtomic(string path, string content, bool executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && File.ReadAllText(path) == content) return;
        string temp = path + ".tmp" + Environment.ProcessId;
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(temp, executable ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: true);
    }
}
