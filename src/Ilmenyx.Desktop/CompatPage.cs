// New Ilmenyx additions: see LICENSING.md.
// Game compatibility page: ProtonDB (summary + local index of the ODbL export), Steam Deck (Valve) and AreWeAntiCheatYet (MIT).
// Network only after a confirmation that names the hosts; SteamDB is only a link the user opens (it forbids automated access).
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed record CompatServices(CompatDatabase Db, CompatOnline Online, Func<CompatHardware> Hardware, Func<CancellationToken, Task<IReadOnlyList<LibGame>>> Library,
    Func<string, string?> GetLaunchOptions, Func<string, string, string?> SetLaunchOptions, Func<string, CancellationToken, Task> OpenUrl);

public sealed class CompatPage : StackPanel
{
    private readonly CompatServices services;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly TextBlock dbStatus = new() { Name = "CompatDbStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button dbUpdate = UiKit.Primary("CompatDbUpdate", "BAIXAR BASE DE RELATOS");
    private readonly Button dbCancel = new() { Name = "CompatDbCancel", Content = "Cancelar", IsVisible = false };
    private readonly TextBox search = new() { Name = "CompatSearch", PlaceholderText = "Buscar jogo pelo nome ou pelo ID da Steam (ex.: Elden Ring, 1245620)", MinWidth = 260 };
    private readonly WrapPanel results = new() { Name = "CompatResults" };
    private readonly WrapPanel library = new() { Name = "CompatLibrary" };
    private readonly StackPanel detail = new() { Name = "CompatDetail" };
    private readonly Dictionary<string, string?> undo = new(StringComparer.Ordinal);
    private CancellationTokenSource? updating, searching;
    private bool onlineAllowed;
    private HashSet<string> steamIds = new(StringComparer.Ordinal);
    public string? ShownAppId { get; private set; }

    public CompatPage(CompatServices services, Func<Window?> owner, CancellationToken lifetime)
    {
        this.services = services; this.owner = owner; this.lifetime = lifetime;
        Children.Add(new StackPanel { Margin = new Thickness(0, 4, 0, 18), Children = {
            new TextBlock { Text = "Compatibilidade", FontSize = 32, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "ProtonDB, Steam Deck e anti-cheat, com relatos de quem tem hardware parecido com o seu", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } });

        dbUpdate.Click += async (_, _) => await UpdateDatabaseAsync();
        dbCancel.Click += (_, _) => updating?.Cancel();
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = {
            UiKit.Label("BASE DE RELATOS DO PROTONDB"), dbStatus,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { dbUpdate, dbCancel } },
            Faint("Arquivo mensal oficial do ProtonDB (github.com/bdefore/protondb-data), conferido pelo hash publicado pelo GitHub e transformado num índice local. Depois disso a busca e os relatos funcionam sem internet.") } }));

        search.TextChanged += (_, _) => _ = SearchAsync(search.Text ?? "");
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { UiKit.Label("JOGO"), search, results, library } }));
        Children.Add(detail);
        Children.Add(Faint(CompatDatabase.Attribution + " · Steam Deck: Valve · Anti-cheat: AreWeAntiCheatYet (MIT). O SteamDB não permite acesso automatizado: o app só abre a página dele quando você clica."));
        RefreshDbStatus();
        AttachedToVisualTree += async (_, _) => await LoadLibraryAsync();
    }

    private static TextBlock Faint(string text) => new() { Text = text, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) };
    private static Border Pill(string text, IBrush fg, string? name = null) => new()
    {
        Name = name, BorderBrush = fg, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(10, 3), Margin = new Thickness(0, 0, 6, 6),
        HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = text, Foreground = fg, FontSize = 11, FontWeight = FontWeight.SemiBold },
    };

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
            library.Children.Add(new TextBlock { Text = "Sua biblioteca:", Foreground = Palette.Faint, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) });
            foreach (var g in games) library.Children.Add(GameButton(g.Id, g.Name, "CompatLib" + g.Id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private Button GameButton(string id, string title, string name)
    {
        var b = new Button { Name = name, Content = title, FontSize = 12, Padding = new Thickness(12, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
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

        var links = new WrapPanel();
        foreach (var (label, url) in new[] { ("no ProtonDB", $"https://www.protondb.com/app/{appId}"), ("no SteamDB", $"https://steamdb.info/app/{appId}/"), ("na Loja Steam", $"https://store.steampowered.com/app/{appId}/") })
        {
            var b = new Button { Name = "CompatLink" + label.Split(' ')[^1], Content = "Abrir " + label, FontSize = 11, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 6, 6) };
            b.Click += async (_, _) => { try { await services.OpenUrl(url, lifetime); } catch (Exception e) when (e is ArgumentException or IOException or TimeoutException or FileNotFoundException) { } };
            links.Children.Add(b);
        }
        detail.Children.Add(new StackPanel { Margin = new Thickness(4, 6, 0, 12), Children = {
            new TextBlock { Name = "CompatTitle", Text = title, FontSize = 26, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Steam " + appId + (steamIds.Contains(appId) ? " · instalado neste PC" : ""), Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 8) }, links } });

        // Three status tiles; filled by the online query.
        var tiles = new Grid { Name = "CompatTiles", ColumnDefinitions = new("*,12,*,12,*"), Margin = new Thickness(0, 0, 0, 14) };
        Border Tile(int col, string label, string name, out TextBlock value, out TextBlock note)
        {
            value = new TextBlock { Name = name, Text = "—", FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
            note = new TextBlock { Text = "consulte online", Foreground = Palette.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            var t = UiKit.Card(new StackPanel { Spacing = 4, Children = { UiKit.Label(label), value, note } }, new Thickness(0)); Grid.SetColumn(t, col); tiles.Children.Add(t); return t;
        }
        Tile(0, "PROTONDB", "CompatTier", out var tierValue, out var tierNote); Tile(2, "STEAM DECK", "CompatDeck", out var deckValue, out var deckNote); Tile(4, "ANTI-CHEAT", "CompatAntiCheat", out var acValue, out var acNote);
        detail.Children.Add(tiles);
        var query = UiKit.Primary("CompatOnlineQuery", "CONSULTAR ONLINE"); var queryStatus = new TextBlock { Name = "CompatOnlineStatus", Foreground = Palette.Faint, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        query.Click += async (_, _) =>
        {
            if (!onlineAllowed)
            {
                if (owner() is not { } w || !await ConfirmDialog.AskAsync(w, "Consulta online", "Consultar a compatibilidade deste jogo?\n\n• www.protondb.com (nota e número de relatos)\n• store.steampowered.com (Steam Deck)\n• raw.githubusercontent.com (lista de anti-cheat do AreWeAntiCheatYet)\n\nSó o ID do jogo é enviado. As respostas ficam guardadas por 7 dias. Vale para esta sessão.", "Consultar", "Cancelar")) return;
                onlineAllowed = true;
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
            queryStatus.Text = errors.Count == 0 ? "Consultado às " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) + " (respostas guardadas por 7 dias)." : string.Join(" · ", errors);
            query.IsEnabled = true;
        };
        detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 14), Children = { query, queryStatus } });

        var ranked = CompatMatcher.RankAll(reports, hw, DateTimeOffset.UtcNow);
        int sameGpu = ranked.Count(x => x.Matches.Contains("mesma GPU") || x.Matches.Contains("mesmo chip") || x.Matches.Contains("GPU da mesma série"));
        detail.Children.Add(UiKit.Card(new StackPanel { Spacing = 6, Children = {
            UiKit.Label("SUA MÁQUINA"), new TextBlock { Name = "CompatHardware", Text = $"{hw.GpuText} · {hw.Distro ?? "distro desconhecida"}" + (hw.NvidiaProprietary ? " · driver NVIDIA proprietário" : ""), TextWrapping = TextWrapping.Wrap },
            new TextBlock { Name = "CompatCounts", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Text = reports.Count == 0
                ? (services.Db.ReadMeta() is null ? "Baixe a base de relatos (acima) para ver o que funcionou para outras pessoas." : "Nenhum relato deste jogo na base local.")
                : $"{Count(reports.Count, "relato recente", "relatos recentes")} na base · {reports.Count(r => r.Works)} {(reports.Count(r => r.Works) == 1 ? "conseguiu" : "conseguiram")} jogar · {sameGpu} com a mesma GPU, série ou chip que a sua" } } }));
        if (reports.Count == 0) return;

        var (options, protons) = CompatMatcher.Suggest(ranked);
        if (options.Count > 0 || protons.Count > 0)
        {
            var box = new StackPanel { Spacing = 8, Children = { UiKit.Label("O QUE FUNCIONOU PARA QUEM TEM HARDWARE PARECIDO") } };
            if (options.Count > 0)
            {
                var chips = new WrapPanel { Name = "CompatSuggestedOptions" };
                foreach (var o in options) chips.Children.Add(Pill($"{o.Option}  ·  {o.Count} de {o.Of}", Palette.AccentText));
                box.Children.Add(new TextBlock { Text = "Opções de inicialização mais usadas", Foreground = Palette.Muted, FontSize = 12 }); box.Children.Add(chips);
            }
            if (protons.Count > 0)
            {
                var chips = new WrapPanel { Name = "CompatSuggestedProton" };
                foreach (var p in protons) chips.Children.Add(Pill($"{p.Option}  ·  {p.Count} de {p.Of}", Palette.Success));
                box.Children.Add(new TextBlock { Text = "Versões do Proton (escolha em Steam › Propriedades › Compatibilidade)", Foreground = Palette.Muted, FontSize = 12 }); box.Children.Add(chips);
            }
            box.Children.Add(Faint("Contagem entre relatos que deram certo, com GPU parecida ou do mesmo fabricante. Ponto de partida para testar, não garantia."));
            detail.Children.Add(UiKit.Card(box));
        }

        var list = new StackPanel { Name = "CompatReports", Spacing = 0 };
        detail.Children.Add(new TextBlock { Text = "RELATOS MAIS PARECIDOS COM A SUA MÁQUINA", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, Margin = new Thickness(4, 6, 0, 10) });
        int index = 0;
        foreach (var x in ranked.Take(12)) list.Children.Add(ReportCard(appId, x, index++));
        detail.Children.Add(list);
    }

    private static IBrush TierBrush(string? tier) => tier switch { "platinum" or "gold" or "native" => Palette.Success, "silver" => Palette.AccentText, "bronze" => Palette.Warn, "borked" => Palette.Error, _ => Palette.Muted };

    private Border ReportCard(string appId, RankedReport x, int index)
    {
        var r = x.Report;
        var head = new WrapPanel();
        head.Children.Add(Pill(r.Works ? "FUNCIONA" : "NÃO FUNCIONA", r.Works ? Palette.Success : Palette.Error));
        head.Children.Add(new TextBlock { Text = r.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Foreground = Palette.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 6) });
        foreach (var m in x.Matches) head.Children.Add(Pill(m, Palette.AccentText));
        var body = new StackPanel { Spacing = 6, Children = { head,
            new TextBlock { Text = $"{r.Gpu ?? "GPU não informada"}", FontSize = 13, TextWrapping = TextWrapping.Wrap },
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
            var code = new Border { Background = Palette.Panel, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 8),
                Child = new SelectableTextBlock { Name = "CompatOptions" + index, Text = lo, FontFamily = new FontFamily("monospace"), FontSize = 12, TextWrapping = TextWrapping.Wrap } };
            var copy = new Button { Name = "CompatCopy" + index, Content = "Copiar", FontSize = 11, Padding = new Thickness(10, 4) };
            var apply = new Button { Name = "CompatApply" + index, Content = "Aplicar na Steam", FontSize = 11, Padding = new Thickness(10, 4), IsEnabled = verdict.Applicable };
            var revert = new Button { Name = "CompatUndo" + index, Content = "Desfazer", FontSize = 11, Padding = new Thickness(10, 4), IsVisible = false };
            var status = new TextBlock { Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Text = verdict.Applicable ? "" : verdict.Reason ?? "" };
            ToolTip.SetTip(apply, verdict.Applicable ? "Grava estas opções no jogo, no localconfig.vdf da Steam (com backup). A Steam precisa estar fechada." : verdict.Reason);
            copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } c) { await c.SetTextAsync(lo); status.Text = "Copiado. Cole em Steam › Propriedades do jogo › Opções de inicialização."; } };
            apply.Click += async (_, _) =>
            {
                string? current = services.GetLaunchOptions(appId);
                if (owner() is not { } w || !await ConfirmDialog.AskAsync(w, "Aplicar na Steam", $"Gravar estas opções de inicialização no jogo?\n\n{lo}\n\nAtuais: {(string.IsNullOrWhiteSpace(current) ? "(nenhuma)" : current)}\n\nVieram de um relato anônimo do ProtonDB: revise antes. A Steam precisa estar fechada; o localconfig.vdf ganha backup (.ilmenyx.bak) e você pode desfazer aqui.", "Aplicar", "Cancelar")) return;
                if (services.SetLaunchOptions(appId, lo) is { } error) { status.Text = error; return; }
                undo[appId] = current; revert.IsVisible = true; status.Text = "Aplicado. Abra a Steam e inicie o jogo para testar.";
            };
            revert.Click += (_, _) =>
            {
                if (!undo.TryGetValue(appId, out var previous)) return;
                status.Text = services.SetLaunchOptions(appId, previous ?? "") ?? "Opções anteriores restauradas."; if (status.Text == "Opções anteriores restauradas.") { undo.Remove(appId); revert.IsVisible = false; }
            };
            body.Children.Add(code);
            body.Children.Add(new WrapPanel { Children = { copy, new Border { Width = 6 }, apply, new Border { Width = 6 }, revert, new Border { Width = 10 }, status } });
        }
        return UiKit.Card(body);
    }
}
