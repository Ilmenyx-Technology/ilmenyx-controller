// New Ilmenyx additions (layout follows the original RESET JOGOS launcher: game list left, profile cards right; cinematic library grid and hero since 2026-11-06): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class LauncherPage : Grid
{
    public delegate Task<FpsSession?> LaunchFunc(LibGame game, GameProfile profile, string gpuVendor, Action<string> status, Action<string> learnedExe, CancellationToken ct);

    private readonly LinuxGameLibrary library;
    private readonly SteamInfo steam;
    private readonly string settingsPath;
    private readonly LaunchFunc launch;
    private readonly Func<Window?> owner;
    private readonly string gpuVendor;
    private readonly bool multiGpu;
    private readonly CancellationToken lifetime;
    private readonly IlmenyxSettings settings;
    private IReadOnlyList<LibGame> games = [];
    private LibGame? current; private GameProfile? profile;
    private CancellationTokenSource? launching;
    private bool loading;
    private readonly TextBlock count = new() { Name = "LGameCount", Text = "Procurando seus jogos…", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Name = "LGameSearch", PlaceholderText = "Buscar jogo…" };
    private readonly StackPanel list = new() { Name = "LGameList", Spacing = 8 };
    private readonly TextBlock empty = new() { Name = "LEmpty", Text = "Escolha um jogo na lista.", Foreground = Palette.Muted };
    private readonly ScrollViewer detail = new() { Name = "LDetail", IsVisible = false };
    private readonly TextBlock title = new() { Name = "LTitle", FontSize = 32, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock sub = new() { Name = "LSub", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button play = UiKit.Primary("BtnPlay", "JOGAR", 40, 14);
    private readonly TextBlock status = new() { Name = "LStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly CheckBox chkPlan = new() { Name = "LChkPlan", Content = "Perfil de energia Alto desempenho enquanto joga (volta ao anterior depois)" };
    private readonly CheckBox chkGpu = new() { Name = "LChkGpu", Content = "Forçar a placa de vídeo dedicada (PRIME offload)" };
    private readonly CheckBox chkGameMode = new() { Name = "LChkGameMode", Content = "GameMode (gamemoderun)" };
    private readonly CheckBox chkMango = new() { Name = "LChkMango", Content = "MangoHud (overlay de desempenho)" };
    private readonly TextBlock exeText = new() { Name = "LExeText", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox exePath = new() { Name = "LExePath", PlaceholderText = "/caminho/completo/do/executavel" };
    private readonly TextBox extra = new() { Name = "LExtra", PlaceholderText = "ex.: -novid -high +fps_max 0" };
    private readonly TextBlock preview = new() { Name = "LPreview", FontFamily = new FontFamily("monospace"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBlock steamNow = new() { Name = "LSteamNow", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel steamBox = new() { Name = "LSteamBox", IsVisible = false };
    private readonly TextBlock gfxNote = new() { Name = "LGfxNote", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel helpers = new() { Name = "LHelpers" };
    private readonly TextBox helperPath = new() { Name = "LHelperPath", PlaceholderText = "/caminho/completo/do/programa" };
    private readonly CheckBox helperClose = new() { Name = "LHelperClose", Content = "Fechar junto com o jogo", IsChecked = true };
    private readonly StackPanel addBox = new() { Name = "LAddBox", IsVisible = false, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border emptyBox = new() { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(24), MinHeight = 232, VerticalAlignment = VerticalAlignment.Top };
    private readonly WrapPanel heroTags = new() { Name = "LHeroTags" };
    private readonly Border heroScrim = new() { IsHitTestVisible = false, IsVisible = false, Background = GameArt.Scrim() };
    private Border hero = null!;
    private bool heroArt;
    private readonly string? steamRoot;
    private int columns = 2;
    private UiKit.ChipGroup<string>? modeChips, resChips, styleChips; private UiKit.ChipGroup<int>? hzChips;
    public IReadOnlyList<LibGame> Games => games;
    public GameProfile? Profile => profile;

    public LauncherPage(LinuxGameLibrary library, SteamInfo steam, string settingsPath, LaunchFunc launch, Func<Window?> owner, string gpuVendor, bool multiGpu, CancellationToken lifetime)
    {
        this.library = library; this.steam = steam; this.settingsPath = settingsPath; this.launch = launch; this.owner = owner; this.gpuVendor = gpuVendor; this.multiGpu = multiGpu; this.lifetime = lifetime;
        settings = IlmenyxSettings.Load(settingsPath);
        steamRoot = steam.Root();
        ColumnDefinitions = new("300,20,*"); RowDefinitions = new("Auto,*");
        BuildLeft(); BuildRight();
        // Narrow window (minimum 900x560 leaves ~640 px here): a slimmer one-column library so the detail keeps room for its chips.
        SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 860; int cols = narrow ? 1 : 2;
            ColumnDefinitions[0].Width = new GridLength(narrow ? 230 : 300);
            if (cols != columns) { columns = cols; Filter(); }
        };
        Loaded += async (_, _) => { if (games.Count == 0) await ScanAsync(); };
    }

    private void BuildLeft()
    {
        var scan = new Button { Name = "BtnScanGames", Content = "ATUALIZAR LISTA", Padding = new Thickness(14, 8), FontSize = 12 };
        var add = new Button { Name = "BtnAddExe", Content = "+ ADICIONAR EXECUTÁVEL", Padding = new Thickness(14, 8), FontSize = 12 };
        scan.Click += async (_, _) => await ScanAsync(); add.Click += (_, _) => addBox.IsVisible = !addBox.IsVisible;
        search.TextChanged += (_, _) => Filter();
        var addPath = new TextBox { Name = "LAddPath", PlaceholderText = "/caminho/completo/do/jogo" };
        AutomationProperties.SetName(addPath, "Caminho completo do executável do jogo");
        var addStatus = new TextBlock { Name = "LAddStatus", Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var confirm = UiKit.Primary("BtnAddExeConfirm", "ADICIONAR", 14, 6);
        confirm.Click += async (_, _) =>
        {
            string path = addPath.Text?.Trim() ?? "";
            if (!LinuxGameLibrary.IsRunnableFile(path)) { addStatus.Text = "Informe o caminho completo de um arquivo regular executável (sem links, fora de /proc, /sys e /dev)."; return; }
            if (!settings.CustomGames.Any(c => c.Exe == path)) { settings.CustomGames.Add(new() { Name = Path.GetFileNameWithoutExtension(path), Exe = path }); Save(); }
            addStatus.Text = "Adicionado."; await ScanAsync(); Select(games.FirstOrDefault(g => g.Store == "Manual" && g.Id == path));
        };
        addBox.Children.Add(addPath); addBox.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { confirm } }); addBox.Children.Add(addStatus);

        // Page header: title, library summary (live text) and the library actions.
        var header = UiKit.PageHeader("Jogos", null, new WrapPanel { ItemSpacing = 8, LineSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { scan, add } });
        count.FontSize = 12.5; ((StackPanel)header.Children[0]).Children.Add(count);
        Grid.SetColumnSpan(header, 3); Children.Add(header);

        var searchBox = new Grid { Children = { search, SearchIcon() } };
        search.Padding = new Thickness(34, 7, 10, 7);
        var tools = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Spacing = 8, Children = { searchBox, addBox } };
        var left = new Grid { RowDefinitions = new("Auto,Auto,*") };
        var shelf = new TextBlock { Text = "BIBLIOTECA", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Faint, LetterSpacing = 0.6, Margin = new Thickness(4, 0, 0, 8) };
        Grid.SetRow(shelf, 1); Grid.SetRow(tools, 0);
        var scroll = new ScrollViewer { Name = "LGameScroll", Content = list, Padding = new Thickness(0, 0, 8, 0) }; Grid.SetRow(scroll, 2);
        left.Children.Add(tools); left.Children.Add(shelf); left.Children.Add(scroll);
        Grid.SetRow(left, 1); Children.Add(left);
    }

    private static Control SearchIcon()
    {
        var icon = UiKit.Icon(Icons.Search, 15, Palette.Faint);
        icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); icon.IsHitTestVisible = false;
        return icon;
    }

    /// <summary>Small card title: icon + caps label (the reference's section captions, inside a surface).</summary>
    private static StackPanel CardTitle(string icon, string text) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10), Children = { UiKit.Icon(icon, 15, Palette.AccentText), UiKit.Label(text) },
    };

    /// <summary>The chip groups are horizontal stacks; in the detail column they must wrap at the minimum window size.</summary>
    private static WrapPanel Wrap(StackPanel chips)
    {
        var w = new WrapPanel { Name = chips.Name, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var c in chips.Children.ToList()) { chips.Children.Remove(c); w.Children.Add(c); }
        return w;
    }

    private void BuildRight()
    {
        emptyBox.Child = new StackPanel
        {
            Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.Icon(Icons.Games, 40, Palette.Faint), empty, new TextBlock { Text = "Cada jogo guarda o seu próprio perfil: desempenho, argumentos de inicialização e programas auxiliares.", Foreground = Palette.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 340 } },
        };
        empty.HorizontalAlignment = HorizontalAlignment.Center; empty.FontSize = 14;
        Grid.SetColumn(emptyBox, 2); Grid.SetRow(emptyBox, 1); Children.Add(emptyBox);
        play.Click += async (_, _) => await PlayAsync();

        // Hero: the selected game (local Steam artwork when there is one, the brand gradient otherwise), its tags and the main action.
        title.FontSize = 26; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        sub.FontSize = 12.5; sub.MaxLines = 2; sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.Margin = new Thickness(0, 4, 0, 0);
        play.VerticalAlignment = VerticalAlignment.Bottom; play.Margin = new Thickness(16, 0, 0, 0); play.FontSize = 14;
        var bottom = new Grid { ColumnDefinitions = new("*,Auto"), VerticalAlignment = VerticalAlignment.Bottom, Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { title, sub } } } };
        Grid.SetColumn(play, 1); bottom.Children.Add(play);
        var heroContent = new Grid { RowDefinitions = new("Auto,*,Auto"), Children = { heroTags } };
        Grid.SetRow(bottom, 2); heroContent.Children.Add(bottom);
        hero = UiKit.Hero(heroContent, height: 232, name: "LHero");
        if (hero.Child is Grid layers) layers.Children.Insert(0, heroScrim);

        foreach (var c in new[] { chkPlan, chkGpu, chkGameMode, chkMango }) { c.Margin = new Thickness(0, 0, 0, 6); c.FontSize = 13; c.IsCheckedChanged += (_, _) => SaveFromControls(); }
        ToolTip.SetTip(chkGpu, multiGpu ? "Define as variáveis de PRIME offload para a GPU dedicada." : "Esta máquina tem uma única GPU: não há o que selecionar.");
        chkGpu.IsEnabled = multiGpu;
        bool hasGm = BoundedProcess.Find("gamemoderun") is not null, hasMango = BoundedProcess.Find("mangohud") is not null;
        chkGameMode.IsEnabled = hasGm; chkMango.IsEnabled = hasMango;
        if (!hasGm) ToolTip.SetTip(chkGameMode, "gamemoderun não está instalado."); if (!hasMango) ToolTip.SetTip(chkMango, "mangohud não está instalado.");
        var setExe = new Button { Name = "BtnSetExe", Content = "DEFINIR EXECUTÁVEL", FontSize = 12, Padding = new Thickness(14, 7) }; setExe.Click += (_, _) => SetExe();
        var recommended = new Button { Name = "BtnRecommended", Content = "PERFIL PARA MAIS FPS", FontSize = 12, Padding = new Thickness(14, 7) }; recommended.Click += (_, _) => Recommended();
        exeText.Margin = new Thickness(0, 6, 0, 0);
        var perf = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Oc, "DESEMPENHO"), chkPlan, chkGpu, chkGameMode, chkMango, exeText, new Border { Height = 6 }, exePath, new Border { Height = 8 },
                new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { setExe, recommended } },
                new TextBlock { Text = "O perfil de energia vale só durante o jogo e volta ao anterior. Prioridade de processo elevada exige privilégios no Linux; o GameMode cuida disso quando instalado. O executável é descoberto sozinho; se errar, informe o caminho certo.", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }
            }
        });

        void Chosen(Action<GameProfile> set) { if (loading || profile is null) return; set(profile); Save(); UpdatePreview(); }
        modeChips = new("LModePanel", [("Padrão do jogo", "default"), ("Tela cheia", "full"), ("Sem bordas", "borderless"), ("Janela", "window")], "default", v => Chosen(p => p.WinMode = v));
        resChips = new("LResPanel", [("Padrão", "default"), ("1280x720", "1280x720"), ("1920x1080", "1920x1080"), ("2560x1440", "2560x1440"), ("3840x2160", "3840x2160")], "default", v => Chosen(p => p.Res = v));
        hzChips = new("LHzPanel", [("Padrão", 0), ("60", 60), ("120", 120), ("144", 144), ("165", 165), ("240", 240)], 0, v => Chosen(p => p.Hz = v));
        styleChips = new("LStylePanel", [("Source / CS2", "source"), ("Unity", "unity"), ("Unreal", "unreal")], "source", v => Chosen(p => p.Style = v));
        extra.TextChanged += (_, _) => SaveFromControls();
        var presets = new WrapPanel { Name = "LExtraPresets", Margin = new Thickness(0, 8, 0, 14), ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (label, token, hint) in new (string, string, string?)[] { ("Pular intro (-novid)", "-novid", null), ("Sem limite de FPS (+fps_max 0)", "+fps_max 0", null), ("Sem joystick (-nojoy)", "-nojoy", null), ("Abrir console (-console)", "-console", null),
            ("Programas de terceiros (-allow_third_party_software)", "-allow_third_party_software",
             "CS2: libera programas que o modo confiável bloqueia, como a captura de jogo do OBS, ReShade e alguns overlays.\nAtenção: a Valve diz que usar esta opção pode reduzir o seu Fator de Confiança (Trust Factor) no matchmaking.") })
        {
            var b = new Button { Content = label, FontSize = 11, Padding = new Thickness(11, 4), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Background = Palette.Card, Foreground = Palette.Muted, BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
            if (hint is not null) ToolTip.SetTip(b, hint);
            b.Click += (_, _) => { if (!(extra.Text ?? "").Contains(token, StringComparison.OrdinalIgnoreCase)) extra.Text = ((extra.Text ?? "").Trim() + " " + token).Trim(); };
            presets.Children.Add(b);
        }
        var steamApply = UiKit.Primary("BtnSteamApply", "APLICAR NAS OPÇÕES DA STEAM"); steamApply.HorizontalAlignment = HorizontalAlignment.Left;
        steamApply.Click += async (_, _) => await ApplySteamAsync(steamApply);
        steamBox.Children.Add(steamNow); steamBox.Children.Add(steamApply);
        preview.FontSize = 12; preview.Margin = new Thickness(0);
        var code = new Border { Background = Palette.Hover, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 9), Margin = new Thickness(0, 6, 0, 12), Child = preview };
        Control Row(string label, Control chips) => new StackPanel { Margin = new Thickness(0, 0, 0, 12), Children = { new TextBlock { Text = label, Foreground = Palette.Muted, FontSize = 12 }, new Border { Height = 6 }, chips } };
        var gfx = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Gpu, "GRÁFICOS (ARGUMENTOS DE INICIALIZAÇÃO)"), Row("Modo de janela", Wrap(modeChips.Panel)), Row("Resolução", Wrap(resChips.Panel)), Row("Taxa de atualização (Hz)", Wrap(hzChips.Panel)), Row("Tipo de argumento (depende do jogo)", Wrap(styleChips.Panel)),
                new TextBlock { Text = "Extras", Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) }, extra, presets, UiKit.Label("LINHA FINAL"), code, steamBox, gfxNote
            }
        });
        var addHelper = new Button { Name = "BtnAddHelper", Content = "ADICIONAR", FontSize = 12, Padding = new Thickness(14, 7) }; addHelper.Click += (_, _) => AddHelper();
        var help = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Apps, "PROGRAMAS AUXILIARES"), new TextBlock { Text = "Abrem antes do jogo (ex.: monitoramento) e podem fechar junto com ele. Iniciados sem shell, com os argumentos que você escrever após o caminho.", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) },
                helpers, helperPath, new WrapPanel { Margin = new Thickness(0, 10, 0, 0), ItemSpacing = 12, LineSpacing = 6, Children = { helperClose, addHelper } }
            }
        });
        status.Margin = new Thickness(4, 12, 0, 4);
        detail.Content = new StackPanel { Margin = new Thickness(0, 0, 8, 0), Children = { hero, status, UiKit.SectionHeader("Perfil do jogo"), perf, gfx, help } };
        Grid.SetColumn(detail, 2); Grid.SetRow(detail, 1); Children.Add(detail);
    }

    /// <summary>Hero tags: store and the parts of the profile that are on (refreshed whenever the profile changes).</summary>
    private void UpdateTags()
    {
        heroTags.Children.Clear();
        if (current is not { } g || profile is not { } p) return;
        var tags = new List<string> { g.Store };
        if (p.HighPerfPlan) tags.Add("Alto desempenho");
        if (p.GpuHigh && multiGpu) tags.Add("GPU dedicada");
        if (p.GameMode && chkGameMode.IsEnabled) tags.Add("GameMode");
        if (p.MangoHud && chkMango.IsEnabled) tags.Add("MangoHud");
        if (p.Res != "default") tags.Add(p.Res);
        if (p.Hz > 0) tags.Add(p.Hz + " Hz");
        if (tags.Count == 1) tags.Add("Perfil padrão");
        if (p.Helpers.Count > 0) tags.Add(p.Helpers.Count == 1 ? "1 programa auxiliar" : p.Helpers.Count + " programas auxiliares");
        foreach (var t in tags) heroTags.Children.Add(UiKit.Tag(t, onImage: heroArt));
    }

    private string SettingsKey(LibGame g) => g.Key;
    private void Save() { try { settings.Save(settingsPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Não foi possível salvar as configurações: " + e.Message; } }

    public async Task ScanAsync()
    {
        count.Text = "Procurando seus jogos…";
        try
        {
            games = await library.ScanAsync(settings.CustomGames.ToList(), lifetime);
            Filter();
            count.Text = games.Count == 0 ? "Nenhum jogo encontrado. Use + ADICIONAR EXECUTÁVEL para incluir um à mão." : $"{games.Count} jogos · {string.Join(" · ", games.GroupBy(g => g.Store).Select(x => $"{x.Key} {x.Count()}"))}";
        }
        catch (OperationCanceledException) { count.Text = "Busca cancelada."; }
    }

    private void Filter()
    {
        string q = (search.Text ?? "").Trim(); string? keep = current?.Key;
        list.Children.Clear();
        var shown = games.Where(g => q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        // Asymmetric grid: the selected game (or the first one) is a wide featured tile, the others pair up below it.
        string? big = shown.Any(g => g.Key == keep) ? keep : shown.FirstOrDefault()?.Key;
        Grid? row = null; int col = 0;
        foreach (var g in shown)
        {
            var item = Tile(g, g.Key == big, g.Key == keep);
            AutomationProperties.SetName(item, g.Name + " (" + g.Store + ")");
            var game = g; item.Click += (_, _) => { Select(game); FocusTile(game); };
            if (g.Key == big || columns == 1) { list.Children.Add(item); row = null; continue; }
            if (row is null || col >= columns) { row = new Grid { ColumnDefinitions = new("*,8,*") }; list.Children.Add(row); col = 0; }
            Grid.SetColumn(item, col * 2); row.Children.Add(item); col++;
        }
    }

    private Button Tile(LibGame g, bool large, bool selected)
    {
        var art = g.IsSteam ? GameArt.Steam(steamRoot, g.Id, hero: false) : null;
        var body = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                GameArt.Cover(art, g.Name, large ? (columns == 1 ? 104 : 128) : (columns == 1 ? 0 : 68)),
                new TextBlock { Text = g.Name, FontSize = large ? 13.5 : 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 6, 2, 0), Foreground = selected ? Palette.AccentText : Palette.Fg },
                new TextBlock { Text = g.Subtitle, FontSize = 11, Foreground = Palette.Faint, Margin = new Thickness(2, 0, 2, 0) },
            },
        };
        if (body.Children[0] is Border { Height: 0 } none) none.IsVisible = false; // compact one-column list: names only, except the featured tile
        return new Button
        {
            Name = "LGame_" + SafeName(g.Key), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(6, 6, 6, 8), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius),
            Background = selected ? Palette.Hover : Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = selected ? Palette.Accent : Brushes.Transparent, Content = body,
        };
    }

    /// <summary>The library is rebuilt on selection (the featured tile moves): keep keyboard focus on the chosen game.</summary>
    private void FocusTile(LibGame g)
    {
        string name = "LGame_" + SafeName(g.Key);
        foreach (var b in list.Children.SelectMany(c => c is Grid r ? r.Children : [c]).OfType<Button>()) if (b.Name == name) { b.Focus(); return; }
    }

    public static string SafeName(string key) => new(key.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    public void Select(LibGame? g)
    {
        current = g;
        if (g is null) { detail.IsVisible = false; emptyBox.IsVisible = true; profile = null; Filter(); return; }
        detail.IsVisible = true; emptyBox.IsVisible = false;
        if (!settings.GameProfiles.TryGetValue(SettingsKey(g), out var p)) { p = new GameProfile(); settings.GameProfiles[SettingsKey(g)] = p; }
        loading = true; profile = p;
        title.Text = g.Name; sub.Text = $"{g.Store} · {g.InstallDir}";
        var art = g.IsSteam ? GameArt.Steam(steamRoot, g.Id, hero: true) : null; heroArt = art is not null;
        hero.Background = art is null ? UiKit.HeroBrush() : new ImageBrush { Source = art, Stretch = Stretch.UniformToFill };
        heroScrim.IsVisible = heroArt; title.Foreground = heroArt ? Brushes.White : Palette.Fg; sub.Foreground = heroArt ? GameArt.OnImageMuted : Palette.Muted;
        chkPlan.IsChecked = p.HighPerfPlan; chkGpu.IsChecked = p.GpuHigh && multiGpu; chkGameMode.IsChecked = p.GameMode; chkMango.IsChecked = p.MangoHud;
        modeChips!.Select(p.WinMode); resChips!.Select(p.Res); hzChips!.Select(p.Hz); styleChips!.Select(p.Style); extra.Text = p.Extra; exePath.Text = p.ExePath ?? "";
        RebuildHelpers();
        steamBox.IsVisible = g.IsSteam; steamNow.Text = g.IsSteam ? "Opções atuais na Steam: " + (steam.GetLaunchOptions(g.Id) is { Length: > 0 } cur ? cur : "(nenhuma)") : "";
        gfxNote.Text = g.IsSteam ? "Os argumentos e variáveis só passam a valer na Steam depois de APLICAR NAS OPÇÕES DA STEAM (a Steam é fechada nessa hora). Depois, o JOGAR abre pela Steam normalmente."
            : g.LaunchUri is not null ? "Esta loja abre o jogo por um atalho que não aceita argumentos: use as opções de inicialização da própria loja. Desempenho e programas auxiliares funcionam normalmente."
            : "Os argumentos são enviados direto ao executável quando você clica em JOGAR.";
        status.Text = ""; loading = false; UpdatePreview(); ShowExe(g, p);
        Filter();
    }

    private void ShowExe(LibGame g, GameProfile p)
    {
        string? exe = p.ExePath ?? g.ExePath;
        if (exe is not null) { exeText.Text = "Executável: " + exe; return; }
        exeText.Text = "Procurando o executável…";
        var game = g;
        _ = Task.Run(() => LinuxGameLibrary.FindMainExe(game.InstallDir, game.Name)).ContinueWith(t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        { if (current == game) exeText.Text = t.Result is { } found ? $"Executável (detectado, confirme): {found}" : "Executável não identificado. Informe o caminho abaixo, se necessário."; }));
    }

    private void SaveFromControls()
    {
        if (loading || profile is null) return;
        profile.HighPerfPlan = chkPlan.IsChecked == true; profile.GpuHigh = chkGpu.IsChecked == true; profile.GameMode = chkGameMode.IsChecked == true; profile.MangoHud = chkMango.IsChecked == true;
        profile.Extra = (extra.Text ?? "").Trim();
        Save(); UpdatePreview();
    }

    private void UpdatePreview() { if (profile is null) return; string a = GameLaunchOptions.BuildArgs(profile); string o = current?.IsSteam == true ? GameLaunchOptions.BuildSteamOptions(profile, gpuVendor, File.Exists(MangoHudLogReader.ConfigPath()) ? MangoHudLogReader.ConfigPath() : null) : a; preview.Text = o.Length > 0 ? o : "(nenhum argumento)"; UpdateTags(); }

    private void Recommended()
    {
        if (profile is null) return;
        loading = true; chkPlan.IsChecked = true; chkGpu.IsChecked = multiGpu; chkGameMode.IsChecked = chkGameMode.IsEnabled; loading = false; SaveFromControls();
        status.Text = "Perfil para mais FPS ligado: Alto desempenho" + (multiGpu ? ", GPU dedicada" : "") + (chkGameMode.IsEnabled ? ", GameMode" : "") + ".";
    }

    private void SetExe()
    {
        if (current is null || profile is null) return;
        string path = (exePath.Text ?? "").Trim();
        if (!LinuxGameLibrary.IsRunnableFile(path)) { status.Text = "Caminho inválido: informe um arquivo regular executável (sem links, fora de /proc, /sys e /dev)."; return; }
        profile.ExePath = path; Save(); ShowExe(current, profile); status.Text = "Executável definido.";
    }

    private void RebuildHelpers()
    {
        helpers.Children.Clear();
        if (profile is null) return;
        foreach (var h in profile.Helpers.ToList())
        {
            var remove = new Button { Content = "REMOVER", FontSize = 11, Padding = new Thickness(10, 4) };
            var grid = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
            var text = new TextBlock { Text = h.Display + (h.CloseWithGame ? "  ·  fecha com o jogo" : ""), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(remove, 1); grid.Children.Add(text); grid.Children.Add(remove);
            remove.Click += (_, _) => { profile.Helpers.Remove(h); Save(); RebuildHelpers(); UpdateTags(); };
            helpers.Children.Add(grid);
        }
    }

    private void AddHelper()
    {
        if (profile is null) return;
        string line = (helperPath.Text ?? "").Trim();
        try
        {
            var parts = GameLaunchOptions.SplitArguments(line);
            if (parts.Count == 0 || !LinuxGameLibrary.IsRunnableFile(parts[0])) { status.Text = "Informe o caminho completo de um programa executável (e, opcionalmente, seus argumentos)."; return; }
            profile.Helpers.Add(new HelperProgram { Path = parts[0], Args = string.Join(" ", parts.Skip(1).Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)), CloseWithGame = helperClose.IsChecked == true });
            Save(); helperPath.Text = ""; RebuildHelpers(); UpdateTags();
        }
        catch (FormatException e) { status.Text = e.Message; }
    }

    private async Task ApplySteamAsync(Button button)
    {
        if (current is not { IsSteam: true } g || profile is null || owner() is not { } window) return;
        string options = GameLaunchOptions.BuildSteamOptions(profile, gpuVendor, File.Exists(MangoHudLogReader.ConfigPath()) ? MangoHudLogReader.ConfigPath() : null);
        button.IsEnabled = false;
        try
        {
            if (steam.IsRunning)
            {
                if (!await ConfirmDialog.AskAsync(window, "Jogos · Steam", "A Steam precisa ser fechada para gravar as opções de inicialização (senão ela as sobrescreve ao sair).\n\nFechar a Steam agora? Jogos abertos serão encerrados. É feito um backup do arquivo antes de alterar.")) return;
                status.Text = "Fechando a Steam…";
                if (!await steam.ShutdownAsync(lifetime)) { status.Text = "A Steam não fechou. Feche-a manualmente e tente de novo."; return; }
            }
            string? error = await Task.Run(() => steam.SetLaunchOptions(g.Id, options));
            status.Text = error ?? (options.Length > 0 ? $"Opções gravadas na Steam: {options}. Clique em JOGAR (a Steam abre sozinha)." : "Opções da Steam limpas para este jogo.");
            steamNow.Text = "Opções atuais na Steam: " + (steam.GetLaunchOptions(g.Id) is { Length: > 0 } cur ? cur : "(nenhuma)");
        }
        catch (OperationCanceledException) { status.Text = "Cancelado."; }
        finally { button.IsEnabled = true; }
    }

    private async Task PlayAsync()
    {
        if (launching is not null) { launching.Cancel(); return; }
        if (current is not { } g || profile is not { } p) return;
        launching = CancellationTokenSource.CreateLinkedTokenSource(lifetime); play.Content = "PARAR ACOMPANHAMENTO";
        try
        {
            var session = await launch(g, p, gpuVendor, t => Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = t),
                exe => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (p.ExePath != exe) { p.ExePath = exe; Save(); if (current == g) exeText.Text = "Executável: " + exe; } }), launching.Token);
            // Measured on this PC (MangoHud log of this launch): kept locally for the FPS page, at most FpsSession.MaxEntries sessions.
            if (session is { IsValid: true }) { settings.AddFpsSession(session); Save(); }
        }
        finally { launching.Dispose(); launching = null; play.Content = "JOGAR"; }
    }
}

/// <summary>
/// Game artwork for the library: the user's own Steam client cache (read-only local files, never downloaded) or a brand placeholder.
/// Files are size-limited, decoded at a bounded width and cached for the session; any unreadable file falls back to the placeholder.
/// </summary>
internal static class GameArt
{
    private const long MaxBytes = 8L << 20;
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    public static readonly IBrush OnImageMuted = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));

    /// <summary>Steam artwork for <paramref name="appId"/>: the wide hero/header images (hero) or the header/capsule (tiles).</summary>
    public static Bitmap? Steam(string? steamRoot, string appId, bool hero)
    {
        if (steamRoot is null || appId.Length is 0 or > 12 || !appId.All(char.IsAsciiDigit)) return null;
        string dir = Path.Combine(steamRoot, "appcache", "librarycache");
        string[] names = hero
            ? [appId + "_library_hero.jpg", Path.Combine(appId, "library_hero.jpg"), appId + "_header.jpg", Path.Combine(appId, "header.jpg"), appId + "_library_600x900.jpg", Path.Combine(appId, "library_600x900.jpg")]
            : [appId + "_header.jpg", Path.Combine(appId, "header.jpg"), appId + "_library_600x900.jpg", Path.Combine(appId, "library_600x900.jpg")];
        foreach (var n in names) if (Load(Path.Combine(dir, n), hero ? 1280 : 460) is { } b) return b;
        return null;
    }

    private static Bitmap? Load(string path, int width)
    {
        string key = path + "|" + width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (Cache) if (Cache.TryGetValue(key, out var hit)) return hit;
        Bitmap? bitmap = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.LinkTarget is null && info.Length is > 0 and <= MaxBytes)
            {
                using var stream = File.OpenRead(path);
                bitmap = Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException) { bitmap = null; }
        lock (Cache) Cache[key] = bitmap;
        return bitmap;
    }

    /// <summary>Cover surface: the artwork cropped to fill, or the brand gradient with the game's initials and the games icon.</summary>
    public static Border Cover(Bitmap? art, string name, double height)
    {
        var b = new Border { Height = height, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius + 2), ClipToBounds = true };
        if (art is not null) { b.Background = new ImageBrush { Source = art, Stretch = Stretch.UniformToFill }; return b; }
        b.Background = UiKit.HeroBrush();
        var icon = UiKit.Icon(Icons.Games, Math.Max(18, height * 0.42), Palette.Fg);
        icon.Opacity = 0.10; icon.HorizontalAlignment = HorizontalAlignment.Right; icon.VerticalAlignment = VerticalAlignment.Bottom; icon.Margin = new Thickness(0, 0, 10, 6);
        b.Child = new Grid
        {
            Children =
            {
                icon,
                new TextBlock { Text = Initials(name), FontSize = Math.Max(14, height * 0.3), FontWeight = FontWeight.Bold, Foreground = Palette.Fg, Opacity = 0.82, LetterSpacing = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 8) },
            },
        };
        return b;
    }

    public static string Initials(string name)
    {
        var words = name.Split([' ', '-', '_', ':', '.'], StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(2).ToList();
        return words.Count == 0 ? "?" : string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
    }

    /// <summary>Legibility scrim over artwork: darker towards the bottom and the left, where the text sits.</summary>
    public static IBrush Scrim() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(0x10, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0.45), new GradientStop(Color.FromArgb(0xC0, 0, 0, 0), 1) },
    };
}
