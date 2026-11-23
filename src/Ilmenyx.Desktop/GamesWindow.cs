// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// Games-only window ("--games", game shortcuts with "--play KEY"): the JOGOS pages except the bottleneck detector (it needs the usage monitor). No monitor, sensors, tray icon or other pages
/// are created, so it opens fast and runs nothing in the background besides what the game being played needs.
/// </summary>
public sealed class GamesWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    public LauncherPage Page { get; }
    public const string DefaultTab = "launcher";
    private static readonly (string Key, Func<string> Label, string Icon)[] Tabs =
    [
        ("launcher", () => L.T("Jogos"), Icons.Games), ("achievements", () => L.T("Conquistas"), Icons.Achievements), ("compat", () => L.T("Compatibilidade"), Icons.Compat),
        ("gamecheck", () => L.T("Jogo não abre?"), Icons.Validate), ("canrun", () => L.T("Meu PC roda isso?"), Icons.CanRun),
    ];
    private readonly Dictionary<string, Button> nav = new();
    private readonly Dictionary<string, Control> pages = new();
    private readonly Panel host = new() { Name = "GamesHost" };
    public string CurrentTab { get; private set; } = "";

    /// <summary>Maps a page key asked by a page (GameCheckPage links) to a tab of this window; null for pages that only exist in the full app.</summary>
    public static string? TabFor(string key) => Tabs.Any(t => t.Key == key) ? key : null;

    public GamesWindow()
    {
        if (IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).ThemeLight) Palette.Apply(true);
        Icon = AppIcon.Window; Title = L.T("Jogos — Ilmenyx Controller"); Width = 1180; Height = 780; MinWidth = 900; MinHeight = 560; Background = Palette.Bg;
        Page = LauncherPage.Create(() => this, lifetime.Token);
        var side = new StackPanel { Name = "GamesNav", Margin = new Thickness(8, 16, 4, 8) };
        AutomationProperties.SetName(side, L.T("JOGOS"));
        foreach (var (key, label, icon) in Tabs)
        {
            var text = new TextBlock { Text = label(), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var ic = UiKit.Icon(icon, 17, Palette.Faint); ic.Margin = new Thickness(0, 0, 10, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { ic, text } };
            var b = new Button
            {
                Name = "GamesNav_" + key, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8, 10, 8), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), BorderThickness = new Thickness(0), Margin = new Thickness(0, 1), Background = Brushes.Transparent,
            };
            AutomationProperties.SetName(b, label());
            string k = key; b.Click += (_, _) => Select(k);
            nav[key] = b; side.Children.Add(b);
        }
        side.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down) || TopLevel.GetTopLevel(side)?.FocusManager?.GetFocusedElement() is not Button f) return;
            var list = side.Children.OfType<Button>().ToList(); int i = list.IndexOf(f); if (i < 0) return;
            list[Math.Clamp(i + (e.Key == Key.Down ? 1 : -1), 0, list.Count - 1)].Focus(); e.Handled = true;
        };
        var sideScroll = new ScrollViewer { Content = side, Width = 214, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        pages["launcher"] = new Border { Padding = new Thickness(12, 16, 12, 0), Child = Page };
        host.Children.Add(pages["launcher"]);
        var grid = new Grid { ColumnDefinitions = new("Auto,*") };
        grid.Children.Add(sideScroll); Grid.SetColumn(host, 1); grid.Children.Add(host);
        Content = grid;
        Select(DefaultTab);
        LauncherPage.GuardClose(this, () => Page);
        Closed += (_, _) => lifetime.Cancel(); // a game being followed stops being followed; its temporary changes are undone
    }

    /// <summary>Shows a tab, creating its page on first use (like the full app). Unknown keys are ignored.</summary>
    public void Select(string key)
    {
        if (TabFor(key) is not { } tab) return;
        if (!pages.ContainsKey(tab)) { var page = Build(tab); pages[tab] = page; host.Children.Add(page); }
        foreach (var (k, p) in pages) p.IsVisible = k == tab;
        foreach (var (k, b) in nav) { bool on = k == tab; b.Foreground = on ? Palette.Accent : Palette.Muted; b.Background = on ? Palette.Card : Brushes.Transparent; }
        CurrentTab = tab;
    }

    private Control Build(string key)
    {
        Control Scroll(string name, Control c) { c.Margin = new Thickness(0, 4, 8, 12); return new ScrollViewer { Name = name, Content = c, Margin = new Thickness(12, 16, 0, 0) }; }
        switch (key)
        {
            case "achievements": { var a = AchievementsPage.Create(lifetime.Token); a.Margin = new Thickness(0, 4, 8, 12); return new Border { Padding = new Thickness(12, 16, 0, 0), Child = a }; }
            case "compat": return Scroll("CompatScroll", new CompatPage(GamePages.CompatServicesNow(), () => this, lifetime.Token));
            case "gamecheck":
                return Scroll("GameCheckScroll", new GameCheckPage(OperatingSystem.IsLinux() ? new GameCheckServices(new GameLaunchProbe().CollectAsync, GameUserFixRunner.RunAsync) /* its links go to Otimiza/Driver NVIDIA, full-app pages: null hides them instead of dead buttons */ : throw new PlatformNotSupportedException(), () => this, lifetime.Token));
            default:
                // CPU model and RAM come straight from /proc (no usage monitor in this window); the GPU/disk facts use the same collectors as the shell.
                return Scroll("CanRunScroll", new CanRunPage(CanRunServices.Real(() => GamePages.LocalPcNow(LinuxMemoryInfo.CpuModel(), LinuxMemoryInfo.TotalBytes()), GamePages.SteamLibraryNow), lifetime.Token));
        }
    }

    /// <summary>Brings the window back for a second launch (it may be minimised behind the game).</summary>
    public void Bring() { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
}
