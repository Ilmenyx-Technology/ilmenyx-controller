// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// Games-only window ("--games", game shortcuts with "--play KEY"): just the Jogos page. No monitor, sensors, tray icon or other pages
/// are created, so it opens fast and runs nothing in the background besides what the game being played needs.
/// </summary>
public sealed class GamesWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    public LauncherPage Page { get; }

    public GamesWindow()
    {
        if (IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).ThemeLight) Palette.Apply(true);
        Icon = AppIcon.Window; Title = L.T("Jogos — Ilmenyx Controller"); Width = 1180; Height = 780; MinWidth = 900; MinHeight = 560; Background = Palette.Bg;
        Page = LauncherPage.Create(() => this, lifetime.Token);
        Content = new Border { Padding = new Thickness(20, 16, 12, 0), Child = Page };
        Closed += (_, _) => lifetime.Cancel(); // a game being followed stops being followed; its temporary changes are undone
    }

    /// <summary>Brings the window back for a second launch (it may be minimised behind the game).</summary>
    public void Bring() { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
}
