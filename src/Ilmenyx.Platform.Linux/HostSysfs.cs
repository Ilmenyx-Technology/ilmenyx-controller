// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Collections.Concurrent;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Flatpak exposes only /sys/{block,bus,class,dev,devices}; /sys/module (loaded driver versions, taint) and /sys/firmware (UEFI, Secure Boot)
/// are hidden. Those few attributes are read on the host through <see cref="HostSpawn"/> and cached for 30 s (they rarely change). Natively unused.
/// </summary>
public static class HostSysfs
{
    private static readonly string[] Exposed = ["/sys/block", "/sys/bus", "/sys/class", "/sys/dev", "/sys/devices"];
    private static readonly ConcurrentDictionary<string, (DateTime At, string Value)> Cache = new(StringComparer.Ordinal);
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    /// <summary>Pure: a /sys path the Flatpak sandbox does not show.</summary>
    public static bool HiddenInFlatpak(string path) =>
        (path == "/sys" || path.StartsWith("/sys/", StringComparison.Ordinal)) && path != "/sys" && !Exposed.Any(e => path == e || path.StartsWith(e + "/", StringComparison.Ordinal));

    public static bool Hidden(string path) => HostSpawn.InFlatpak && HiddenInFlatpak(path);

    /// <summary>Attribute text read on the host ("" when missing or unreadable), bounded like <see cref="SysfsText.Read"/>.</summary>
    public static string Read(string path, int maxCharacters) => Cached("r:" + path, () =>
    {
        var (code, output) = Task.Run(() => BoundedProcess.RunAsync("head", ["-c", (maxCharacters + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "--", path], TimeSpan.FromSeconds(3), maxCharacters + 64)).GetAwaiter().GetResult();
        return code != 0 || output.Length > maxCharacters ? "" : new string(output.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim();
    });

    public static bool DirectoryExists(string path) => Cached("d:" + path, () =>
        Task.Run(() => BoundedProcess.RunAsync("test", ["-d", path], TimeSpan.FromSeconds(3), 64)).GetAwaiter().GetResult().ExitCode == 0 ? "1" : "") == "1";

    /// <summary>First bytes of a binary attribute (efivars), via od on the host; null when unreadable.</summary>
    public static byte[]? ReadBytes(string path, int count)
    {
        string text = Cached("b:" + path, () =>
        {
            var (code, output) = Task.Run(() => BoundedProcess.RunAsync("od", ["-An", "-v", "-tu1", "-N", count.ToString(System.Globalization.CultureInfo.InvariantCulture), "--", path], TimeSpan.FromSeconds(3), 4096)).GetAwaiter().GetResult();
            return code == 0 ? output : "";
        });
        var bytes = text.Split((char[])[' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(t => byte.TryParse(t, out byte b) ? (int)b : -1).ToList();
        return bytes.Count == 0 || bytes.Contains(-1) ? null : bytes.Select(b => (byte)b).ToArray();
    }

    private static string Cached(string key, Func<string> read)
    {
        if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < Ttl) return hit.Value;
        string value;
        try { value = read(); }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { value = ""; }
        Cache[key] = (DateTime.UtcNow, value);
        return value;
    }
}
