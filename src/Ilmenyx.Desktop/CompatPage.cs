// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Game compatibility page: ProtonDB (summary + local index of the ODbL export), Steam Deck (Valve) and AreWeAntiCheatYet (MIT).
// Network only after a confirmation that names the hosts; SteamDB is only a link the user opens (it forbids automated access).
// Cinematic layout since 2026-11-06: the open game is the hero (local Steam artwork when present), the three verdicts are stat tiles.
// 2026-11-07: hero art also from the Steam CDN (only after the online consent; cached on disk), and the similar-hardware suggestions are
// triaged (LaunchOptionTriage) into one conflict-free "Combinação recomendada" plus alternatives with a reason each.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed record CompatServices(CompatDatabase Db, CompatOnline Online, Func<CompatHardware> Hardware, Func<CancellationToken, Task<IReadOnlyList<LibGame>>> Library,
    Func<string, string?> GetLaunchOptions, Func<string, string, string?> SetLaunchOptions, Func<string, CancellationToken, Task> OpenUrl)
{
    /// <summary>Whether a program (gamemoderun, mangohud…) exists for Steam to run; default: the host's usual bin folders and PATH.</summary>
    public Func<string, bool>? ToolInstalled { get; init; }
    /// <summary>The Jogos profile of a Steam game (by AppID), to warn when its experimental options conflict with the recommended combination.</summary>
    public Func<string, GameProfile?>? Profile { get; init; }
}

public sealed class CompatPage : StackPanel
{
    private readonly CompatServices services;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly TextBlock dbStatus = new() { Name = "CompatDbStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button dbUpdate = UiKit.Primary("CompatDbUpdate", "BAIXAR BASE DE RELATOS");
    private readonly Button dbCancel = new() { Name = "CompatDbCancel", Content = "Cancelar", IsVisible = false };
    private readonly TextBox search = new() { Name = "CompatSearch", PlaceholderText = "Buscar jogo pelo nome ou pelo ID da Steam (ex.: Elden Ring, 1245620)", MinWidth = 260, Padding = new Thickness(34, 7, 10, 7) };
    private readonly WrapPanel results = new() { Name = "CompatResults", ItemSpacing = 6, LineSpacing = 6 };
    private readonly WrapPanel library = new() { Name = "CompatLibrary", ItemSpacing = 6, LineSpacing = 6 };
    private string? steamRoot; private bool steamRootRead;
    private readonly StackPanel detail = new() { Name = "CompatDetail" };
    private readonly Dictionary<string, string?> undo = new(StringComparer.Ordinal);
    private CancellationTokenSource? updating, searching;
    private bool onlineAllowed;
    private HashSet<string> steamIds = new(StringComparer.Ordinal);
    public string? ShownAppId { get; private set; }

    public CompatPage(CompatServices services, Func<Window?> owner, CancellationToken lifetime)
    {
        this.services = services; this.owner = owner; this.lifetime = lifetime;
        Children.Add(UiKit.PageHeader("Compatibilidade", "ProtonDB, Steam Deck e anti-cheat, com relatos de quem tem hardware parecido com o seu"));

        search.TextChanged += (_, _) => _ = SearchAsync(search.Text ?? "");
        var icon = UiKit.Icon(Icons.Search, 15, Palette.Faint); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); icon.IsHitTestVisible = false;
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { CardTitle(Icons.Games, "JOGO"), new Grid { Children = { search, icon } }, results, library } }));

        dbUpdate.Click += async (_, _) => await UpdateDatabaseAsync();
        dbCancel.Click += (_, _) => updating?.Cancel();
        dbStatus.FontSize = 12.5;
        var dbActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Children = { dbUpdate, dbCancel } };
        var dbRow = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new StackPanel { Spacing = 4, Children = { CardTitle(Icons.Report, "BASE DE RELATOS DO PROTONDB"), dbStatus } } } };
        Grid.SetColumn(dbActions, 1); dbRow.Children.Add(dbActions);
        Children.Add(UiKit.Card(new StackPanel { Spacing = 8, Children = { dbRow,
            Faint("Arquivo mensal oficial do ProtonDB (github.com/bdefore/protondb-data), conferido pelo hash publicado pelo GitHub e transformado num índice local. Depois disso a busca e os relatos funcionam sem internet.") } }));
        Children.Add(detail);
        Children.Add(Faint(CompatDatabase.Attribution + " · Steam Deck: Valve · Anti-cheat: AreWeAntiCheatYet (MIT) · Imagens dos jogos: Steam (Valve) e os estúdios. O SteamDB não permite acesso automatizado: o app só abre a página dele quando você clica."));
        RefreshDbStatus();
        AttachedToVisualTree += async (_, _) => await LoadLibraryAsync();
    }

    private static TextBlock Faint(string text) => new() { Text = text, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) };

    /// <summary>Status capsule: a soft tint of the state colour instead of a strong outline.</summary>
    private static Border Pill(string text, IBrush fg, string? name = null)
    {
        var c = ((ISolidColorBrush)fg).Color;
        return new Border
        {
            Name = name, Background = new SolidColorBrush(Color.FromArgb((byte)(Palette.IsLight ? 0x1C : 0x26), c.R, c.G, c.B)), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(10, 3), Margin = new Thickness(0, 0, 6, 6),
            HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = text, Foreground = fg, FontSize = 11, FontWeight = FontWeight.SemiBold },
        };
    }

    /// <summary>Caption over the feature gradient: the label style in the secondary ink (Faint is too weak on the gradient).</summary>
    private static TextBlock OnFeature(string text) { var t = UiKit.Label(text); t.Foreground = Palette.Muted; t.Margin = new Thickness(0, 4, 0, 0); return t; }

    private static StackPanel CardTitle(string icon, string text) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 2), Children = { UiKit.Icon(icon, 15, Palette.AccentText), UiKit.Label(text) },
    };

    private string? SteamRoot()
    {
        if (!steamRootRead) { steamRootRead = true; try { steamRoot = new Ilmenyx.Platform.Linux.SteamInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Root(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { steamRoot = null; } }
        return steamRoot;
    }

    private static string Count(int n, string one, string many) => n.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + " " + (n == 1 ? one : many);

    private void RefreshDbStatus()
    {
        var m = services.Db.ReadMeta();
        dbStatus.Text = m is null
            ? "Ainda não baixada. Sem ela, a página mostra só as consultas online (nota do ProtonDB, Steam Deck e anti-cheat)."
            : $"{Count(m.Apps, "jogo", "jogos")} · {Count(m.Reports, "relato", "relatos")} de {m.Oldest:MM/yyyy} a {m.Newest:dd/MM/yyyy} · arquivo {m.Source} · montada em {m.BuiltAt.ToLocalTime():dd/MM/yyyy}";
        dbUpdate.Content = m is null ? "BAIXAR BASE DE RELATOS" : "ATUALIZAR BASE";
    }

    private async Task UpdateDatabaseAsync()
    {
        if (owner() is not { } w || !await ConfirmDialog.AskAsync(w, "Base de relatos",
            "Baixar o arquivo de relatos mais recente do ProtonDB?\n\n• De: api.github.com e raw.githubusercontent.com (repositório bdefore/protondb-data)\n• Tamanho: cerca de 70 MB; vira um índice de ~30 MB em ~/.cache/ilmenyx/compat e o arquivo baixado é apagado\n• Licença dos dados: Open Database License (ODbL)\n• Nada sobre você ou seu PC é enviado.", "Baixar", "Cancelar")) return;
        updating = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        dbUpdate.IsEnabled = false; dbCancel.IsVisible = true;
        try
        {
            var progress = new Progress<string>(s => dbStatus.Text = s); // created on the UI thread: reports come back to it
            var token = updating.Token;
            await Task.Run(() => services.Online.UpdateDatabaseAsync(services.Db, progress, token, services.Hardware()));
            RefreshDbStatus(); if (ShownAppId is { } id) await ShowAsync(id, services.Db.Title(id) ?? id);
        }
        catch (OperationCanceledException) { dbStatus.Text = "Download cancelado. A base anterior (se havia) continua valendo."; }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException) { dbStatus.Text = "Não foi possível atualizar: " + e.Message + ". A base anterior (se havia) continua valendo."; }
        finally { dbUpdate.IsEnabled = true; dbCancel.IsVisible = false; updating?.Dispose(); updating = null; }
    }

    private async Task LoadLibraryAsync()
    {
        if (library.Children.Count > 0) return;
        try
        {
            var games = (await services.Library(lifetime)).Where(g => g.Store == "Steam" && g.Id.All(char.IsAsciiDigit)).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
            steamIds = games.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
            if (games.Count == 0) { library.Children.Add(new TextBlock { Text = "Nenhum jogo da Steam encontrado neste PC: use a busca.", Foreground = Palette.Faint, FontSize = 12 }); return; }
            library.Children.Add(new TextBlock { Text = "Sua biblioteca:", Foreground = Palette.Faint, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
            foreach (var g in games) library.Children.Add(GameButton(g.Id, g.Name, "CompatLib" + g.Id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private Button GameButton(string id, string title, string name)
    {
        var b = new Button { Name = name, Content = title, FontSize = 12, Padding = new Thickness(12, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
        b.Click += async (_, _) => await ShowAsync(id, title);
        return b;
    }

    private async Task SearchAsync(string text)
    {
        searching?.Cancel(); searching = new CancellationTokenSource(); var token = searching.Token;
        try { await Task.Delay(250, token); } catch (OperationCanceledException) { return; }
        var hits = text.Trim().Length < 2 ? [] : await Task.Run(() => services.Db.Search(text, 12), token);
        if (token.IsCancellationRequested) return;
        results.Children.Clear();
        foreach (var (id, title) in hits) results.Children.Add(GameButton(id, title, "CompatHit" + id));
        if (hits.Count == 0 && text.Trim().Length >= 2)
        {
            bool isId = text.Trim().All(char.IsAsciiDigit) && text.Trim().Length <= 12;
            results.Children.Add(isId ? GameButton(text.Trim(), "Steam " + text.Trim(), "CompatHitId")
                : new TextBlock { Text = services.Db.ReadMeta() is null ? "Sem a base local, busque pelo ID da Steam (número na URL da loja)." : "Nenhum jogo com esse nome na base.", Foreground = Palette.Faint, FontSize = 12 });
        }
    }

    public async Task ShowAsync(string appId, string title)
    {
        ShownAppId = appId; detail.Children.Clear();
        var reports = await Task.Run(() => services.Db.Reports(appId));
        var hw = await Task.Run(services.Hardware);
        if (ShownAppId != appId) return;

        var art = await Task.Run(() => GameArt.Steam(SteamRoot(), appId, hero: true) ?? LoadArt(services.Online.CachedArt(appId)));
        if (ShownAppId != appId) return;

        // Hero: the open game, with its identity and (after the online query) its verdicts as tags. Rebuilt when the artwork arrives.
        var tags = new List<(string? Name, string Text)> { (null, "Steam " + appId) };
        if (steamIds.Contains(appId)) tags.Add((null, "instalado neste PC"));
        if (reports.Count > 0) tags.Add((null, Count(reports.Count, "relato na base", "relatos na base")));
        var heroSlot = new Border { Margin = new Thickness(0, 4, 0, 12) };
        void RenderHero() => heroSlot.Child = Hero(appId, title, art, tags);
        RenderHero();
        detail.Children.Add(heroSlot);
        async Task FetchArtAsync()
        {
            if (art is not null || !onlineAllowed) return;
            try { var file = await services.Online.ArtAsync(appId, lifetime); var bmp = await Task.Run(() => LoadArt(file)); if (bmp is not null && ShownAppId == appId) { art = bmp; RenderHero(); } }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or ArgumentException) { } // the gradient placeholder stays
        }
        _ = FetchArtAsync();

        // Three verdict tiles; filled by the online query.
        var tiles = new Grid { Name = "CompatTiles", ColumnDefinitions = new("*,10,*,10,*"), Margin = new Thickness(0, 0, 0, 12) };
        void Tile(int col, string label, string icon, string name, out TextBlock value, out TextBlock note)
        {
            value = new TextBlock { Name = name, Text = "—", FontSize = 22, TextWrapping = TextWrapping.Wrap };
            note = new TextBlock { Text = "consulte online", Foreground = Palette.Faint };
            var t = UiKit.StatTile(label, value, note, icon, Palette.AccentText); Grid.SetColumn(t, col); tiles.Children.Add(t);
        }
        Tile(0, "PROTONDB", Icons.Compat, "CompatTier", out var tierValue, out var tierNote); Tile(2, "STEAM DECK", Icons.Games, "CompatDeck", out var deckValue, out var deckNote); Tile(4, "ANTI-CHEAT", Icons.Blocked, "CompatAntiCheat", out var acValue, out var acNote);
        detail.Children.Add(tiles);
        var query = UiKit.Primary("CompatOnlineQuery", "CONSULTAR ONLINE"); var queryStatus = new TextBlock { Name = "CompatOnlineStatus", Foreground = Palette.Faint, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        query.Click += async (_, _) =>
        {
            if (!onlineAllowed)
            {
                if (owner() is not { } w || !await ConfirmDialog.AskAsync(w, "Consulta online", "Consultar a compatibilidade deste jogo?\n\n• www.protondb.com (nota e número de relatos)\n• store.steampowered.com (Steam Deck)\n• raw.githubusercontent.com (lista de anti-cheat do AreWeAntiCheatYet)\n• shared.akamai.steamstatic.com (imagem do jogo, da Steam)\n\nSó o ID do jogo é enviado. As respostas ficam guardadas por 7 dias e as imagens por 30 dias. Vale para esta sessão.", "Consultar", "Cancelar")) return;
                onlineAllowed = true; _ = FetchArtAsync();
            }
            query.IsEnabled = false; queryStatus.Text = "Consultando…";
            var errors = new List<string>();
            try { var s = await services.Online.SummaryAsync(appId, lifetime); tierValue.Text = s is null ? "Sem relatos" : ProtonSummary.TierName(s.Tier); tierValue.Foreground = TierBrush(s?.Tier); tierNote.Text = s is null ? "ninguém relatou este jogo no ProtonDB" : $"{ProtonSummary.TierMeaning(s.Tier)} · {s.Total} relatos · confiança {ProtonSummary.ConfidenceName(s.Confidence)}" + (s.TrendingTier is { } tr && tr != s.Tier ? $" · tendência: {ProtonSummary.TierName(tr)}" : ""); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { errors.Add("ProtonDB: " + e.Message); }
            try { var d = await services.Online.DeckAsync(appId, lifetime); deckValue.Text = d?.Name ?? "—"; deckValue.Foreground = d?.Category switch { 3 => Palette.Success, 2 => Palette.Warn, 1 => Palette.Error, _ => Palette.Fg }; deckNote.Text = d is { Notes.Count: > 0 } ? string.Join(" · ", d.Notes.Take(3)) : "sem detalhes da Valve"; }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { errors.Add("Steam: " + e.Message); }
            try
            {
                var a = await services.Online.AntiCheatAsync(appId, lifetime);
                acValue.Text = a is null ? "Não listado" : a.Status switch { "Supported" => "Suportado", "Running" => "Roda", "Planned" => "Planejado", "Broken" => "Quebrado", "Denied" => "Negado", _ => a.Status };
                acValue.Foreground = a is null ? Palette.Muted : a.BlocksOnline ? Palette.Error : Palette.Success;
                acNote.Text = a is null ? "o jogo não aparece na lista (comum em jogos sem anti-cheat online)" : a.StatusName + (a.AntiCheats.Count > 0 ? " · " + string.Join(", ", a.AntiCheats) : "");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { errors.Add("Anti-cheat: " + e.Message); }
            foreach (var (label, value) in new[] { ("ProtonDB", tierValue.Text), ("Deck", deckValue.Text), ("Anti-cheat", acValue.Text) })
                if (value is { Length: > 0 } v && v != "—" && !tags.Any(t => t.Name == "CompatHeroTag" + label)) tags.Add(("CompatHeroTag" + label, label + ": " + v));
            if (ShownAppId == appId) RenderHero();
            queryStatus.Text = errors.Count == 0 ? "Consultado às " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) + " (respostas guardadas por 7 dias)." : string.Join(" · ", errors);
            query.IsEnabled = true;
        };
        detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 14), Children = { query, queryStatus } });

        var ranked = CompatMatcher.RankAll(reports, hw, DateTimeOffset.UtcNow);
        int sameGpu = ranked.Count(x => x.Matches.Contains("mesma GPU") || x.Matches.Contains("mesmo chip") || x.Matches.Contains("GPU da mesma série"));
        detail.Children.Add(UiKit.Card(new StackPanel { Spacing = 6, Children = {
            CardTitle(Icons.System, "SUA MÁQUINA"), new TextBlock { Name = "CompatHardware", Text = $"{hw.GpuText} · {hw.Distro ?? "distro desconhecida"}" + (hw.NvidiaProprietary ? " · driver NVIDIA proprietário" : ""), TextWrapping = TextWrapping.Wrap },
            new TextBlock { Name = "CompatCounts", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Text = reports.Count == 0
                ? (services.Db.ReadMeta() is null ? "Baixe a base de relatos (acima) para ver o que funcionou para outras pessoas." : "Nenhum relato deste jogo na base local.")
                : $"{Count(reports.Count, "relato recente", "relatos recentes")} na base · {reports.Count(r => r.Works)} {(reports.Count(r => r.Works) == 1 ? "conseguiu" : "conseguiram")} jogar · {sameGpu} com a mesma GPU, série ou chip que a sua" } } }));
        if (reports.Count == 0) return;

        var (options, protons) = CompatMatcher.Suggest(ranked, 30);
        if (options.Count > 0 || protons.Count > 0)
        {
            var triage = LaunchOptionTriage.Triage(options, protons, hw, services.ToolInstalled ?? HostTools.Installed);
            detail.Children.Add(Worked(appId, triage));
        }

        var list = new StackPanel { Name = "CompatReports", Spacing = 0 };
        detail.Children.Add(UiKit.SectionHeader("Relatos mais parecidos com a sua máquina", new TextBlock { Text = "ordenados por semelhança de hardware", Foreground = Palette.Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center }));
        int index = 0;
        foreach (var x in ranked.Take(12)) list.Children.Add(ReportCard(appId, x, index++));
        detail.Children.Add(list);
    }

    /// <summary>Banner of the open game: the artwork under a legibility scrim, or the brand gradient with a large faint monogram as placeholder.</summary>
    private Border Hero(string appId, string title, Bitmap? art, IReadOnlyList<(string? Name, string Text)> tagList)
    {
        bool onImage = art is not null;
        var links = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (label, url) in new[] { ("no ProtonDB", $"https://www.protondb.com/app/{appId}"), ("no SteamDB", $"https://steamdb.info/app/{appId}/"), ("na Loja Steam", $"https://store.steampowered.com/app/{appId}/") })
        {
            var b = onImage ? UiKit.Translucent("CompatLink" + label.Split(' ')[^1], "Abrir " + label) : new Button { Name = "CompatLink" + label.Split(' ')[^1], Content = "Abrir " + label, Background = Palette.Card };
            b.FontSize = 11.5; b.Padding = new Thickness(12, 5);
            b.Click += async (_, _) => { try { await services.OpenUrl(url, lifetime); } catch (Exception e) when (e is ArgumentException or IOException or TimeoutException or FileNotFoundException) { } };
            links.Children.Add(b);
        }
        var heroTags = new WrapPanel { Name = "CompatHeroTags" };
        foreach (var (name, text) in tagList) heroTags.Children.Add(UiKit.Tag(text, onImage: onImage, name: name));
        var heroBottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = {
            new TextBlock { Name = "CompatTitle", Text = title, FontSize = 26, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = onImage ? Brushes.White : Palette.Fg },
            links } };
        var heroContent = new Grid { RowDefinitions = new("Auto,*,Auto"), Children = { heroTags } };
        Grid.SetRow(heroBottom, 2); heroContent.Children.Add(heroBottom);
        var hero = UiKit.Hero(heroContent, onImage ? new ImageBrush { Source = art, Stretch = Stretch.UniformToFill } : null, height: onImage ? 236 : 200, name: "CompatHero");
        if (hero.Child is Grid layers)
        {
            if (onImage) layers.Children.Insert(0, new Border { IsHitTestVisible = false, Background = GameArt.Scrim() });
            else
            {
                // Placeholder: the game's initials and the games icon, faint, on the right (where the artwork's subject usually is).
                var mark = new StackPanel { Name = "CompatHeroPlaceholder", IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 34, 0), Opacity = Palette.IsLight ? 0.16 : 0.12,
                    Children = { UiKit.Icon(Icons.Games, 54, Palette.Fg), new TextBlock { Text = GameArt.Initials(title), FontSize = 58, FontWeight = FontWeight.Bold, LetterSpacing = 2, Foreground = Palette.Fg, HorizontalAlignment = HorizontalAlignment.Center } } };
                layers.Children.Insert(1, mark);
            }
        }
        ToolTip.SetTip(hero, onImage ? "Imagem do jogo: Steam (Valve) e o estúdio do jogo." : "Sem imagem: ela aparece se o jogo estiver na sua Steam ou depois de CONSULTAR ONLINE.");
        return hero;
    }

    private static Bitmap? LoadArt(string? path)
    {
        if (path is null) return null;
        try { using var stream = File.OpenRead(path); return Bitmap.DecodeToWidth(stream, 1280, BitmapInterpolationMode.MediumQuality); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException) { return null; }
    }

    /// <summary>"O que funcionou…": the conflict-free combination (applicable as one line), the Proton version, and the alternatives with their reasons.</summary>
    private Border Worked(string appId, LaunchTriage t)
    {
        var box = new StackPanel { Spacing = 8, Children = {
            new TextBlock { Text = "O que funcionou para quem tem hardware parecido", FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Juntamos as opções que mais deram certo em PCs parecidos com o seu e tiramos as que brigam entre si ou que não servem para a sua placa de vídeo.", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap } } };
        box.Children.Add(OnFeature("COMBINAÇÃO RECOMENDADA"));
        if (t.Recommended.Count > 0 && t.CommandLine is { } line)
        {
            var chips = new WrapPanel { Name = "CompatRecommended" };
            foreach (var o in t.Recommended) chips.Children.Add(UiKit.Tag($"{o.Option}  ·  {o.Count} de {o.Of}", Palette.Fg));
            box.Children.Add(chips);
            box.Children.Add(new Border { Background = Palette.Hover, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 9),
                Child = new SelectableTextBlock { Name = "CompatRecommendedLine", Text = line, FontFamily = new FontFamily("monospace"), FontSize = 12, TextWrapping = TextWrapping.Wrap } });
            box.Children.Add(Actions(appId, line, "Recommended", LaunchOptionSafety.Check(line), "Combinação montada pelo Ilmenyx a partir de relatos anônimos do ProtonDB, sem opções que conflitam entre si: revise antes."));
            // Experimental options of this game's Jogos profile, judged by the same conflict table.
            if (services.Profile?.Invoke(appId) is { } prof && (prof.ProtonWayland || prof.LowLatency))
            {
                var hits = GameExtras.AgainstRecommended(prof, t.Recommended.Select(o => o.Option));
                box.Children.Add(new TextBlock { Name = "CompatExtrasNote", FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap,
                    Text = hits.Count == 0 ? "As opções experimentais ligadas no perfil deste jogo (página Jogos) combinam com esta recomendação."
                        : "Não combinam com esta recomendação (desligue no perfil do jogo, na página Jogos, se for usá-la): " + string.Join(" ", hits.Select(h => $"{h.Label}: {h.Reason}")) });
            }
        }
        else box.Children.Add(new TextBlock { Name = "CompatRecommendedNone", Text = "Nenhuma opção de inicialização serve para este PC sem conflito; o jogo pode rodar sem nenhuma. Veja as alternativas e os relatos abaixo.", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        if (t.Proton is { } p)
        {
            var proton = new TextBlock { Name = "CompatRecommendedProton", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            proton.Inlines!.Add(new Avalonia.Controls.Documents.Run("Proton: ") { FontWeight = FontWeight.SemiBold });
            proton.Inlines.Add(new Avalonia.Controls.Documents.Run($"{p.Option} ({p.Count} de {p.Of}) "));
            proton.Inlines.Add(new Avalonia.Controls.Documents.Run("· escolha em Steam › Propriedades do jogo › Compatibilidade") { Foreground = Palette.Muted });
            box.Children.Add(proton);
        }
        var alternatives = t.Alternatives.Concat(t.OtherProtons.Select(o => o.Option.Contains("Proton", StringComparison.OrdinalIgnoreCase) ? o : o with { Option = "Proton " + o.Option })).Take(10).ToList();
        if (alternatives.Count > 0)
        {
            box.Children.Add(OnFeature("ALTERNATIVAS (NÃO COMBINAR COM AS ACIMA)"));
            var list = new StackPanel { Name = "CompatAlternatives", Spacing = 6 };
            foreach (var a in alternatives)
            {
                var row = new Grid { ColumnDefinitions = new("Auto,*") };
                var chip = UiKit.Tag($"{a.Option}  ·  {a.Count} de {a.Of}", Palette.Muted); chip.Margin = new Thickness(0, 0, 10, 0); chip.VerticalAlignment = VerticalAlignment.Top; chip.MaxWidth = 320;
                var why = new TextBlock { Text = a.Reason, Foreground = Palette.Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(why, 1); row.Children.Add(chip); row.Children.Add(why); list.Children.Add(row);
            }
            box.Children.Add(list);
        }
        box.Children.Add(new TextBlock { Text = "Contagem entre relatos que deram certo, com GPU parecida ou do mesmo fabricante. Ponto de partida para testar, não garantia.", Foreground = Palette.Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        var feature = UiKit.FeatureCard(box, "CompatWorked"); feature.Margin = new Thickness(0, 0, 0, 12);
        return feature;
    }

    /// <summary>Copy / Aplicar na Steam (confirmation showing the current options, backup) / Desfazer for one launch line.</summary>
    private WrapPanel Actions(string appId, string lo, string suffix, LaunchOptionSafety.Verdict verdict, string source)
    {
        var copy = new Button { Name = "CompatCopy" + suffix, Content = "Copiar", FontSize = 11, Padding = new Thickness(10, 4) };
        var apply = UiKit.Primary("CompatApply" + suffix, "Aplicar na Steam", 12, 4); apply.FontSize = 11; apply.IsEnabled = verdict.Applicable;
        var revert = new Button { Name = "CompatUndo" + suffix, Content = "Desfazer", FontSize = 11, Padding = new Thickness(10, 4), IsVisible = false };
        var status = new TextBlock { Name = "CompatStatus" + suffix, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Text = verdict.Applicable ? "" : verdict.Reason ?? "" };
        ToolTip.SetTip(apply, verdict.Applicable ? "Grava estas opções no jogo, no localconfig.vdf da Steam (com backup). A Steam precisa estar fechada." : verdict.Reason);
        copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } c) { await c.SetTextAsync(lo); status.Text = "Copiado. Cole em Steam › Propriedades do jogo › Opções de inicialização."; } };
        apply.Click += async (_, _) =>
        {
            string? current = services.GetLaunchOptions(appId);
            if (owner() is not { } w || !await ConfirmDialog.AskAsync(w, "Aplicar na Steam", $"Gravar estas opções de inicialização no jogo?\n\n{lo}\n\nAtuais: {(string.IsNullOrWhiteSpace(current) ? "(nenhuma)" : current)}\n\n{source} A Steam precisa estar fechada; o localconfig.vdf ganha backup (.ilmenyx.bak) e você pode desfazer aqui.", "Aplicar", "Cancelar")) return;
            if (services.SetLaunchOptions(appId, lo) is { } error) { status.Text = error; return; }
            undo[appId] = current; revert.IsVisible = true; status.Text = "Aplicado. Abra a Steam e inicie o jogo para testar.";
        };
        revert.Click += (_, _) =>
        {
            if (!undo.TryGetValue(appId, out var previous)) return;
            status.Text = services.SetLaunchOptions(appId, previous ?? "") ?? "Opções anteriores restauradas."; if (status.Text == "Opções anteriores restauradas.") { undo.Remove(appId); revert.IsVisible = false; }
        };
        return new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { copy, apply, revert, status } };
    }

    private static IBrush TierBrush(string? tier) => tier switch { "platinum" or "gold" or "native" => Palette.Success, "silver" => Palette.AccentText, "bronze" => Palette.Warn, "borked" => Palette.Error, _ => Palette.Muted };

    private Border ReportCard(string appId, RankedReport x, int index)
    {
        var r = x.Report;
        var head = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(Pill(r.Works ? "FUNCIONA" : "NÃO FUNCIONA", r.Works ? Palette.Success : Palette.Error));
        head.Children.Add(new TextBlock { Text = r.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Foreground = Palette.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 6) });
        foreach (var m in x.Matches) head.Children.Add(Pill(m, Palette.AccentText));
        var body = new StackPanel { Spacing = 6, Children = { head,
            new TextBlock { Text = $"{r.Gpu ?? "GPU não informada"}", FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = $"{r.Os ?? "distro não informada"} · Proton {(r.Proton.Length > 0 ? r.Proton : "padrão da Steam")}" + (r.Kernel is { } k ? " · kernel " + k : ""), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap } } };
        foreach (var f in r.Faults)
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            line.Inlines!.Add(new Avalonia.Controls.Documents.Run("Problema · " + CompatReport.FaultName(f)) { Foreground = Palette.Warn, FontWeight = FontWeight.SemiBold });
            if (r.FaultNotes.TryGetValue(f, out var n)) line.Inlines.Add(new Avalonia.Controls.Documents.Run(" — " + n));
            body.Children.Add(line);
        }
        foreach (var note in new[] { r.VerdictNote, r.ConcludingNotes }.Where(n => n is not null))
            body.Children.Add(new TextBlock { Text = note, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        if (r.LaunchOptions is { } lo)
        {
            var verdict = LaunchOptionSafety.Check(lo);
            var code = new Border { Background = Palette.Hover, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 9), Margin = new Thickness(0, 4, 0, 2),
                Child = new SelectableTextBlock { Name = "CompatOptions" + index, Text = lo, FontFamily = new FontFamily("monospace"), FontSize = 12, TextWrapping = TextWrapping.Wrap } };
            body.Children.Add(code);
            body.Children.Add(Actions(appId, lo, index.ToString(CultureInfo.InvariantCulture), verdict, "Vieram de um relato anônimo do ProtonDB: revise antes."));
        }
        return UiKit.Card(body);
    }
}

/// <summary>Programs Steam can run from launch options: the host's usual bin folders (through /run/host inside Flatpak) and, outside Flatpak, PATH.</summary>
internal static class HostTools
{
    public static bool Installed(string tool)
    {
        if (tool.Length == 0 || tool.Contains('/')) return false;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dirs = new List<string> { "/usr/bin", "/usr/local/bin", "/bin", "/usr/games", Path.Combine(home, ".local", "bin") };
        if (!Ilmenyx.Platform.Linux.HostSpawn.InFlatpak) dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries));
        try { return dirs.Any(d => File.Exists(Ilmenyx.Platform.Linux.HostSpawn.HostPath(Path.Combine(d, tool)))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
