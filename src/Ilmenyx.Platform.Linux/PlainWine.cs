// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Platform.Linux;

/// <summary>
/// A plain Wine build (not Proton) for installers. A large multi-stage installer failed its own CRC check in every Proton tried
/// (UMU-Proton 10, GE-Proton 11, GE-Proton9-27; with and without fsync, from C: and D:) at the same point, while the same files
/// installed with Bottles' Soda 11, run outside any Steam container (owner's tests, 2026-11-08). This finds such a Wine: one the
/// Ilmenyx keeps in ~/.local/share/ilmenyx/runners/wine-*, or a runner Bottles already downloaded (soda, caffe, kron4ek, wine-ge).
/// </summary>
public static class PlainWine
{
    private static readonly string[] BottlesRunnerKinds = ["soda", "caffe", "kron4ek", "wine-ge", "lutris"];

    /// <summary>Folders searched, best first.</summary>
    public static IEnumerable<string> RunnerRoots(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(ClassicProton.Root(Path.Combine(home, ".local", "share")));
        yield return Path.Combine(home, ".var", "app", "com.usebottles.bottles", "data", "bottles", "runners");
        yield return Path.Combine(home, ".local", "share", "bottles", "runners");
    }

    /// <summary>The <c>bin/wine</c> of the newest usable plain Wine, or null.</summary>
    public static string? Find(string? home = null)
    {
        int rootIndex = 0;
        foreach (var root in RunnerRoots(home))
        {
            bool own = rootIndex++ == 0;
            if (!Directory.Exists(root)) continue;
            var candidates = Directory.EnumerateDirectories(root)
                .Where(d => { string n = Path.GetFileName(d).ToLowerInvariant(); return own ? n.StartsWith("wine-", StringComparison.Ordinal) : BottlesRunnerKinds.Any(k => n.StartsWith(k, StringComparison.Ordinal)); })
                .Where(IsUsable)
                .OrderByDescending(d => VersionKey(Path.GetFileName(d)), StringComparer.Ordinal);
            if (candidates.FirstOrDefault() is { } best) return Path.Combine(best, "bin", "wine");
        }
        return null;
    }

    /// <summary>A plain Wine folder: bin/wine and bin/wineserver, and no Proton script (Protons go through umu instead).</summary>
    public static bool IsUsable(string folder) =>
        File.Exists(Path.Combine(folder, "bin", "wine")) && File.Exists(Path.Combine(folder, "bin", "wineserver")) && !File.Exists(Path.Combine(folder, "proton"));

    /// <summary>Sort key where numbers compare by value ("soda-11.0" after "soda-9.0").</summary>
    private static string VersionKey(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(8, '0'));

    /// <summary>Short name shown to the user, e.g. "soda-11.0-10".</summary>
    public static string Name(string wineBin) => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(wineBin)!)!);
}
