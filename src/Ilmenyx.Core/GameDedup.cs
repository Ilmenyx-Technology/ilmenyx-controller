// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>One library card: the visible game plus the entries joined into it (same program), which stay in the settings untouched.</summary>
public sealed record GameGroup(LibGame Game, IReadOnlyList<LibGame> Merged);

/// <summary>Joins library entries that start the same program (a hand-added game and its menu entry, a portal alias and the real path). Display only.</summary>
public static class GameDedup
{
    private static int Rank(LibGame g) => g.Store == "Manual" ? 0 : g.Store == "Linux" ? 2 : 1;

    /// <summary>Normalized program path of an entry; null when it has none or it cannot be resolved (Flatpak launchers and unresolved aliases never join).</summary>
    public static string? ProgramKey(LibGame g, Func<string, string?> resolve)
    {
        string? exe = g.ExePath ?? (g.Store == "Manual" ? g.Id : null);
        if (string.IsNullOrEmpty(exe) || g.Kind?.Contains("Flatpak", StringComparison.Ordinal) == true) return null;
        try { return resolve(exe) is { Length: > 0 } real && Path.IsPathFullyQualified(real) ? Path.GetFullPath(real).TrimEnd('/') : null; }
        catch (ArgumentException) { return null; }
    }

    /// <param name="resolve">Real path of a program (portal alias to host path); null when it cannot be told.</param>
    /// <param name="score">Profile data of an entry (sessions, cover, achievements): more wins between hand-added duplicates.</param>
    public static IReadOnlyList<GameGroup> Group(IReadOnlyList<LibGame> all, Func<string, string?> resolve, Func<LibGame, int>? score = null)
    {
        var keys = all.Select(g => ProgramKey(g, resolve)).ToList();
        var result = new List<GameGroup>();
        var done = new HashSet<int>();
        for (int i = 0; i < all.Count; i++)
        {
            if (done.Contains(i)) continue;
            var members = new List<int> { i };
            if (keys[i] is { } key) members.AddRange(Enumerable.Range(i + 1, all.Count - i - 1).Where(j => keys[j] == key));
            // Menu entries (Linux) only join when a hand-added or store entry has the same program: two menu entries that share a launcher stay apart.
            if (members.Count > 1 && members.All(m => all[m].Store == "Linux")) members = [i];
            if (members.Count == 1) { done.Add(i); result.Add(new(all[i], [])); continue; }
            var ordered = members.OrderBy(m => Rank(all[m]))
                .ThenByDescending(m => resolve(all[m].ExePath ?? all[m].Id) == (all[m].ExePath ?? all[m].Id) ? 1 : 0) // real path before portal alias
                .ThenByDescending(m => score?.Invoke(all[m]) ?? 0).ThenByDescending(m => m).ToList(); // shortcut: no add date is stored, so the later entry in the list is taken as the newest
            foreach (var m in members) done.Add(m);
            result.Add(new(all[ordered[0]], ordered.Skip(1).Select(m => all[m]).ToList()));
        }
        return result.OrderBy(g => g.Game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Hidden keys where hiding any entry of a joined group hides the whole group (the visible key is added).</summary>
    public static HashSet<string> HiddenKeys(IEnumerable<string> hidden, IReadOnlyDictionary<string, IReadOnlyList<LibGame>> merged)
    {
        var set = new HashSet<string>(hidden);
        foreach (var (key, rest) in merged) if (rest.Any(m => set.Contains(m.Key))) set.Add(key);
        return set;
    }

    /// <summary>"Também aparece como" line of the game panel for the entries joined into it.</summary>
    public static string AlsoAppearsAs(IEnumerable<LibGame> merged) =>
        string.Join("; ", merged.Select(m => m.Store == "Linux" ? L.T($"{m.Name} (menu de aplicativos)") : L.T("outra entrada adicionada à mão")).Distinct());
}
