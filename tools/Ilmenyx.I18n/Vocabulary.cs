// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ilmenyx.I18n;

/// <summary>
/// Tells Portuguese texts from technical ones (environment variables, file keys, English tool output): a text is Portuguese when it has an
/// accented word, a Portuguese function word, or a word that is not English. English is the system word list (cracklib) plus
/// <see cref="Technical"/>; without the list, only accents and function words count.
/// </summary>
internal static partial class Vocabulary
{
    private static readonly HashSet<string> Function = ["de", "da", "das", "dos", "para", "com", "não", "uma", "um", "em", "ao", "aos", "pelo", "pela",
        "sem", "mais", "seu", "sua", "seus", "suas", "ou", "que", "na", "nas", "nos", "é", "está", "foi", "este", "esta", "isso", "pode", "já", "do"];

    /// <summary>Portuguese words that are also English words (or look like them) but in this app are Portuguese text.</summary>
    private static readonly HashSet<string> Portuguese = ["alto", "disco", "discos", "canal", "agenda", "extrema", "graves", "remover", "pronto", "rosa",
        "salvo", "inferior", "superior", "testes", "valor", "verde", "e", "baixo", "sinal", "carga", "nome", "estado", "fonte", "formato"];

    private static readonly string[] Technical = ["desktop", "download", "downloads", "devices", "device", "vendor", "decoder", "encoder", "mesa", "vulkan",
        "proton", "steam", "linux", "flatpak", "flathub", "firmware", "bluetooth", "benchmark", "hotspot", "nvidia", "intel", "amd", "radeon", "geforce",
        "kernel", "dkms", "gamemode", "mangohud", "wine", "lutris", "heroic", "bottles", "ilmenyx", "github", "gpu", "cpu", "ram", "vram", "ssd", "hdd",
        "nvme", "usb", "pci", "drm", "bios", "uefi", "smart", "zram", "swap", "rgb", "openrgb", "fps", "gamesim", "overlay", "launcher", "plasma", "kde",
        "gnome", "xfce", "cinnamon", "mate", "lxqt", "budgie", "deepin", "manjaro", "fedora", "debian", "ubuntu", "arch", "cachyos", "mint", "pacman", "apt",
        "dnf", "rpm", "ok", "status", "exe", "exec", "url", "id", "uuid", "json", "html", "pdf", "api", "sdk", "cli", "gui", "dir", "esq", "min", "max",
        "mhz", "ghz", "gb", "gib", "mb", "mib", "kb", "tb", "hz", "ms", "wh", "rapl", "tdp", "ppt", "oc", "xmp", "expo", "pbo", "aio", "led", "wayland",
        "xorg", "x11", "icu", "glibc", "libc", "lsb", "systemd", "polkit", "pkexec", "sudo", "root", "dmi", "smbios", "acpi", "hwmon", "sysfs", "procfs",
        "ubo", "spir", "glsl", "dxvk", "vkd3d", "esync", "fsync", "ntsync", "nvapi", "dlss", "fsr", "xess", "rt", "dxr", "ray", "query", "multilib", "aur",
        "appimage", "snap", "conky", "flameshot", "sunshine", "piper", "solaar", "discord", "chrome", "obs", "teams", "minecraft", "fortnite", "valorant",
        "dota", "apex", "tarkov", "cs2", "gta", "rog", "msi", "asus", "asrock", "gigabyte", "dell", "lenovo", "acer", "hp", "zotac", "evga", "galax",
        "gainward", "colorful", "sapphire", "xfx", "powercolor", "corsair", "kingston", "samsung", "crucial", "wd", "seagate", "toshiba", "sk", "hynix",
        "micron", "nouveau", "nvk", "i915", "xe", "amdgpu", "radv", "anv", "llvmpipe", "lavapipe", "softpipe", "swiftshader", "iris", "arc", "uhd", "hd",
        "navi", "rdna", "rdna2", "rdna3", "rdna4", "gcn", "turing", "ampere", "ada", "lovelace", "blackwell", "pascal", "maxwell", "kepler", "volta",
        "hopper", "zen", "zen2", "zen3", "zen4", "zen5", "ryzen", "threadripper", "epyc", "core", "xeon", "celeron", "pentium", "atom", "athlon", "ultra",
        "mobile", "ti", "super", "xt", "xtx", "gre", "laptop", "max-q", "notebook", "ghost", "wukong", "ragnarök", "lol", "tft"];

    private static readonly HashSet<string> english = new(StringComparer.Ordinal);

    [GeneratedRegex(@"\p{L}[\p{L}'-]*")]
    private static partial Regex WordRx();

    [GeneratedRegex(@"[à-öø-ÿÀ-ÖØ-ß]")]
    private static partial Regex Accent();

    private static IEnumerable<string> Words(string s) => WordRx().Matches(s).Select(m => m.Value.ToLowerInvariant());

    public static void Build()
    {
        english.UnionWith(Technical);
        foreach (var list in new[] { "/usr/share/dict/words", "/usr/share/dict/cracklib-small", "/usr/share/cracklib/cracklib-small" })
            if (File.Exists(list)) { english.UnionWith(File.ReadLines(list).Select(w => w.Trim().ToLowerInvariant())); break; }
        english.ExceptWith(Function);
    }

    public static bool HasWordList => english.Count > Technical.Length;

    /// <summary>Has an accented word, a Portuguese function word or (with the word list) a word of 3+ letters that is not English.</summary>
    public static bool IsPortuguese(string text) => Accent().IsMatch(text)
        || Words(text).Any(w => Function.Contains(w) || Portuguese.Contains(w) || HasWordList && w.Length >= 3 && !w.Contains('\'') && !english.Contains(w) && !english.Contains(w.TrimEnd('s')));
}
