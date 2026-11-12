// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <param name="Wine">A plain Wine (runs the game directly); false = a Proton build (runs through umu).</param>
public sealed record GameRunner(string Name, string Path, bool Wine);

/// <summary>
/// The Proton and Wine builds a Windows game can be told to use (GameProfile.Runner): the ones Steam, umu, Bottles and Ilmenyx already
/// keep on this PC, plus any folder the user picks. Nothing is downloaded here.
/// </summary>
public static class GameRunners
{
    /// <summary>umu keyword: it downloads and uses the latest GE-Proton.</summary>
    public const string LatestGe = "GE-Proton";

    public static bool IsProton(string folder) => File.Exists(Path.Combine(folder, "proton"));

    /// <summary>A folder that can run a Windows game: a Proton build or a plain Wine.</summary>
    public static bool IsRunner(string folder) => Path.IsPathFullyQualified(folder) && (IsProton(folder) || PlainWine.IsUsable(folder));

    /// <summary>The <c>bin/wine</c> of a profile's runner when it is a plain Wine folder; null for automatic, GE and Proton builds.</summary>
    public static string? WineBin(string? runner) =>
        runner is { Length: > 0 } r && Path.IsPathFullyQualified(r) && PlainWine.IsUsable(r) ? Path.Combine(r, "bin", "wine") : null;

    /// <summary>Builds found on this PC, newest first, one per folder name (Steam's folder appears under two paths).</summary>
    public static IReadOnlyList<GameRunner> Find(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string steam = Path.Combine(home, ".local", "share", "Steam");
        string[] roots = [Path.Combine(steam, "compatibilitytools.d"), Path.Combine(home, ".steam", "root", "compatibilitytools.d"), Path.Combine(steam, "steamapps", "common"), .. PlainWine.RunnerRoots(home)];
        var found = new Dictionary<string, GameRunner>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.EnumerateDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    if (found.ContainsKey(name)) continue;
                    if (IsProton(dir)) found[name] = new(name, dir, false);
                    else if (PlainWine.IsUsable(dir)) found[name] = new(name, dir, true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return found.Values.OrderByDescending(r => PlainWine.VersionKey(r.Name), StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Why a folder cannot be a game's Wine prefix, or null when it can: it must be empty (or not exist yet) or already be a prefix.
    /// Wine fills the folder with a "C:" drive, so a folder with other files (Documents, a game folder) is refused.
    /// </summary>
    public static string? PrefixProblem(string folder, string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Path.IsPathFullyQualified(folder)) return L.T("Escolha uma pasta pelo caminho completo.");
        string full = Path.GetFullPath(folder).TrimEnd('/');
        if (full.Length == 0 || full == Path.GetFullPath(home).TrimEnd('/')) return L.T("Escolha uma pasta só para o jogo, não a pasta pessoal nem a raiz do sistema.");
        try
        {
            if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any() && !File.Exists(Path.Combine(full, "system.reg")))
                return L.T("Esta pasta já tem outros arquivos e não é um prefixo do Wine. Escolha uma pasta vazia ou um prefixo que já existe.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return L.T("Não foi possível ler esta pasta: ") + e.Message; }
        return null;
    }
}
