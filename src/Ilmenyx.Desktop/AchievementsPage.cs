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
    private readonly StackPanel list = new() { Name = "AchList", Spacing = 6 };
    private readonly StackPanel detail = new() { Name = "AchDetail", Spacing = 8 };
    private readonly TextBlock summary = new() { Name = "AchSummary", Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly List<AchievementView> views = [];
    public IReadOnlyList<AchievementView> Views => views;
    private AchievementView? shown;

    public static AchievementsPage Create(CancellationToken lifetime)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var library = new LinuxGameLibrary(home);
        var online = new CompatOnline(CompatPaths.Folder());
        var roots = library.SteamRoots().ToList();
        return new AchievementsPage(
            async ct =>
            {
                var settings = IlmenyxSettings.Load(IlmenyxSettings.DefaultPath());
                var games = await library.ScanAsync(settings.CustomGames.ToList(), ct);
                return games.Select(g => AchievementFiles.Find(g, settings.GameProfiles.GetValueOrDefault(g.Key), roots)).OfType<AchievementSource>().ToList();
            },
            (s, ct) => AchievementFiles.LoadAsync(s, roots, Achievements.SteamLanguage(L.Current), online.AchievementsAsync, ct),
            online.AchievementIconAsync, lifetime);
    }

    public AchievementsPage(Func<CancellationToken, Task<IReadOnlyList<AchievementSource>>> sources, LoadFunc load, Func<string, string, CancellationToken, Task<string?>> icon, CancellationToken lifetime)
    {
        this.sources = sources; this.load = load; this.icon = icon; this.lifetime = lifetime;
        ColumnDefinitions = new("300,20,*"); RowDefinitions = new("Auto,*");
        var refresh = new Button { Name = "BtnAchRefresh", Content = L.T("Atualizar"), Padding = new Thickness(14, 8), FontSize = 12 };
        refresh.Click += async (_, _) => await RefreshAsync();
        var header = UiKit.PageHeader(L.T("Conquistas"), null, refresh);
        ((StackPanel)header.Children[0]).Children.Add(summary);
        Grid.SetColumnSpan(header, 3); Children.Add(header);
        var left = new ScrollViewer { Content = list, Padding = new Thickness(0, 0, 8, 0) }; Grid.SetRow(left, 1); Children.Add(left);
        var right = new ScrollViewer { Content = detail, Padding = new Thickness(0, 0, 8, 12) }; Grid.SetRow(right, 1); Grid.SetColumn(right, 2); Children.Add(right);
        Loaded += async (_, _) => { if (views.Count == 0) await RefreshAsync(); };
    }

    public async Task RefreshAsync()
    {
        summary.Text = L.T("Procurando jogos com conquistas…"); list.Children.Clear(); detail.Children.Clear(); views.Clear();
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
        int total = views.Sum(v => v.Defs.Count), got = views.Sum(v => v.Unlocked);
        summary.Text = views.Count == 0 ? L.T("Nenhum jogo com conquistas encontrado.")
            : L.T($"{views.Count} jogos · {got} de {total} conquistas");
        if (views.Count == 0)
            detail.Children.Add(UiKit.Card(new TextBlock { Name = "AchEmpty", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 13,
                Text = L.T("Aparecem aqui os jogos da Steam que a Steam já guardou neste PC e os jogos de Windows adicionados ao Ilmenyx que trazem o emulador da Steam (steam_emu.ini, steam_settings). Jogue uma vez pelo Ilmenyx para as conquistas começarem a contar.") }));
    }

    private Button Row(AchievementView v)
    {
        double share = v.Defs.Count == 0 ? 0 : (double)v.Unlocked / v.Defs.Count;
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = share, Height = 4, MinHeight = 4, Margin = new Thickness(0, 6, 0, 0) };
        var b = new Button
        {
            Name = "Ach_" + v.Source.AppId, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 10),
            Content = new StackPanel { Children = {
                new TextBlock { Text = v.Source.Game.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = L.T($"{v.Unlocked} de {v.Defs.Count}") + " · " + Origin(v.Source), FontSize = 11.5, Foreground = Palette.Muted }, bar } },
        };
        AutomationProperties.SetName(b, v.Source.Game.Name + ": " + L.T($"{v.Unlocked} de {v.Defs.Count} conquistas"));
        b.Click += (_, _) => Show(v);
        return b;
    }

    private static string Origin(AchievementSource s) => s.Origin == AchievementOrigin.Steam ? "Steam" : L.T("Ilmenyx");

    private void Show(AchievementView v)
    {
        shown = v; detail.Children.Clear();
        string where = v.Source.Origin == AchievementOrigin.Steam ? L.T("Conquistas registradas pela Steam neste PC.")
            : L.T("Cópia sem Steam: o Ilmenyx lê as conquistas que o jogo grava e avisa quando uma sai enquanto você joga por aqui.");
        detail.Children.Add(new TextBlock { Name = "AchGameTitle", Text = v.Source.Game.Name, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        detail.Children.Add(new TextBlock { Text = L.T($"{v.Unlocked} de {v.Defs.Count} conquistas ({(v.Defs.Count == 0 ? 0 : 100.0 * v.Unlocked / v.Defs.Count):0}%)") + " · " + where, Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        var culture = CultureInfo.GetCultureInfo(L.Current);
        // Unlocked first (newest on top), then the rest in the game's order.
        foreach (var d in v.Defs.OrderBy(d => v.Unlocks.ContainsKey(d.Id) ? 0 : 1).ThenByDescending(d => v.Unlocks.GetValueOrDefault(d.Id)?.At))
            detail.Children.Add(Item(v.Source.AppId, d, v.Unlocks.GetValueOrDefault(d.Id), culture));
    }

    private Control Item(string appId, AchievementDef d, AchievementUnlock? u, CultureInfo culture)
    {
        bool got = u is not null, secret = d.Hidden && !got;
        var picture = new Image { Width = 48, Height = 48, Opacity = got ? 1 : 0.45 };
        var frame = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = Palette.Hover, Child = picture, VerticalAlignment = VerticalAlignment.Top };
        string file = got ? d.Icon : d.IconGray is { Length: > 0 } gray ? gray : d.Icon;
        if (file.Length > 0) _ = LoadIconAsync(appId, file, picture);
        string when = got ? u!.At is { } at ? L.T("Desbloqueada em ") + at.ToLocalTime().ToString("d", culture) : L.T("Desbloqueada")
            : d.GlobalPercent is { } pct ? L.T($"{pct.ToString("0.#", culture)}% dos jogadores têm") : "";
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Spacing = 2, Children = {
            new TextBlock { Text = secret ? L.T("Conquista secreta") : d.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = got ? Palette.Fg : Palette.Muted },
            new TextBlock { Text = secret ? L.T("Continue jogando para descobrir.") : d.Description, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } };
        if (when.Length > 0) text.Children.Add(new TextBlock { Text = when, FontSize = 11, Foreground = got ? Palette.Success : Palette.Faint });
        var row = new DockPanel { Children = { frame, text } }; DockPanel.SetDock(frame, Dock.Left);
        var card = UiKit.Card(row, new Thickness(0)); card.Name = "AchItem_" + d.Id; card.Padding = new Thickness(12, 10);
        AutomationProperties.SetName(card, (secret ? L.T("Conquista secreta") : d.Name) + (got ? " — " + L.T("desbloqueada") : " — " + L.T("bloqueada")));
        return card;
    }

    private async Task LoadIconAsync(string appId, string file, Image target)
    {
        try { if (await icon(appId, file, lifetime) is { } path && GameArt.FromFile(path, false) is { } bmp) target.Source = bmp; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { } // no icon: the empty frame stays
    }
}
