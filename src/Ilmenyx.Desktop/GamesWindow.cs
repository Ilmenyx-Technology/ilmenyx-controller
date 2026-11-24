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
    private static readonly (string Key, Func<string> Label)[] Tabs =
    [
        ("launcher", () => L.T("Jogos")), ("achievements", () => L.T("Conquistas")), ("compat", () => L.T("Compatibilidade")),
        ("gamecheck", () => L.T("Jogo não abre?")), ("canrun", () => L.T("Meu PC roda isso?")),
    ];
    private readonly Dictionary<string, Button> nav = new();
    private readonly Dictionary<string, Control> pages = new();
    private readonly Panel host = new() { Name = "GamesHost" };
    private Control searchHost = null!;
    public string CurrentTab { get; private set; } = "";

    /// <summary>Maps a page key asked by a page (GameCheckPage links) to a tab of this window; null for pages that only exist in the full app.</summary>
    public static string? TabFor(string key) => Tabs.Any(t => t.Key == key) ? key : null;

    /// <param name="pageFactory">Builds the library page (default: the real one). Settable for UI fixtures, which never touch the real library or launch games.</param>
    public GamesWindow(Func<Func<Window?>, CancellationToken, LauncherPage>? pageFactory = null)
    {
        if (IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).ThemeLight) Palette.Apply(true);
        Icon = AppIcon.Window; Title = L.T("Jogos — Ilmenyx Controller"); Width = 1180; Height = 780; MinWidth = 900; MinHeight = 560; Background = Palette.Bg;
        Page = pageFactory is null ? LauncherPage.Create(() => this, lifetime.Token, LauncherLayout.Showcase) : pageFactory(() => this, lifetime.Token);
        // Top bar: wordmark, one pill per page (the active one in the accent colour) and, on the Jogos page, the search field.
        var tabs = new WrapPanel { Name = "GamesNav", ItemSpacing = 6, LineSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(tabs, L.T("JOGOS"));
        foreach (var (key, label) in Tabs)
        {
            var b = new Button { Name = "GamesNav_" + key, Content = label(), FontSize = 13, FontWeight = FontWeight.Medium, Padding = new Thickness(15, 7), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderThickness = new Thickness(0) };
            AutomationProperties.SetName(b, label());
            string k = key; b.Click += (_, _) => Select(k);
            nav[key] = b; tabs.Children.Add(b);
        }
        tabs.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right) || TopLevel.GetTopLevel(tabs)?.FocusManager?.GetFocusedElement() is not Button f) return;
            var list = tabs.Children.OfType<Button>().ToList(); int i = list.IndexOf(f); if (i < 0) return;
            list[Math.Clamp(i + (e.Key == Key.Right ? 1 : -1), 0, list.Count - 1)].Focus(NavigationMethod.Directional); e.Handled = true;
        };
        var wordmark = new TextBlock { FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        wordmark.Inlines!.Add(new Avalonia.Controls.Documents.Run("Ilmenyx")); wordmark.Inlines.Add(new Avalonia.Controls.Documents.Run(" " + L.T("Jogos")) { Foreground = Palette.AccentText });
        searchHost = Page.SearchField; searchHost.Width = 230; searchHost.VerticalAlignment = VerticalAlignment.Center;
        var bar = new Grid { Name = "GamesTopBar", ColumnDefinitions = new("Auto,*,Auto"), RowDefinitions = new("Auto,Auto"), Margin = new Thickness(24, 10, 24, 10) };
        wordmark.Margin = new Thickness(0, 0, 22, 0); Grid.SetColumn(tabs, 1); Grid.SetColumn(searchHost, 2);
        bar.Children.Add(wordmark); bar.Children.Add(tabs); bar.Children.Add(searchHost);
        // Under ~1120 px the search drops to its own row instead of squeezing the pills.
        bar.SizeChanged += (_, e) =>
        {
            bool low = e.NewSize.Width < 1120;
            Grid.SetRow(searchHost, low ? 1 : 0); Grid.SetColumn(searchHost, low ? 0 : 2); Grid.SetColumnSpan(searchHost, low ? 3 : 1);
            searchHost.HorizontalAlignment = low ? HorizontalAlignment.Left : HorizontalAlignment.Right; searchHost.Margin = low ? new Thickness(0, 10, 0, 0) : new Thickness(12, 0, 0, 0);
        };
        var top = new Border { Name = "GamesTop", Background = Palette.Panel, BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
        pages["launcher"] = Page;
        host.Children.Add(Page);
        var grid = new Grid { RowDefinitions = new("Auto,*") };
        Grid.SetRow(host, 1); grid.Children.Add(top); grid.Children.Add(host);
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
        if (tab != "launcher") Page.CloseDrawer(restoreFocus: false); // the settings drawer belongs to the Jogos page
        searchHost.IsVisible = tab == "launcher";
        foreach (var (k, b) in nav) PaintTab(b, k == tab);
        CurrentTab = tab;
    }

    /// <summary>Active pill: accent fill with the on-accent text; the others muted, with the hover tint. The template's own hover colours are pinned too.</summary>
    private static void PaintTab(Button b, bool on)
    {
        IBrush bg = on ? Palette.Accent : Brushes.Transparent, fg = on ? Palette.OnAccent : Palette.Muted, hoverBg = on ? Palette.Accent : Palette.Hover, hoverFg = on ? Palette.OnAccent : Palette.Fg;
        b.Background = bg; b.Foreground = fg;
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" }) b.Resources[key] = hoverBg;
        foreach (var key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed" }) b.Resources[key] = hoverFg;
        b.Resources["ButtonBorderBrushPointerOver"] = Brushes.Transparent; b.Resources["ButtonBorderBrushPressed"] = Brushes.Transparent;
    }

    private Control Build(string key)
    {
        Control Scroll(string name, Control c) { c.Margin = new Thickness(0, 4, 8, 12); return new ScrollViewer { Name = name, Content = c, Margin = new Thickness(24, 12, 16, 0) }; }
        switch (key)
        {
            case "achievements": { var a = AchievementsPage.Create(lifetime.Token); a.Margin = new Thickness(0, 4, 8, 12); return new Border { Padding = new Thickness(24, 12, 16, 0), Child = a }; }
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
