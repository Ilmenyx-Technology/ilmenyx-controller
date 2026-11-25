// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// Conquistas: every game on this PC whose achievements Ilmenyx can read. Steam games show what Steam recorded; Windows copies that run
/// without Steam show what their Steam emulator saved in the game's Wine prefix, in the same layout. Read-only: nothing is ever unlocked here.
/// </summary>
public sealed class AchievementsPage : Grid
{
    public delegate Task<AchievementView> LoadFunc(AchievementSource source, CancellationToken ct);
    private readonly Func<CancellationToken, Task<IReadOnlyList<AchievementSource>>> sources;
    private readonly LoadFunc load;
    private readonly Func<string, string, CancellationToken, Task<string?>> icon;
    private readonly CancellationToken lifetime;
    private readonly StackPanel list = new() { Name = "AchList", Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly StackPanel detail = new() { Name = "AchDetail", Spacing = 8 };
    private readonly Func<AchievementSource, Bitmap?>? art;
    private readonly Dictionary<string, Border> frames = [];
    private readonly TextBlock summary = new() { Name = "AchSummary", Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly List<AchievementView> views = [];
    public IReadOnlyList<AchievementView> Views => views;
    private AchievementView? shown;
    private readonly Button refresh;
    private Button? previewButton; private AchievementView? previewView;
    private readonly Func<IReadOnlyList<LibGame>>? noEmulator;

    public static AchievementsPage Create(CancellationToken lifetime)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var library = new LinuxGameLibrary(home);
        var online = new CompatOnline(CompatPaths.Folder());
        var roots = library.SteamRoots().ToList();
        IReadOnlyList<LibGame> original = [];
        return new AchievementsPage(
            async ct =>
            {
                var settings = IlmenyxSettings.Load(IlmenyxSettings.DefaultPath());
                var games = await library.ScanAsync(settings.CustomGames.ToList(), ct);
                var found = games.Select(g => (g, s: AchievementFiles.Find(g, settings.GameProfiles.GetValueOrDefault(g.Key), roots))).ToList();
                original = found.Where(x => x.s is null && x.g.IsWindowsManual && x.g.ExePath is { } e && AchievementFiles.HasOriginalSteamApiOnly(AchievementFiles.GameFolder(e))).Select(x => x.g).ToList();
                return found.Select(x => x.s).OfType<AchievementSource>().ToList();
            },
            (s, ct) => AchievementFiles.LoadAsync(s, roots, Achievements.SteamLanguage(L.Current), online.AchievementsAsync, ct),
            online.AchievementIconAsync, lifetime, () => original,
            // Same art sources as the library: the cover the user set, Steam's local cache, then the Compatibility page's art cache (all local; no request here).
            src => GameArt.FromFile(IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).GameProfiles.GetValueOrDefault(src.Game.Key)?.CoverPath, false)
                ?? roots.Select(r => GameArt.Steam(r, src.AppId, false)).FirstOrDefault(b => b is not null) ?? GameArt.FromFile(online.CachedArt(src.AppId), false));
    }

    public AchievementsPage(Func<CancellationToken, Task<IReadOnlyList<AchievementSource>>> sources, LoadFunc load, Func<string, string, CancellationToken, Task<string?>> icon, CancellationToken lifetime, Func<IReadOnlyList<LibGame>>? noEmulator = null, Func<AchievementSource, Bitmap?>? art = null)
    {
        this.noEmulator = noEmulator; this.art = art;
        this.sources = sources; this.load = load; this.icon = icon; this.lifetime = lifetime;
        RowDefinitions = new("Auto,Auto,*");
        refresh = new Button { Name = "BtnAchRefresh", Content = L.T("Atualizar"), Padding = new Thickness(14, 8), FontSize = 12, CornerRadius = new CornerRadius(IlmenyxTheme.Pill) };
        refresh.Click += async (_, _) => await RefreshAsync();
        var header = UiKit.PageHeader(L.T("Conquistas"), null, refresh);
        ((StackPanel)header.Children[0]).Children.Add(summary);
        Children.Add(header);
        // Console-style shelf: one card per game in a row that scrolls sideways, the selected game's trophies below it.
        list.Margin = new Thickness(2, 4, 2, 14);
        var shelf = new ScrollViewer { Name = "AchShelf", Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(shelf, 1); Children.Add(shelf);
        var right = new ScrollViewer { Content = detail, Padding = new Thickness(0, 0, 8, 12) }; Grid.SetRow(right, 2); Children.Add(right);
        Loaded += async (_, _) => { if (views.Count == 0) await RefreshAsync(); };
        AttachedToVisualTree += (_, _) => { RunningGames.Changed += SyncPreview; SyncPreview(); };
        DetachedFromVisualTree += (_, _) => RunningGames.Changed -= SyncPreview;
    }

    /// <summary>"Ver aviso" is only for a game that Ilmenyx opened and that is still running.</summary>
    private void SyncPreview()
    {
        if (previewButton is null || previewView is null) return;
        bool on = RunningGames.Contains(previewView.Source.Game.Key);
        previewButton.IsEnabled = on && !previewBusy;
        ToolTip.SetTip(previewButton, on ? L.T("Mostra o aviso de conquista em 3 segundos, para você conferir por cima do jogo.") : L.T("Abra o jogo pelo Ilmenyx para ver o aviso."));
    }
    private bool previewBusy;

    public async Task RefreshAsync()
    {
        if (!refresh.IsEnabled) return; // a scan is already running
        refresh.IsEnabled = false;
        try { await ScanAsync(); }
        finally { refresh.IsEnabled = true; }
    }

    private async Task ScanAsync()
    {
        previewButton = null; previewView = null;
        summary.Text = L.T("Procurando jogos com conquistas…"); list.Children.Clear(); detail.Children.Clear(); views.Clear(); frames.Clear();
        IReadOnlyList<AchievementSource> found;
        try { found = await sources(lifetime); }
        catch (OperationCanceledException) { return; }
        foreach (var s in found)
        {
            AchievementView v;
            try { v = await load(s, lifetime); } catch (OperationCanceledException) { return; }
            if (v.Defs.Count == 0) continue;
            views.Add(v); list.Children.Add(Row(v));
            if (shown is null || shown.Source.Game.Key == v.Source.Game.Key) Show(v);
        }
        var plain = noEmulator?.Invoke() ?? [];
        foreach (var g in plain) list.Children.Add(PlainRow(g));
        int total = views.Sum(v => v.Defs.Count), got = views.Sum(v => v.Unlocked);
        summary.Text = views.Count == 0 && plain.Count == 0 ? L.T("Nenhum jogo com conquistas encontrado.")
            : L.T($"{views.Count} jogos · {got} de {total} conquistas");
        if (views.Count == 0 && plain.Count == 0)
            detail.Children.Add(UiKit.Card(new TextBlock { Name = "AchEmpty", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 13,
                Text = L.T("Aparecem aqui os jogos da Steam que a Steam já guardou neste PC e os jogos de Windows adicionados ao Ilmenyx que trazem o emulador da Steam (steam_emu.ini, steam_settings). Jogue uma vez pelo Ilmenyx para as conquistas começarem a contar.") }));
    }

    /// <summary>Whole-number percentage for the rings: never rounds up to 100 while something is missing.</summary>
    public static int Percent(int got, int total) => total <= 0 ? 0 : got >= total ? 100 : Math.Clamp((int)(100.0 * got / total), 0, 99);

    /// <summary>"última há 3 dias" from the newest unlock time; null when the source recorded no times (never guessed).</summary>
    public static string? LastUnlockText(IEnumerable<DateTimeOffset?> times, DateTimeOffset now)
    {
        var known = times.Where(t => t is not null).Select(t => t!.Value).ToList();
        if (known.Count == 0) return null;
        var last = known.Max();
        var age = now - last; if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        return age.TotalHours < 1 ? L.T($"última há {(int)age.TotalMinutes} min")
            : age.TotalHours < 48 ? L.T($"última há {(int)age.TotalHours} h")
            : L.T($"última há {(int)age.TotalDays} dias"); // from 48 h on: always 2 or more
    }

    /// <summary>Gold ring (the amber token) with the percentage in the middle.</summary>
    private static Grid Ring(int percent, double size, double font)
    {
        var ring = new Grid { Width = size, Height = size, Children = { new RingGauge(percent / 100.0, Palette.Warn, size), // Viewbox: with enlarged text the percentage shrinks to stay inside the ring instead of spilling over it.
            new Viewbox { Width = size * 0.64, Height = size * 0.34, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = percent + "%", FontSize = font, FontWeight = FontWeight.Bold } } } };
        AutomationProperties.SetName(ring, percent + "%");
        return ring;
    }

    private static Border Pill(string icon, string text, IBrush fg, string? name = null)
    {
        var c = ((ISolidColorBrush)fg).Color;
        var pill = new Border
        {
            Name = name, Background = new SolidColorBrush(Color.FromArgb((byte)(Palette.IsLight ? 0x1C : 0x26), c.R, c.G, c.B)), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(12, 5), Margin = new Thickness(0, 0, 8, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { UiKit.Icon(icon, 14, fg), new TextBlock { Text = text, Foreground = fg, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } } },
        };
        AutomationProperties.SetName(pill, text);
        return pill;
    }

    /// <summary>One game of the shelf: its art with the progress ring over a corner, the name and "X de Y". The selected game gets a white frame.</summary>
    private Button Row(AchievementView v)
    {
        int percent = Percent(v.Unlocked, v.Defs.Count);
        var face = GameArt.Cover(art?.Invoke(v.Source), v.Source.Game.Name, 112);
        var badge = new Border { Background = HeroScrim.Glass(), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, 8), Child = Ring(percent, 38, 10) };
        var frame = new Border { CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius + 5), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Padding = new Thickness(2), Child = new Grid { Children = { face, badge } } };
        frames[v.Source.AppId] = frame;
        var b = new Button
        {
            Name = "Ach_" + v.Source.AppId, Width = 212, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel { Children = { frame,
                new TextBlock { Text = v.Source.Game.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 6, 4, 0) },
                new TextBlock { Text = L.T($"{v.Unlocked} de {v.Defs.Count}") + " · " + Origin(v.Source), FontSize = 11.5, Foreground = Palette.Muted, Margin = new Thickness(4, 0, 4, 0) } } },
        };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" }) b.Resources[key] = Brushes.Transparent;
        foreach (var key in new[] { "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" }) b.Resources[key] = Brushes.Transparent;
        AutomationProperties.SetName(b, v.Source.Game.Name + ": " + L.T($"{v.Unlocked} de {v.Defs.Count} conquistas"));
        b.Click += (_, _) => Show(v);
        return b;
    }

    private void Mark(string? appId) { foreach (var (id, f) in frames) f.BorderBrush = id == appId ? Palette.Fg : Brushes.Transparent; }

    private Button PlainRow(LibGame g)
    {
        var b = new Button
        {
            Name = "AchPlain_" + g.Key, Width = 212, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel { Children = { new Border { Padding = new Thickness(4), Child = GameArt.Cover(null, g.Name, 112) },
                new TextBlock { Text = g.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 2, 4, 0) },
                new TextBlock { Text = L.T("Sem conquistas nesta cópia"), FontSize = 11.5, Foreground = Palette.Muted, Margin = new Thickness(4, 0, 4, 0) } } },
        };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" }) b.Resources[key] = Brushes.Transparent;
        foreach (var key in new[] { "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" }) b.Resources[key] = Brushes.Transparent;
        b.Click += (_, _) =>
        {
            shown = null; previewButton = null; previewView = null; detail.Children.Clear(); Mark(null);
            detail.Children.Add(new TextBlock { Name = "AchGameTitle", Text = g.Name, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            detail.Children.Add(new TextBlock { Name = "AchNoEmulator", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 13,
                Text = L.T("Esta cópia não registra conquistas: ela usa a biblioteca original da Steam, sem emulador, e só conta conquistas com a Steam aberta e o jogo na sua conta. O Ilmenyx não instala emuladores.") });
        };
        return b;
    }

    private static string Origin(AchievementSource s) => s.Origin == AchievementOrigin.Steam ? "Steam" : L.T("Ilmenyx");

    private void Show(AchievementView v)
    {
        shown = v; detail.Children.Clear(); Mark(v.Source.AppId);
        string where = v.Source.Origin == AchievementOrigin.Steam ? L.T("Conquistas registradas pela Steam neste PC.")
            : L.T("Cópia sem Steam: o Ilmenyx lê as conquistas que o jogo grava e avisa quando uma sai enquanto você joga por aqui.");
        var preview = new Button { Name = "BtnAchPreview", Content = L.T("Ver aviso"), Padding = new Thickness(14, 8), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(IlmenyxTheme.Pill) };
        previewButton = preview; previewView = v; SyncPreview();
        preview.Click += async (_, _) => await PreviewAsync(v, preview);
        int total = v.Defs.Count, got = v.Unlocked;
        var info = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        info.Children.Add(new TextBlock { Name = "AchGameTitle", Text = v.Source.Game.Name, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        info.Children.Add(new TextBlock { Name = "AchGameCount", Text = L.T($"{got} de {total} conquistas"), FontSize = 13, Foreground = Palette.Muted });
        // Only when the source recorded unlock times (Steam's stats cache, CODEX/RUNE, Goldberg/GSE do; an unlock without a time adds none).
        if (LastUnlockText(v.Unlocks.Values.Select(u => u.At), DateTimeOffset.Now) is { } last) info.Children.Add(new TextBlock { Name = "AchGameLast", Text = last, FontSize = 12, Foreground = Palette.Faint });
        info.Children.Add(new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Children = { Pill(Icons.Trophy, L.T($"{got} obtidas"), Palette.Warn, "AchPillGot"), Pill(Icons.Lock, L.T($"{total - got} a obter"), Palette.Muted, "AchPillLeft") } });
        info.Children.Add(preview);
        info.Children.Add(new TextBlock { Text = where, Foreground = Palette.Faint, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        var head = new Grid { ColumnDefinitions = new("Auto,*"), Children = { Ring(Percent(got, total), 104, 21), info } };
        Grid.SetColumn(info, 1);
        detail.Children.Add(UiKit.Card(head, new Thickness(0, 0, 0, 8)));
        var culture = CultureInfo.GetCultureInfo(L.Current);
        // Unlocked first (newest on top), then the rest in the game's order; cards wrap into as many columns as fit.
        var grid = new WrapPanel { Name = "AchGrid", ItemSpacing = 10, LineSpacing = 10 };
        foreach (var d in v.Defs.OrderBy(d => v.Unlocks.ContainsKey(d.Id) ? 0 : 1).ThenByDescending(d => v.Unlocks.GetValueOrDefault(d.Id)?.At))
            grid.Children.Add(Item(v.Source.AppId, d, v.Unlocks.GetValueOrDefault(d.Id), culture));
        detail.Children.Add(grid);
    }

    private Control Item(string appId, AchievementDef d, AchievementUnlock? u, CultureInfo culture)
    {
        bool got = u is not null, secret = d.Hidden && !got;
        // No picture (yet): a trophy on the amber surface for what was obtained, a lock on a neutral one for what was not.
        var glyph = UiKit.Icon(got ? Icons.Trophy : Icons.Lock, 30, got ? Palette.Warn : Palette.Faint); glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
        var picture = new Image { Width = 64, Height = 64, Opacity = got ? 1 : 0.45 };
        var frame = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = got ? Palette.WarnBg : Palette.Hover, Child = new Grid { Children = { glyph, picture } }, VerticalAlignment = VerticalAlignment.Top };
        string file = secret ? "" : got ? d.Icon : d.IconGray is { Length: > 0 } gray ? gray : d.Icon;
        if (file.Length > 0) _ = LoadIconAsync(appId, file, picture, glyph);
        string when = got ? u!.At is { } at ? L.T("Obtida em ") + at.ToLocalTime().ToString(at.ToLocalTime().Year == DateTime.Now.Year ? "dd/MM" : "dd/MM/yyyy", culture) : L.T("Obtida")
            : !secret && d.GlobalPercent is { } pct ? L.T($"{pct.ToString("0.#", culture)}% dos jogadores têm") : "";
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = {
            new TextBlock { Text = secret ? L.T("Conquista secreta") : d.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = got ? Palette.Fg : Palette.Muted },
            new TextBlock { Text = secret ? L.T("Continue jogando para descobrir.") : d.Description, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis } } };
        if (when.Length > 0) text.Children.Add(new TextBlock { Text = when, FontSize = 11.5, Foreground = got ? Palette.Warn : Palette.Faint });
        var row = new DockPanel { Children = { frame, text } }; DockPanel.SetDock(frame, Dock.Left);
        var card = UiKit.Card(row, new Thickness(0)); card.Name = "AchItem_" + d.Id; card.Padding = new Thickness(14, 12); card.Width = 330; card.MinHeight = 100;
        if (!got) card.Opacity = 0.9;
        AutomationProperties.SetName(card, (secret ? L.T("Conquista secreta") : d.Name) + (got ? " — " + L.T("desbloqueada") : " — " + L.T("bloqueada")));
        return card;
    }

    private async Task PreviewAsync(AchievementView v, Button button)
    {
        if (v.Defs.Count == 0 || !RunningGames.Contains(v.Source.Game.Key)) return;
        previewBusy = true; button.IsEnabled = false;
        try
        {
            await Task.Delay(3000, lifetime);
            if (!RunningGames.Contains(v.Source.Game.Key)) return; // the game closed meanwhile
            var d = v.Defs.FirstOrDefault(x => v.Unlocks.ContainsKey(x.Id)) ?? v.Defs[0];
            string? picture = null;
            try { if (d.Icon.Length > 0) picture = await icon(v.Source.AppId, d.Icon, lifetime); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { }
            if (!AchievementToast.TryShow(d.Name, d.Description, v.Source.Game.Name, picture, () => _ = DesktopNotification.PlaySoundAsync()))
            {
                _ = DesktopNotification.PlaySoundAsync();
                await DesktopNotification.SendAsync(L.T("Conquista desbloqueada"), d.Name + (d.Description.Length > 0 ? " — " + d.Description : "") + "\n" + v.Source.Game.Name, lifetime, picture, urgent: true);
            }
        }
        catch (OperationCanceledException) { }
        finally { previewBusy = false; if (ReferenceEquals(previewButton, button)) SyncPreview(); else button.IsEnabled = true; }
    }

    private async Task LoadIconAsync(string appId, string file, Image target, Control glyph)
    {
        try { if (await icon(appId, file, lifetime) is { } path && GameArt.FromFile(path, false) is { } bmp) { target.Source = bmp; glyph.IsVisible = false; } }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { } // no icon: the empty frame stays
    }
}
