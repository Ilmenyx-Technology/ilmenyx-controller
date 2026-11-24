// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>Library filter chips of the games-only window. Recent = games Ilmenyx recorded a last-played time for (newest first).</summary>
public enum GameFilter { All, Steam, Windows, Linux, Recent }

public static class GameFilters
{
    /// <summary>Steam library entries; Windows programs run with Proton/Wine; everything else (menu entries, native programs added by hand) is Linux.</summary>
    public static GameFilter Of(LibGame g) => g.IsSteam ? GameFilter.Steam : g.IsWindowsManual ? GameFilter.Windows : GameFilter.Linux;

    /// <param name="lastPlayed">Last-played time of a game (null = never recorded); only the Recent filter needs it.</param>
    public static bool Matches(LibGame g, GameFilter filter, Func<LibGame, DateTimeOffset?>? lastPlayed = null) =>
        filter == GameFilter.All || (filter == GameFilter.Recent ? lastPlayed?.Invoke(g) is not null : Of(g) == filter);

    /// <summary>The game played most recently (the window opens on it); null when none has a recorded time.</summary>
    public static LibGame? MostRecent(IEnumerable<LibGame> games, Func<LibGame, DateTimeOffset?> lastPlayed) =>
        games.Select(g => (g, at: lastPlayed(g))).Where(x => x.at is not null).OrderByDescending(x => x.at).Select(x => x.g).FirstOrDefault();
}
