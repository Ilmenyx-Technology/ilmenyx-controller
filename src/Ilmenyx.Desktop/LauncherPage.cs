// New Ilmenyx additions (layout follows the original RESET JOGOS launcher: game list left, profile cards right): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class LauncherPage : Grid
{
    public delegate Task LaunchFunc(LibGame game, GameProfile profile, string gpuVendor, Action<string> status, Action<string> learnedExe, CancellationToken ct);

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
    private readonly StackPanel list = new() { Name = "LGameList", Spacing = 2 };
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
    private UiKit.ChipGroup<string>? modeChips, resChips, styleChips; private UiKit.ChipGroup<int>? hzChips;
    public IReadOnlyList<LibGame> Games => games;
    public GameProfile? Profile => profile;

    public LauncherPage(LinuxGameLibrary library, SteamInfo steam, string settingsPath, LaunchFunc launch, Func<Window?> owner, string gpuVendor, bool multiGpu, CancellationToken lifetime)
    {
        this.library = library; this.steam = steam; this.settingsPath = settingsPath; this.launch = launch; this.owner = owner; this.gpuVendor = gpuVendor; this.multiGpu = multiGpu; this.lifetime = lifetime;
        settings = IlmenyxSettings.Load(settingsPath);
        ColumnDefinitions = new("320,24,*");
        BuildLeft(); BuildRight();
        Loaded += async (_, _) => { if (games.Count == 0) await ScanAsync(); };
    }

    private void BuildLeft()
    {
        var scan = new Button { Name = "BtnScanGames", Content = "ATUALIZAR LISTA", Padding = new Thickness(14, 8) };
        var add = new Button { Name = "BtnAddExe", Content = "+ ADICIONAR EXECUTÁVEL", Padding = new Thickness(14, 8) };
        scan.Click += async (_, _) => await ScanAsync(); add.Click += (_, _) => addBox.IsVisible = !addBox.IsVisible;
        search.TextChanged += (_, _) => Filter();
        var addPath = new TextBox { Name = "LAddPath", PlaceholderText = "/caminho/completo/do/jogo" };
        AutomationProperties.SetName(addPath, "Caminho completo do executável do jogo");
        var addStatus = new TextBlock { Name = "LAddStatus", Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var confirm = new Button { Name = "BtnAddExeConfirm", Content = "ADICIONAR" };
        confirm.Click += async (_, _) =>
        {
            string path = addPath.Text?.Trim() ?? "";
            if (!LinuxGameLibrary.IsRunnableFile(path)) { addStatus.Text = "Informe o caminho completo de um arquivo regular executável (sem links, fora de /proc, /sys e /dev)."; return; }
            if (!settings.CustomGames.Any(c => c.Exe == path)) { settings.CustomGames.Add(new() { Name = Path.GetFileNameWithoutExtension(path), Exe = path }); Save(); }
            addStatus.Text = "Adicionado."; await ScanAsync(); Select(games.FirstOrDefault(g => g.Store == "Manual" && g.Id == path));
        };
        addBox.Children.Add(addPath); addBox.Children.Add(confirm); addBox.Children.Add(addStatus);
        var head = new StackPanel { Margin = new Thickness(0, 4, 0, 14), Children = { new TextBlock { Text = "Jogos", FontSize = 32, FontWeight = FontWeight.Bold }, count } };
        var tools = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Spacing = 8, Children = { search, new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { scan, add } }, addBox } };
        var left = new Grid { RowDefinitions = new("Auto,Auto,*") };
        Grid.SetRow(tools, 1); var scroll = new ScrollViewer { Name = "LGameScroll", Content = list }; Grid.SetRow(scroll, 2);
        left.Children.Add(head); left.Children.Add(tools); left.Children.Add(scroll);
        Children.Add(left);
    }

    private void BuildRight()
    {
        Grid.SetColumn(empty, 2); Children.Add(empty);
        play.Click += async (_, _) => await PlayAsync();
        var topRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        topRow.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 200, 0), Children = { title, sub } }); play.HorizontalAlignment = HorizontalAlignment.Right; play.VerticalAlignment = VerticalAlignment.Top; topRow.Children.Add(play);

        foreach (var c in new[] { chkPlan, chkGpu, chkGameMode, chkMango }) { c.Margin = new Thickness(0, 0, 0, 10); c.IsCheckedChanged += (_, _) => SaveFromControls(); }
        ToolTip.SetTip(chkGpu, multiGpu ? "Define as variáveis de PRIME offload para a GPU dedicada." : "Esta máquina tem uma única GPU: não há o que selecionar.");
        chkGpu.IsEnabled = multiGpu;
        bool hasGm = BoundedProcess.Find("gamemoderun") is not null, hasMango = BoundedProcess.Find("mangohud") is not null;
        chkGameMode.IsEnabled = hasGm; chkMango.IsEnabled = hasMango;
        if (!hasGm) ToolTip.SetTip(chkGameMode, "gamemoderun não está instalado."); if (!hasMango) ToolTip.SetTip(chkMango, "mangohud não está instalado.");
        var setExe = new Button { Name = "BtnSetExe", Content = "DEFINIR EXECUTÁVEL" }; setExe.Click += (_, _) => SetExe();
        var recommended = new Button { Name = "BtnRecommended", Content = "PERFIL PARA MAIS FPS" }; recommended.Click += (_, _) => Recommended();
        var perf = UiKit.Card(new StackPanel
        {
            Children =
            {
                UiKit.Label("DESEMPENHO"), new Border { Height = 8 }, chkPlan, chkGpu, chkGameMode, chkMango, exeText, new Border { Height = 6 }, exePath, new Border { Height = 8 },
                new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { setExe, recommended } },
                new TextBlock { Text = "O perfil de energia vale só durante o jogo e volta ao anterior. Prioridade de processo elevada exige privilégios no Linux; o GameMode cuida disso quando instalado. O executável é descoberto sozinho; se errar, informe o caminho certo.", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }
            }
        });

        void Chosen(Action<GameProfile> set) { if (loading || profile is null) return; set(profile); Save(); UpdatePreview(); }
        modeChips = new("LModePanel", [("Padrão do jogo", "default"), ("Tela cheia", "full"), ("Sem bordas", "borderless"), ("Janela", "window")], "default", v => Chosen(p => p.WinMode = v));
        resChips = new("LResPanel", [("Padrão", "default"), ("1280x720", "1280x720"), ("1920x1080", "1920x1080"), ("2560x1440", "2560x1440"), ("3840x2160", "3840x2160")], "default", v => Chosen(p => p.Res = v));
        hzChips = new("LHzPanel", [("Padrão", 0), ("60", 60), ("120", 120), ("144", 144), ("165", 165), ("240", 240)], 0, v => Chosen(p => p.Hz = v));
        styleChips = new("LStylePanel", [("Source / CS2", "source"), ("Unity", "unity"), ("Unreal", "unreal")], "source", v => Chosen(p => p.Style = v));
        extra.TextChanged += (_, _) => SaveFromControls();
        var presets = new WrapPanel { Name = "LExtraPresets", Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (label, token) in new[] { ("Pular intro (-novid)", "-novid"), ("Sem limite de FPS (+fps_max 0)", "+fps_max 0"), ("Sem joystick (-nojoy)", "-nojoy"), ("Abrir console (-console)", "-console") })
        {
            var b = new Button { Content = label, FontSize = 11, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => { if (!(extra.Text ?? "").Contains(token, StringComparison.OrdinalIgnoreCase)) extra.Text = ((extra.Text ?? "").Trim() + " " + token).Trim(); };
            presets.Children.Add(b);
        }
        var steamApply = new Button { Name = "BtnSteamApply", Content = "APLICAR NAS OPÇÕES DA STEAM", Background = Palette.Accent, Foreground = Palette.OnAccent, FontWeight = FontWeight.Bold, Padding = new Thickness(20, 9), HorizontalAlignment = HorizontalAlignment.Left };
        steamApply.Click += async (_, _) => await ApplySteamAsync(steamApply);
        steamBox.Children.Add(steamNow); steamBox.Children.Add(steamApply);
        Control Row(string label, Control chips) => new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { new TextBlock { Text = label, Foreground = Palette.Muted, FontSize = 12 }, new Border { Height = 4 }, chips } };
        var gfx = UiKit.Card(new StackPanel
        {
            Children =
            {
                UiKit.Label("GRÁFICOS (ARGUMENTOS DE INICIALIZAÇÃO)"), Row("Modo de janela", modeChips.Panel), Row("Resolução", resChips.Panel), Row("Taxa de atualização (Hz)", hzChips.Panel), Row("Tipo de argumento (depende do jogo)", styleChips.Panel),
                Row("Extras", extra), presets, UiKit.Label("LINHA FINAL"), preview, steamBox, gfxNote
            }
        });
        var addHelper = new Button { Name = "BtnAddHelper", Content = "ADICIONAR" }; addHelper.Click += (_, _) => AddHelper();
        var help = UiKit.Card(new StackPanel
        {
            Children =
            {
                UiKit.Label("PROGRAMAS AUXILIARES"), new TextBlock { Text = "Abrem antes do jogo (ex.: monitoramento) e podem fechar junto com ele. Iniciados sem shell, com os argumentos que você escrever após o caminho.", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) },
                helpers, helperPath, new WrapPanel { Margin = new Thickness(0, 8, 0, 0), ItemSpacing = 12, Children = { helperClose, addHelper } }
            }
        });
        detail.Content = new StackPanel { Margin = new Thickness(0, 0, 6, 0), Children = { topRow, status, perf, gfx, help } };
        Grid.SetColumn(detail, 2); Children.Add(detail);
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
        foreach (var g in games.Where(g => q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
        {
            var item = new Button { Name = "LGame_" + SafeName(g.Key), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(12), Background = g.Key == keep ? Palette.Card : Brushes.Transparent, Content = new StackPanel { Children = { new TextBlock { Text = g.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = g.Subtitle, FontSize = 11, Opacity = 0.6 } } } };
            AutomationProperties.SetName(item, g.Name + " (" + g.Store + ")");
            var game = g; item.Click += (_, _) => Select(game); list.Children.Add(item);
        }
    }

    public static string SafeName(string key) => new(key.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    public void Select(LibGame? g)
    {
        current = g;
        if (g is null) { detail.IsVisible = false; empty.IsVisible = true; profile = null; return; }
        detail.IsVisible = true; empty.IsVisible = false;
        if (!settings.GameProfiles.TryGetValue(SettingsKey(g), out var p)) { p = new GameProfile(); settings.GameProfiles[SettingsKey(g)] = p; }
        loading = true; profile = p;
        title.Text = g.Name; sub.Text = $"{g.Store} · {g.InstallDir}";
        chkPlan.IsChecked = p.HighPerfPlan; chkGpu.IsChecked = p.GpuHigh && multiGpu; chkGameMode.IsChecked = p.GameMode; chkMango.IsChecked = p.MangoHud;
        modeChips!.Select(p.WinMode); resChips!.Select(p.Res); hzChips!.Select(p.Hz); styleChips!.Select(p.Style); extra.Text = p.Extra; exePath.Text = p.ExePath ?? "";
        RebuildHelpers();
        steamBox.IsVisible = g.IsSteam; steamNow.Text = g.IsSteam ? "Opções atuais na Steam: " + (steam.GetLaunchOptions(g.Id) is { Length: > 0 } cur ? cur : "(nenhuma)") : "";
        gfxNote.Text = g.IsSteam ? "Os argumentos e variáveis só passam a valer na Steam depois de APLICAR NAS OPÇÕES DA STEAM (a Steam é fechada nessa hora). Depois, o JOGAR abre pela Steam normalmente."
            : g.LaunchUri is not null ? "Esta loja abre o jogo por um atalho que não aceita argumentos: use as opções de inicialização da própria loja. Desempenho e programas auxiliares funcionam normalmente."
            : "Os argumentos são enviados direto ao executável quando você clica em JOGAR.";
        status.Text = ""; loading = false; UpdatePreview(); ShowExe(g, p);
        foreach (var child in list.Children.OfType<Button>()) child.Background = child.Name == "LGame_" + SafeName(g.Key) ? Palette.Card : Brushes.Transparent;
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

    private void UpdatePreview() { if (profile is null) return; string a = GameLaunchOptions.BuildArgs(profile); string o = current?.IsSteam == true ? GameLaunchOptions.BuildSteamOptions(profile, gpuVendor, File.Exists(MangoHudLogReader.ConfigPath()) ? MangoHudLogReader.ConfigPath() : null) : a; preview.Text = o.Length > 0 ? o : "(nenhum argumento)"; }

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
            remove.Click += (_, _) => { profile.Helpers.Remove(h); Save(); RebuildHelpers(); };
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
            Save(); helperPath.Text = ""; RebuildHelpers();
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
            await launch(g, p, gpuVendor, t => Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = t),
                exe => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (p.ExePath != exe) { p.ExePath = exe; Save(); if (current == g) exeText.Text = "Executável: " + exe; } }), launching.Token);
        }
        finally { launching.Dispose(); launching = null; play.Content = "JOGAR"; }
    }
}
