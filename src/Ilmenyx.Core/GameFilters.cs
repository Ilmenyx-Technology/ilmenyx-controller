// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>Library filter chips of the games-only window. There is no "Recentes": the library records no last-played time (only FPS sessions, which cover a few games).</summary>
public enum GameFilter { All, Steam, Windows, Linux }

public static class GameFilters
{
    /// <summary>Steam library entries; Windows programs run with Proton/Wine; everything else (menu entries, native programs added by hand) is Linux.</summary>
    public static GameFilter Of(LibGame g) => g.IsSteam ? GameFilter.Steam : g.IsWindowsManual ? GameFilter.Windows : GameFilter.Linux;

    public static bool Matches(LibGame g, GameFilter filter) => filter == GameFilter.All || Of(g) == filter;
}
