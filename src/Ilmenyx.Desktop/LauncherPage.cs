// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original RESET JOGOS launcher: game list left, profile cards right; cinematic library grid and hero since 2026-11-06): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
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
    private readonly TextBlock count = new() { Name = "LGameCount", Text = L.T("Procurando seus jogos…"), Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Name = "LGameSearch", PlaceholderText = L.T("Buscar jogo…") };
    private readonly StackPanel list = new() { Name = "LGameList", Spacing = 8 };
    private readonly TextBlock empty = new() { Name = "LEmpty", Text = L.T("Escolha um jogo na lista."), Foreground = Palette.Muted };
    private readonly ScrollViewer detail = new() { Name = "LDetail", IsVisible = false };
    private readonly TextBlock title = new() { Name = "LTitle", FontSize = 32, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock sub = new() { Name = "LSub", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button play = UiKit.Primary("BtnPlay", L.T("Jogar"), 40, 14);
    // Visual feedback between JOGAR and the game's window (Proton can take a while): a moving bar until the game process is seen.
    private readonly ProgressBar playProgress = new() { Name = "LPlayProgress", IsIndeterminate = true, IsVisible = false, Height = 4, MinHeight = 4, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock status = new() { Name = "LStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly CheckBox chkPlan = new() { Name = "LChkPlan", Content = L.T("Perfil de energia Alto desempenho enquanto joga (volta ao anterior depois)") };
    private readonly CheckBox chkGpu = new() { Name = "LChkGpu", Content = L.T("Forçar a placa de vídeo dedicada (PRIME offload)") };
    private readonly CheckBox chkGameMode = new() { Name = "LChkGameMode", Content = L.T("GameMode (gamemoderun)") };
    private readonly CheckBox chkMango = new() { Name = "LChkMango", Content = L.T("MangoHud (overlay de desempenho)") };
    private readonly CheckBox chkGuard = new() { Name = "LChkGuard", Content = L.T("Proteção contra travamento em tela cheia (vsync)") };
    private readonly CheckBox chkWayland = new() { Name = "LChkWayland", Content = GameExtras.WaylandLabel };
    private readonly CheckBox chkLowLat = new() { Name = "LChkLowLat", Content = GameExtras.LowLatencyLabel };
    private readonly TextBlock extrasNote = new() { Name = "LExtrasNote", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
    private readonly StackPanel extrasBox = new() { Name = "LExtrasBox", IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
    private string? protonName;
    /// <summary>Wayland session check (default: the real environment). Settable for fixtures.</summary>
    public Func<bool>? WaylandSession { get; init; }
    /// <summary>Newest UMU-Proton umu already downloaded (default: the real folder). Settable for fixtures.</summary>
    public Func<string?>? UmuProton { get; init; }
    private readonly TextBlock exeText = new() { Name = "LExeText", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox exePath = new() { Name = "LExePath", PlaceholderText = L.T("/caminho/completo/do/executavel") };
    private readonly TextBox extra = new() { Name = "LExtra", PlaceholderText = L.T("ex.: -novid -high +fps_max 0") };
    private readonly TextBox protonId = new() { Name = "LProtonId", PlaceholderText = L.T("ID Steam do jogo (opcional), ex.: 307780"), MaxLength = 10 };
    private readonly StackPanel protonBox = new() { Name = "LProtonBox", IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock protonIdStatus = new() { Name = "LProtonIdStatus", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button removeGame = new() { Name = "BtnRemoveGame", Content = L.T("Remover"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false };
    private readonly Button renameGame = new() { Name = "BtnRenameGame", Content = L.T("Renomear"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false };
    private static readonly SteamAppLookup SteamIds = new();
    /// <summary>Steam ID search by name (default: the Steam store search). Settable for fixtures, so tests never go online.</summary>
    public Func<string, CancellationToken, Task<(string Id, string Name)?>>? FindSteamId { get; init; }
    /// <summary>Asks before removing a game (default: a confirmation dialog). Settable for fixtures.</summary>
    public Func<string, Task<bool>>? ConfirmRemove { get; init; }
    /// <summary>Asks the new name of a game (default: a text dialog; null = cancelled). Settable for fixtures.</summary>
    public Func<string, Task<string?>>? AskName { get; init; }
    private readonly TextBlock preview = new() { Name = "LPreview", FontFamily = new FontFamily("monospace"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBlock steamNow = new() { Name = "LSteamNow", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel steamBox = new() { Name = "LSteamBox", IsVisible = false };
    private readonly TextBlock gfxNote = new() { Name = "LGfxNote", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel helpers = new() { Name = "LHelpers" };
    private readonly TextBox helperPath = new() { Name = "LHelperPath", PlaceholderText = L.T("/caminho/completo/do/programa") };
    private readonly CheckBox helperClose = new() { Name = "LHelperClose", Content = L.T("Fechar junto com o jogo"), IsChecked = true };
    private readonly StackPanel addBox = new() { Name = "LAddBox", IsVisible = false, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border emptyBox = new() { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(24), MinHeight = 232, VerticalAlignment = VerticalAlignment.Top };
    private readonly WrapPanel heroTags = new() { Name = "LHeroTags" };
    private readonly Border heroScrim = new() { IsHitTestVisible = false, IsVisible = false, Background = GameArt.Scrim() };
    private Border hero = null!;
    private bool heroArt;
    private readonly string? steamRoot;
    private readonly NativeArtFinder nativeArt = new();
    private readonly FlatpakApps flatpaks = new();
    private readonly bool hostMango = BoundedProcess.Find("mangohud") is not null;
    private readonly TextBlock mangoFlatpakStatus = new() { Name = "MangoFlatpakStatus", FontSize = 12, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap };
    private readonly Button mangoFlatpakInstall = new() { Name = "MangoFlatpakInstall", Content = L.T("Instalar MangoHud para Flatpak"), FontSize = 12, Padding = new Thickness(14, 7) };
    private readonly ProgressBar mangoFlatpakProgress = new() { IsIndeterminate = true, IsVisible = false, Height = 4, MinWidth = 120 };
    private readonly StackPanel mangoFlatpakBox = new() { Name = "MangoFlatpakBox", Spacing = 6, IsVisible = false, Margin = new Thickness(26, 0, 0, 8) };
    private string? mangoFlatpakBranch;
    private int columns = 2;
    private Grid? leftPane;
    private bool narrowWindow;
    private readonly Button backToLibrary = new() { Name = "BtnBackToLibrary", Content = L.T("← Biblioteca"), FontSize = 11, Padding = new Thickness(10, 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private UiKit.ChipGroup<string>? modeChips, resChips, styleChips, gfxPresetChips, gfxUpChips; private UiKit.ChipGroup<int>? hzChips;
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
            narrowWindow = e.NewSize.Width < 860;
            ColumnDefinitions[0].Width = new GridLength(narrowWindow ? 230 : 300);
            ApplyLayout();
        };
        // Console-style library (owner request 2026-11-08): with no game selected the library fills the page as a grid of covers;
        // Esc (or ← BIBLIOTECA) leaves the selected game and returns to it. Esc in a search with text clears the search first.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { Name: "LGameSearch" } box && !string.IsNullOrEmpty(box.Text)) { box.Text = ""; e.Handled = true; return; }
            if (current is not null) { var was = current; Select(null); FocusTile(was); e.Handled = true; }
        }, RoutingStrategies.Bubble); // bubble: an open drop-down handles its own Esc first
        backToLibrary.Click += (_, _) => { var was = current; Select(null); if (was is not null) FocusTile(was); };
        ToolTip.SetTip(backToLibrary, L.T("Volta para a biblioteca inteira (Esc)."));
        Loaded += async (_, _) => { if (games.Count == 0) await ScanAsync(); };
    }

    private void BuildLeft()
    {
        var scan = new Button { Name = "BtnScanGames", Content = L.T("Atualizar lista"), Padding = new Thickness(14, 8), FontSize = 12 };
        var add = new Button { Name = "BtnAddExe", Content = L.T("+ Adicionar executável"), Padding = new Thickness(14, 8), FontSize = 12 };
        scan.Click += async (_, _) => await ScanAsync(); add.Click += (_, _) => addBox.IsVisible = !addBox.IsVisible;
        // Fast exec: one disposable run of a Windows installer with Proton; the installed game can then be added here.
        var installWin = new Button { Name = "BtnFastExec", Content = L.T("Instalar jogo de Windows"), Padding = new Thickness(14, 8), FontSize = 12 };
        installWin.Click += async (_, _) =>
        {
            if (owner() is not { } window) return;
            await new FastExecDialog(async exe =>
            {
                if (settings.CustomGames.Any(c => c.Exe == exe)) return false;
                void addIt(IlmenyxSettings st) { if (!st.CustomGames.Any(c => c.Exe == exe)) st.CustomGames.Add(new() { Name = GameNameFromFolder(exe), Exe = exe }); }
                if (Save(addIt) is { } fresh) settings.CustomGames = fresh.CustomGames; else addIt(settings);
                await ScanAsync(); Select(games.FirstOrDefault(g => g.Store == "Manual" && g.Id == exe));
                await LookUpSteamIdAsync(automatic: true);
                return true;
            }, BoundedProcess.Find).ShowDialog(window);
        };
        search.TextChanged += (_, _) => Filter();
        var addPath = new TextBox { Name = "LAddPath", PlaceholderText = L.T("/caminho/completo/do/jogo") };
        AutomationProperties.SetName(addPath, L.T("Caminho completo do executável do jogo"));
        var addStatus = new TextBlock { Name = "LAddStatus", Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var confirm = UiKit.Primary("BtnAddExeConfirm", L.T("Adicionar"), 14, 6);
        confirm.Click += async (_, _) =>
        {
            string path = addPath.Text?.Trim() ?? "";
            if (!LinuxGameLibrary.IsRunnableFile(path) && !LinuxGameLibrary.IsWindowsExe(path)) { addStatus.Text = L.T("Escolha um jogo: um executável do Linux ou um .exe de Windows (que roda com Proton). Use Procurar… para abrir o explorador de arquivos."); return; }
            if (!settings.CustomGames.Any(c => c.Exe == path))
            {
                void add(IlmenyxSettings s) { if (!s.CustomGames.Any(c => c.Exe == path)) s.CustomGames.Add(new() { Name = LinuxGameLibrary.IsWindowsExe(path) ? GameNameFromFolder(path) : Path.GetFileNameWithoutExtension(path), Exe = path }); }
                if (Save(add) is { } fresh) settings.CustomGames = fresh.CustomGames; else add(settings);
            }
            addStatus.Text = "Adicionado."; await ScanAsync(); Select(games.FirstOrDefault(g => g.Store == "Manual" && g.Id == path));
            await LookUpSteamIdAsync(automatic: true);
        };
        var browseAdd = new Button { Name = "BtnAddExeBrowse", Content = L.T("Procurar…"), FontSize = 12, Padding = new Thickness(14, 6) };
        browseAdd.Click += async (_, _) => { if (await PickExecutableAsync() is { } picked) addPath.Text = picked; };
        addBox.Children.Add(addPath); addBox.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { browseAdd, confirm } }); addBox.Children.Add(addStatus);

        // Page header: title, library summary (live text) and the library actions.
        var header = UiKit.PageHeader(L.T("Jogos"), null, new WrapPanel { ItemSpacing = 8, LineSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { scan, add, installWin } });
        count.FontSize = 12.5; ((StackPanel)header.Children[0]).Children.Add(count);
        Grid.SetColumnSpan(header, 3); Children.Add(header);

        var searchBox = new Grid { Children = { search, SearchIcon() } };
        search.Padding = new Thickness(34, 7, 10, 7);
        var tools = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Spacing = 8, Children = { searchBox, addBox } };
        var left = new Grid { RowDefinitions = new("Auto,Auto,*") };
        var shelf = new TextBlock { Text = L.T("BIBLIOTECA"), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Faint, LetterSpacing = 0.6, Margin = new Thickness(4, 0, 0, 8) };
        Grid.SetRow(shelf, 1); Grid.SetRow(tools, 0);
        var scroll = new ScrollViewer { Name = "LGameScroll", Content = list, Padding = new Thickness(0, 0, 8, 0) }; Grid.SetRow(scroll, 2);
        left.Children.Add(tools); left.Children.Add(shelf); left.Children.Add(scroll);
        Grid.SetRow(left, 1); Children.Add(left); leftPane = left;
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
            Children = { UiKit.Icon(Icons.Games, 40, Palette.Faint), empty, new TextBlock { Text = L.T("Use + Adicionar executável ou Instalar jogo de Windows."), Foreground = Palette.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 340 } },
        };
        empty.HorizontalAlignment = HorizontalAlignment.Center; empty.FontSize = 14;
        // Empty state of the full-page library (nothing installed or nothing matches the search), under the search box.
        Grid.SetColumn(emptyBox, 0); Grid.SetColumnSpan(emptyBox, 3); Grid.SetRow(emptyBox, 1); emptyBox.Margin = new Thickness(0, 84, 0, 0); emptyBox.IsVisible = false; Children.Add(emptyBox);
        play.Click += async (_, _) => await PlayAsync();

        // Hero: the selected game (local Steam artwork when there is one, the brand gradient otherwise), its tags and the main action.
        title.FontSize = 26; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        sub.FontSize = 12.5; sub.MaxLines = 2; sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.Margin = new Thickness(0, 4, 0, 0);
        play.VerticalAlignment = VerticalAlignment.Bottom; play.Margin = new Thickness(16, 0, 0, 0); play.FontSize = 14;
        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), VerticalAlignment = VerticalAlignment.Bottom, Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { title, sub } } } };
        // Games the user added by hand can leave the library again (the files on disk are never touched).
        removeGame.VerticalAlignment = VerticalAlignment.Bottom; removeGame.Margin = new Thickness(16, 0, 0, 0);
        ToolTip.SetTip(removeGame, L.T("Tira o jogo da biblioteca do Ilmenyx. Os arquivos do jogo não são apagados."));
        removeGame.Click += async (_, _) => await RemoveCurrentAsync();
        renameGame.VerticalAlignment = VerticalAlignment.Bottom; renameGame.Margin = new Thickness(16, 0, 0, 0);
        ToolTip.SetTip(renameGame, L.T("Muda o nome que aparece na biblioteca."));
        renameGame.Click += async (_, _) => await RenameCurrentAsync();
        Grid.SetColumn(renameGame, 1); bottom.Children.Add(renameGame);
        removeGame.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(removeGame, 2); bottom.Children.Add(removeGame);
        Grid.SetColumn(play, 3); bottom.Children.Add(play);
        var heroTop = new DockPanel { Children = { backToLibrary, heroTags } }; heroTags.Margin = new Thickness(10, 0, 0, 0);
        var heroContent = new Grid { RowDefinitions = new("Auto,*,Auto"), Children = { heroTop } };
        Grid.SetRow(bottom, 2); heroContent.Children.Add(bottom);
        hero = UiKit.Hero(heroContent, height: 232, name: L.T("LHero"));
        if (hero.Child is Grid layers) layers.Children.Insert(0, heroScrim);

        ToolTip.SetTip(chkGuard, L.T("Em notebooks com duas GPUs, jogos de Windows em tela cheia sem vsync podem travar a tela inteira (só reiniciando). Com a proteção, o Proton sincroniza os quadros com o monitor; o FPS fica limitado à taxa da tela. Vale quando o jogo usa a GPU dedicada NVIDIA."));
        foreach (var c in new[] { chkPlan, chkGpu, chkGameMode, chkMango, chkGuard }) { c.Margin = new Thickness(0, 0, 0, 6); c.FontSize = 13; c.IsCheckedChanged += (_, _) => SaveFromControls(); }
        // Experimental Proton options (off by default): each asks before turning on and shows whether it applies to this game and session.
        ToolTip.SetTip(chkWayland, L.T("Faz o jogo de Windows abrir direto no Wayland, sem passar pelo XWayland (PROTON_ENABLE_WAYLAND=1). Pode melhorar a fluidez e o HDR, mas é experimental: alguns jogos abrem com janela errada, sem cursor ou sem controle. Só vale numa sessão Wayland com GE-Proton ou UMU-Proton 10 ou mais novo."));
        ToolTip.SetTip(chkLowLat, L.T("Libera o NVIDIA Reflex nos jogos que têm essa opção no menu (PROTON_ENABLE_NVAPI=1, pelo DXVK-NVAPI). Diminui o atraso entre o clique e a imagem; depois ligue o Reflex no menu do jogo. Só para placas NVIDIA com o driver proprietário."));
        foreach (var c in new[] { chkWayland, chkLowLat }) { c.Margin = new Thickness(0, 0, 0, 6); c.FontSize = 13; }
        chkWayland.IsCheckedChanged += async (_, _) => await ExtraToggledAsync(chkWayland, L.T("Ligar o Proton nativo no Wayland (experimental)?"),
            L.T("O jogo de Windows passa a abrir direto no Wayland (PROTON_ENABLE_WAYLAND=1), sem o XWayland.\n\n• Só vale numa sessão Wayland e com GE-Proton ou UMU-Proton 10 ou mais novo; fora disso a opção fica guardada e não entra.\n• É experimental: alguns jogos abrem com janela errada, sem cursor ou sem controle. Se der problema, desmarque.\n• Não combina com o Gamescope.\n• Vale só para este jogo."));
        chkLowLat.IsCheckedChanged += async (_, _) => await ExtraToggledAsync(chkLowLat, L.T("Ligar a baixa latência (experimental)?"),
            L.T("Libera o NVIDIA Reflex para o jogo (PROTON_ENABLE_NVAPI=1, pelo DXVK-NVAPI). Depois, ligue a opção Reflex no menu do próprio jogo.\n\n• Só para placas NVIDIA com o driver proprietário. Em AMD e Intel o Proton não documenta uma opção equivalente, então nada é ligado.\n• Não combina com opções que desligam o NVAPI ou trocam o DXVK pelo OpenGL (PROTON_USE_WINED3D).\n• Vale só para este jogo."));
        extrasBox.Children.Add(new TextBlock { Text = L.T("EXPERIMENTAL (JOGOS DE WINDOWS)"), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Muted, Margin = new Thickness(0, 0, 0, 6) });
        extrasBox.Children.Add(chkWayland); extrasBox.Children.Add(chkLowLat); extrasBox.Children.Add(extrasNote);
        ToolTip.SetTip(chkGpu, multiGpu ? L.T("Define as variáveis de PRIME offload para a GPU dedicada.") : L.T("Esta máquina tem uma única GPU: não há o que selecionar."));
        chkGpu.IsEnabled = multiGpu;
        bool hasGm = BoundedProcess.Find("gamemoderun") is not null, hasMango = hostMango;
        chkGameMode.IsEnabled = hasGm; chkMango.IsEnabled = hasMango;
        mangoFlatpakBox.Children.Add(mangoFlatpakStatus);
        mangoFlatpakBox.Children.Add(new WrapPanel { ItemSpacing = 10, LineSpacing = 6, Children = { mangoFlatpakInstall, mangoFlatpakProgress } });
        mangoFlatpakInstall.Click += async (_, _) => await InstallFlatpakMangoAsync();
        if (!hasGm) ToolTip.SetTip(chkGameMode, L.T("gamemoderun não está instalado.")); if (!hasMango) ToolTip.SetTip(chkMango, L.T("mangohud não está instalado."));
        var setExe = new Button { Name = "BtnSetExe", Content = L.T("Definir executável"), FontSize = 12, Padding = new Thickness(14, 7) }; setExe.Click += (_, _) => SetExe();
        var browseExe = new Button { Name = "BtnSetExeBrowse", Content = L.T("Procurar…"), FontSize = 12, Padding = new Thickness(14, 7) };
        browseExe.Click += async (_, _) => { if (await PickExecutableAsync(current?.InstallDir) is { } picked) { exePath.Text = picked; SetExe(); } };
        var recommended = new Button { Name = "BtnRecommended", Content = L.T("Perfil para mais FPS"), FontSize = 12, Padding = new Thickness(14, 7) }; recommended.Click += (_, _) => Recommended();
        exeText.Margin = new Thickness(0, 6, 0, 0);
        var perf = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Oc, L.T("DESEMPENHO")), chkPlan, chkGpu, chkGameMode, chkMango, mangoFlatpakBox, chkGuard, extrasBox, exeText, new Border { Height = 6 }, exePath, new Border { Height = 8 },
                new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { browseExe, setExe, recommended } }, protonBox,
                new TextBlock { Text = L.T("O perfil de energia vale só durante o jogo e volta ao anterior. Prioridade de processo elevada exige privilégios no Linux; o GameMode cuida disso quando instalado. O executável é descoberto sozinho; se errar, informe o caminho certo."), Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }
            }
        });

        void Chosen(Action<GameProfile> set) { if (loading || profile is null) return; set(profile); SaveProfile(current, profile); UpdatePreview(); }
        modeChips = new("LModePanel", [(L.T("Padrão do jogo"), "default"), (L.T("Tela cheia"), "full"), (L.T("Sem bordas"), "borderless"), (L.T("Janela"), "window")], "default", v => Chosen(p => p.WinMode = v));
        resChips = new("LResPanel", [(L.T("Padrão"), "default"), ("1280x720", "1280x720"), ("1920x1080", "1920x1080"), ("2560x1440", "2560x1440"), ("3840x2160", "3840x2160")], "default", v => Chosen(p => p.Res = v));
        // What the user plays with inside the game (not applied): recorded with each MangoHud session so the FPS page compares like with like.
        gfxPresetChips = new("LGfxPresetPanel", [(L.T("Não informado"), ""), (L.T("Baixo"), "Baixo"), (L.T("Médio"), "Médio"), (L.T("Alto"), "Alto"), ("Ultra", "Ultra")], "", v => Chosen(p => p.GfxPreset = v));
        gfxUpChips = new("LGfxUpscalePanel", [(L.T("Não informado"), ""), (L.T("Nativo"), "Native"), (L.T("Qualidade"), "Quality"), (L.T("Desempenho"), "Performance")], "", v => Chosen(p => p.GfxUpscaling = v));
        hzChips = new("LHzPanel", [(L.T("Padrão"), 0), ("60", 60), ("120", 120), ("144", 144), ("165", 165), ("240", 240)], 0, v => Chosen(p => p.Hz = v));
        styleChips = new("LStylePanel", [("Source / CS2", "source"), ("Unity", "unity"), ("Unreal", "unreal")], "source", v => Chosen(p => p.Style = v));
        extra.TextChanged += (_, _) => SaveFromControls();
        // Windows games (.exe) run with Proton through the bundled umu-launcher; the Steam ID lets it apply the game's known fixes.
        protonBox.Children.Add(UiKit.Label(L.T("PROTON (JOGO DE WINDOWS)")));
        var findId = new Button { Name = "BtnFindSteamId", Content = L.T("Buscar"), FontSize = 11, Padding = new Thickness(12, 5), Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(findId, L.T("Procura o ID pelo nome do jogo na loja Steam (só o nome é enviado)."));
        findId.Click += async (_, _) => await LookUpSteamIdAsync(automatic: false);
        var idRow = new DockPanel(); DockPanel.SetDock(findId, Dock.Right); idRow.Children.Add(findId); idRow.Children.Add(protonId);
        protonBox.Children.Add(new Border { Height = 6 }); protonBox.Children.Add(idRow); protonBox.Children.Add(protonIdStatus);
        var protonHelp = new TextBlock { Text = L.T("Roda com Proton, sem a Steam. Com o ID Steam, o Proton aplica as correções conhecidas do jogo."), Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        ToolTip.SetTip(protonHelp, L.T("Na primeira vez o Proton é baixado (~1 GB) e o \"Windows\" do jogo é preparado; demora alguns minutos. O ID é o número em store.steampowered.com/app/ID; o Ilmenyx tenta achá-lo pelo nome quando o jogo é adicionado."));
        protonBox.Children.Add(protonHelp);
        protonId.LostFocus += (_, _) => { if (current is { IsWindowsManual: true } cg && profile?.ProtonGameId is { Length: >= 3 } typed) FetchArtLater(cg, typed); };
        protonId.TextChanged += (_, _) =>
        {
            if (loading || profile is null) return;
            string digits = new((protonId.Text ?? "").Where(char.IsAsciiDigit).ToArray());
            if (digits != (protonId.Text ?? "")) { protonId.Text = digits; return; }
            profile.ProtonGameId = digits.Length > 0 ? digits : null; SaveProfile(current, profile); UpdateTags();
            protonIdStatus.Foreground = Palette.Faint; protonIdStatus.Text = digits.Length > 0 ? L.T("ID salvo. Vale na próxima vez que o jogo abrir.") : "";
        };
        var presets = new WrapPanel { Name = "LExtraPresets", Margin = new Thickness(0, 8, 0, 14), ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (label, token, hint) in new (string, string, string?)[] { (L.T("Pular intro (-novid)"), "-novid", null), (L.T("Sem limite de FPS (+fps_max 0)"), "+fps_max 0", null), (L.T("Sem joystick (-nojoy)"), "-nojoy", null), (L.T("Abrir console (-console)"), "-console", null),
            (L.T("Programas de terceiros (-allow_third_party_software)"), "-allow_third_party_software",
             L.T("CS2: libera programas que o modo confiável bloqueia, como a captura de jogo do OBS, ReShade e alguns overlays.\nAtenção: a Valve diz que usar esta opção pode reduzir o seu Fator de Confiança (Trust Factor) no matchmaking.")) })
        {
            var b = new Button { Content = label, FontSize = 11, Padding = new Thickness(11, 4), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Background = Palette.Card, Foreground = Palette.Muted, BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
            if (hint is not null) ToolTip.SetTip(b, hint);
            b.Click += (_, _) => { if (!(extra.Text ?? "").Contains(token, StringComparison.OrdinalIgnoreCase)) extra.Text = ((extra.Text ?? "").Trim() + " " + token).Trim(); };
            presets.Children.Add(b);
        }
        var steamApply = UiKit.Primary("BtnSteamApply", L.T("Aplicar nas opções da Steam")); steamApply.HorizontalAlignment = HorizontalAlignment.Left;
        steamApply.Click += async (_, _) => await ApplySteamAsync(steamApply);
        steamBox.Children.Add(steamNow); steamBox.Children.Add(steamApply);
        preview.FontSize = 12; preview.Margin = new Thickness(0);
        var code = new Border { Background = Palette.Hover, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 9), Margin = new Thickness(0, 6, 0, 12), Child = preview };
        Control Row(string label, Control chips) => new StackPanel { Margin = new Thickness(0, 0, 0, 12), Children = { new TextBlock { Text = label, Foreground = Palette.Muted, FontSize = 12 }, new Border { Height = 6 }, chips } };
        var gfx = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Gpu, L.T("GRÁFICOS (ARGUMENTOS DE INICIALIZAÇÃO)")), Row(L.T("Modo de janela"), Wrap(modeChips.Panel)), Row(L.T("Resolução"), Wrap(resChips.Panel)), Row(L.T("Taxa de atualização (Hz)"), Wrap(hzChips.Panel)), Row(L.T("Tipo de argumento (depende do jogo)"), Wrap(styleChips.Panel)),
                new TextBlock { Text = L.T("Qualidade usada dentro do jogo (para a Estimativa de FPS)"), FontWeight = FontWeight.SemiBold, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 4) },
                new TextBlock { Text = L.T("O Ilmenyx não muda isto no jogo. Diga a qualidade gráfica e o upscaling que você usa: cada sessão medida com MangoHud guarda esses valores e a resolução acima, e a Estimativa de FPS usa as sessões com a mesma qualidade para ajustar as estimativas dos outros jogos a este PC."), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) },
                Row(L.T("Qualidade gráfica"), Wrap(gfxPresetChips.Panel)), Row(L.T("Upscaling (DLSS / FSR)"), Wrap(gfxUpChips.Panel)),
                new TextBlock { Text = "Extras", Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) }, extra, presets, UiKit.Label(L.T("LINHA FINAL")), code, steamBox, gfxNote
            }
        });
        var addHelper = new Button { Name = "BtnAddHelper", Content = L.T("Adicionar"), FontSize = 12, Padding = new Thickness(14, 7) }; addHelper.Click += (_, _) => AddHelper();
        var help = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Apps, L.T("PROGRAMAS AUXILIARES")), new TextBlock { Text = L.T("Abrem antes do jogo (ex.: monitoramento) e podem fechar junto com ele. Iniciados sem shell, com os argumentos que você escrever após o caminho."), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) },
                helpers, helperPath, new WrapPanel { Margin = new Thickness(0, 10, 0, 0), ItemSpacing = 12, LineSpacing = 6, Children = { helperClose, addHelper } }
            }
        });
        status.Margin = new Thickness(4, 12, 0, 4);
        detail.Content = new StackPanel { Margin = new Thickness(0, 0, 8, 0), Children = { hero, playProgress, status, UiKit.SectionHeader(L.T("Perfil do jogo")), perf, gfx, help } };
        Grid.SetColumn(detail, 2); Grid.SetRow(detail, 1); Children.Add(detail);
    }

    /// <summary>Hero tags: store and the parts of the profile that are on (refreshed whenever the profile changes).</summary>
    private void UpdateTags()
    {
        heroTags.Children.Clear();
        if (current is not { } g || profile is not { } p) return;
        var tags = new List<string> { g.Kind ?? g.Store };
        if (p.HighPerfPlan) tags.Add(L.T("Alto desempenho"));
        if (p.GpuHigh && multiGpu) tags.Add(L.T("GPU dedicada"));
        if (p.GameMode && chkGameMode.IsEnabled) tags.Add("GameMode");
        if (p.MangoHud && chkMango.IsEnabled) tags.Add("MangoHud");
        if (g.IsWindowsManual) tags.Add(p.ProtonGameId is { } pid ? "Proton · ID " + pid : "Proton");
        foreach (var c in ExtraChoices().Where(c => c.Applies)) tags.Add(c.Id == GameExtras.WaylandId ? L.T("Wayland (exp.)") : L.T("Baixa latência (exp.)"));
        if (p.Res != "default") tags.Add(p.Res);
        if (p.Hz > 0) tags.Add(p.Hz + " Hz");
        if (tags.Count == 1) tags.Add(L.T("Perfil padrão"));
        if (p.Helpers.Count > 0) tags.Add(p.Helpers.Count == 1 ? L.T("1 programa auxiliar") : p.Helpers.Count + L.T(" programas auxiliares"));
        foreach (var t in tags) heroTags.Children.Add(UiKit.Tag(t, onImage: heroArt));
    }

    private string SettingsKey(LibGame g) => g.Key;
    /// <summary>Applies only this change to the current settings file (atomic; keeps what other pages and instances saved). Null on failure.</summary>
    private IlmenyxSettings? Save(Action<IlmenyxSettings> change)
    {
        try { return IlmenyxSettings.Update(settingsPath, change); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = L.T("Não foi possível salvar as configurações: ") + e.Message; return null; }
    }

    /// <summary>Writes this game's profile (only that entry) to the settings file.</summary>
    private void SaveProfile(LibGame? g, GameProfile p) { if (g is null) return; string key = SettingsKey(g); Save(s => s.GameProfiles[key] = p); }

    public async Task ScanAsync()
    {
        count.Text = L.T("Procurando seus jogos…");
        try
        {
            games = await library.ScanAsync(settings.CustomGames.ToList(), lifetime);
            Filter();
            count.Text = games.Count == 0 ? L.T("Nenhum jogo encontrado. Use + Adicionar executável para incluir um à mão.") : $"{games.Count} {(games.Count == 1 ? L.T("jogo") : L.T("jogos"))} · {string.Join(" · ", games.GroupBy(g => g.Kind ?? g.Store).Select(x => $"{x.Key} {x.Count()}"))}";
        }
        catch (OperationCanceledException) { count.Text = L.T("Busca cancelada."); }
    }

    private void Filter()
    {
        string q = (search.Text ?? "").Trim(); string? keep = current?.Key;
        list.Children.Clear();
        var shown = games.Where(g => q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        // Asymmetric grid: the selected game (or the first one) is a wide featured tile, the others pair up below it.
        string? big = current is null ? null : shown.Any(g => g.Key == keep) ? keep : shown.FirstOrDefault()?.Key; // browsing: all covers equal
        Grid? row = null; int col = 0;
        foreach (var g in shown)
        {
            var item = Tile(g, g.Key == big, g.Key == keep);
            AutomationProperties.SetName(item, g.Name + " (" + g.Subtitle + ")");
            var game = g; item.Click += (_, _) => { Select(game); FocusTile(game); };
            if (g.Key == big || columns == 1) { list.Children.Add(item); row = null; continue; }
            if (row is null || col >= columns) { row = new Grid { ColumnDefinitions = new(string.Join(",8,", Enumerable.Repeat("*", columns))), Margin = new Thickness(0, 0, 0, 8) }; list.Children.Add(row); col = 0; }
            Grid.SetColumn(item, col * 2); row.Children.Add(item); col++;
        }
        emptyBox.IsVisible = current is null && shown.Count == 0;
        empty.Text = games.Count == 0 ? L.T("Nenhum jogo na biblioteca.") : L.T("Nenhum jogo com esse nome.");
    }

    private Button Tile(LibGame g, bool large, bool selected)
    {
        var art = ArtFor(g, hero: false);
        var body = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                GameArt.Cover(art, g.Name, appIcon: art is null && g.Icon is not null ? GameArt.FromFile(nativeArt.Icon(g.Icon), false) : null, height: current is null ? 118 : large ? (columns == 1 ? 104 : 128) : (columns == 1 ? 0 : 68)),
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
    /// <summary>Browsing (no game selected): the library spans the whole page with as many cover columns as fit. A game selected:
    /// the classic two-pane layout (library on the left, details on the right).</summary>
    private void ApplyLayout(bool force = false)
    {
        if (leftPane is null) return;
        bool browse = current is null;
        Grid.SetColumnSpan(leftPane, browse ? 3 : 1);
        detail.IsVisible = !browse;
        double width = Bounds.Width > 0 ? Bounds.Width : 1100;
        int cols = browse ? Math.Clamp((int)((width - 20) / 230), 2, 8) : narrowWindow ? 1 : 2;
        if (cols != columns || force) { columns = cols; Filter(); }
    }

    private void FocusTile(LibGame g)
    {
        string name = "LGame_" + SafeName(g.Key);
        foreach (var b in list.Children.SelectMany(c => c is Grid r ? r.Children : [c]).OfType<Button>()) if (b.Name == name) { b.Focus(); return; }
    }

    public static string SafeName(string key) => new(key.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    public void Select(LibGame? g)
    {
        current = g;
        if (g is null) { profile = null; ApplyLayout(force: true); return; }
        ApplyLayout(force: true);
        if (!settings.GameProfiles.TryGetValue(SettingsKey(g), out var p)) { p = new GameProfile(); settings.GameProfiles[SettingsKey(g)] = p; }
        loading = true; profile = p;
        title.Text = g.Name; sub.Text = g.InstallDir.Length > 0 ? $"{g.Subtitle} · {g.InstallDir}" : g.ExePath is { } exe ? $"{g.Subtitle} · {exe} {g.Args}".TrimEnd() : g.Subtitle;
        var art = ArtFor(g, hero: true); heroArt = art is not null;
        if (art is null && g.IsWindowsManual && p.ProtonGameId is { Length: > 0 } artId) FetchArtLater(g, artId);
        hero.Background = art is null ? UiKit.HeroBrush() : new ImageBrush { Source = art, Stretch = Stretch.UniformToFill };
        heroScrim.IsVisible = heroArt; title.Foreground = heroArt ? Brushes.White : Palette.Fg; sub.Foreground = heroArt ? GameArt.OnImageMuted : Palette.Muted;
        chkPlan.IsChecked = p.HighPerfPlan; chkGpu.IsChecked = p.GpuHigh && multiGpu; chkGameMode.IsChecked = p.GameMode; chkMango.IsChecked = p.MangoHud; UpdateFlatpakMango(g); chkGuard.IsChecked = p.FullscreenGuard; chkGuard.IsVisible = multiGpu && gpuVendor == "NVIDIA";
        modeChips!.Select(p.WinMode); resChips!.Select(p.Res); hzChips!.Select(p.Hz); styleChips!.Select(p.Style); gfxPresetChips!.Select(p.GfxPreset ?? ""); gfxUpChips!.Select(p.GfxUpscaling ?? ""); extra.Text = p.Extra; exePath.Text = p.ExePath ?? g.ExePath ?? "";
        protonBox.IsVisible = g.IsWindowsManual; protonId.Text = p.ProtonGameId ?? ""; protonIdStatus.Text = "";
        removeGame.IsVisible = renameGame.IsVisible = g.Store == "Manual";
        chkWayland.IsChecked = p.ProtonWayland; chkLowLat.IsChecked = p.LowLatency; extrasBox.IsVisible = g.IsSteam || g.IsWindowsManual;
        protonName = g.IsSteam ? steam.CompatToolName(g.Id) : g.IsWindowsManual ? (UmuProton ?? (() => LinuxGameExtras.UmuProtonName()))() : null;
        RebuildHelpers();
        steamBox.IsVisible = g.IsSteam; steamNow.Text = g.IsSteam ? L.T("Opções atuais na Steam: ") + (steam.GetLaunchOptions(g.Id) is { Length: > 0 } cur ? cur : L.T("(nenhuma)")) : "";
        gfxNote.Text = g.IsSteam ? L.T("Os argumentos e variáveis só passam a valer na Steam depois de Aplicar nas opções da Steam (a Steam é fechada nessa hora). Depois, o Jogar abre pela Steam normalmente.")
            : g.LaunchUri is not null ? L.T("Esta loja abre o jogo por um atalho que não aceita argumentos: use as opções de inicialização da própria loja. Desempenho e programas auxiliares funcionam normalmente.")
            : L.T("Os argumentos são enviados direto ao executável quando você clica em Jogar.");
        status.Text = ""; loading = false; UpdatePreview(); ShowExe(g, p);
        Filter();
    }

    private void ShowExe(LibGame g, GameProfile p)
    {
        string? exe = p.ExePath ?? g.ExePath;
        if (exe is not null) { exeText.Text = L.T("Executável: ") + exe; return; }
        exeText.Text = L.T("Procurando o executável…");
        var game = g;
        _ = Task.Run(() => LinuxGameLibrary.FindMainExe(game.InstallDir, game.Name)).ContinueWith(t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (current != game) return;
            exeText.Text = t.Result is { } found ? L.T($"Executável (detectado, confirme): {found}") : L.T("Executável não identificado. Informe o caminho abaixo, se necessário.");
            if (t.Result is { } f && string.IsNullOrWhiteSpace(exePath.Text)) exePath.Text = f;
        }));
    }

    private void SaveFromControls()
    {
        if (loading || profile is null) return;
        profile.HighPerfPlan = chkPlan.IsChecked == true; profile.GpuHigh = chkGpu.IsChecked == true; profile.GameMode = chkGameMode.IsChecked == true; profile.MangoHud = chkMango.IsChecked == true; profile.FullscreenGuard = chkGuard.IsChecked == true;
        profile.ProtonWayland = chkWayland.IsChecked == true; profile.LowLatency = chkLowLat.IsChecked == true;
        profile.Extra = (extra.Text ?? "").Trim();
        SaveProfile(current, profile); UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (profile is null) return;
        string a = GameLaunchOptions.BuildArgs(profile);
        string o = current?.IsSteam == true ? GameLaunchOptions.BuildSteamOptions(profile, gpuVendor, File.Exists(MangoHudLogReader.ConfigPath()) ? MangoHudLogReader.ConfigPath() : null, ExtrasNow()) : a;
        preview.Text = o.Length > 0 ? o : L.T("(nenhum argumento)");
        var choices = ExtraChoices();
        extrasNote.Text = choices.Count == 0 ? L.T("Desligadas. Opções novas do Proton, para testar jogo a jogo; cada uma explica o que faz antes de ligar.")
            : GameExtras.Summary(choices) + (current?.IsWindowsManual == true ? "" : current?.IsSteam == true ? L.T("\nNa Steam, entram depois de Aplicar nas opções da Steam.") : "");
        UpdateTags();
    }

    /// <summary>What the experimental options depend on for the selected game: session, Proton build (Steam mapping or umu), NVIDIA proprietary rendering.</summary>
    private ExtrasContext? ExtrasNow() => current is { } g && profile is { } p && (g.IsSteam || g.IsWindowsManual)
        ? new(WaylandSession?.Invoke() ?? LinuxGameExtras.WaylandSession(), GameExtras.ParseProton(protonName), gpuVendor == "NVIDIA" && (!multiGpu || p.GpuHigh), true)
        : null;

    private IReadOnlyList<ExtraChoice> ExtraChoices() => profile is { } p && ExtrasNow() is { } ctx ? GameExtras.Resolve(p, ctx, GameLaunchOptions.OwnPieces(p, gpuVendor)) : [];

    /// <summary>Turning an experimental option on asks first (explained); declining unticks it. Turning off needs no question.</summary>
    private async Task ExtraToggledAsync(CheckBox box, string question, string explanation)
    {
        if (loading || profile is null) return;
        if (box.IsChecked == true && (owner() is not { } window || !await ConfirmDialog.AskAsync(window, question, explanation, L.T("Ligar"), L.T("Cancelar"))))
        {
            loading = true; box.IsChecked = false; loading = false;
            return;
        }
        SaveFromControls();
    }

    private void Recommended()
    {
        if (profile is null) return;
        loading = true; chkPlan.IsChecked = true; chkGpu.IsChecked = multiGpu; chkGameMode.IsChecked = chkGameMode.IsEnabled; loading = false; SaveFromControls();
        status.Text = L.T("Perfil para mais FPS ligado: Alto desempenho") + (multiGpu ? L.T(", GPU dedicada") : "") + (chkGameMode.IsEnabled ? ", GameMode" : "") + ".";
    }

    /// <summary>Readable game name for an installed executable: its top game folder (".../Jogos/My Game/Binaries/Win64/x.exe" -> "My Game").</summary>
    private static string GameNameFromFolder(string exe)
    {
        string? dir = Path.GetDirectoryName(exe);
        string[] generic = ["binaries", "retail", "win64", "win32", "bin", "bin64", "x64", "x86", "game"];
        for (int i = 0; i < 5 && dir is { Length: > 1 }; i++)
        {
            string n = Path.GetFileName(dir);
            if (!generic.Contains(n.ToLowerInvariant())) return n;
            dir = Path.GetDirectoryName(dir);
        }
        return Path.GetFileNameWithoutExtension(exe);
    }

    /// <summary>System file chooser for the game's executable (Linux program, AppImage, script or Windows .exe). Null when cancelled.</summary>
    private async Task<string?> PickExecutableAsync(string? startFolder = null)
    {
        if (owner() is not { } window) return null;
        var storage = window.StorageProvider;
        Avalonia.Platform.Storage.IStorageFolder? start = null;
        string from = startFolder is { Length: > 0 } f && Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try { start = await storage.TryGetFolderFromPathAsync(new Uri(from)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = L.T("Escolha o executável do jogo"), AllowMultiple = false, SuggestedStartLocation = start,
            FileTypeFilter = [new(L.T("Jogos (Linux e Windows)")) { Patterns = ["*.exe", "*.EXE", "*.x86_64", "*.x86", "*.sh", "*.AppImage", "*"] }, new(L.T("Todos os arquivos")) { Patterns = ["*"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Artwork of a library game: Steam's local cache; for a hand-added Windows game with a Steam ID, that ID's local Steam
    /// cache or the image already downloaded for it (Steam CDN, kept in the Compatibility cache).</summary>
    private Bitmap? ArtFor(LibGame g, bool hero)
    {
        if (g.IsSteam) return GameArt.Steam(steamRoot, g.Id, hero);
        if (g.Store == "Linux")
        {
            if (nativeArt.Cached(g.Id) is { } shot) return GameArt.FromFile(shot, hero);
            FetchNativeArtLater(g); return null;
        }
        if (!g.IsWindowsManual || !settings.GameProfiles.TryGetValue(SettingsKey(g), out var gp) || gp.ProtonGameId is not { Length: > 0 } id) return null;
        return GameArt.Steam(steamRoot, id, hero) ?? GameArt.FromFile(ArtCache.CachedArt(id), hero);
    }

    private static readonly CompatOnline ArtCache = new(CompatPaths.Folder());
    private readonly HashSet<string> artTried = new(StringComparer.Ordinal);
    /// <summary>Downloads the Steam artwork of an ID (default: Steam CDN through the Compatibility cache). Settable for fixtures.</summary>
    public Func<string, CancellationToken, Task<string?>>? FetchArt { get; init; }

    /// <summary>Once per ID and session: fetches the game's Steam image (only the ID goes in the address), then repaints the library and hero.</summary>
    private void FetchArtLater(LibGame g, string id)
    {
        if (!artTried.Add(id)) return;
        _ = Task.Run(async () =>
        {
            string? file = null;
            try { file = await (FetchArt ?? ((i, ct) => ArtCache.ArtAsync(i, ct)))(id, lifetime); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { }
            if (file is null) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { Filter(); if (current?.Key == g.Key) Select(current); });
        });
    }

    /// <summary>Once per game and session: the AppStream screenshot of a Linux game (catalog read and download off the UI thread), then repaints.</summary>
    private void FetchNativeArtLater(LibGame g)
    {
        if (!artTried.Add(g.Key)) return;
        _ = Task.Run(async () =>
        {
            string? file = null;
            try { file = await nativeArt.ScreenshotAsync(g.Id, g.Kind == "Linux · Flatpak", lifetime); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or TaskCanceledException) { }
            if (file is null) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { Filter(); if (current?.Key == g.Key) Select(current); });
        });
    }

    /// <summary>
    /// A Flatpak game needs MangoHud's Flatpak extension for its runtime (the host mangohud does not reach the sandbox): the option is enabled
    /// only with it; otherwise the card says so and offers the user-scope install (no password).
    /// </summary>
    private void UpdateFlatpakMango(LibGame g)
    {
        mangoFlatpakBox.IsVisible = false; mangoFlatpakBranch = null;
        string? app = g.ExePath is { } exe && Path.GetFileName(exe) == "flatpak" ? GameProcesses.FlatpakApp(g) : null;
        if (app is null) { chkMango.IsEnabled = hostMango; ToolTip.SetTip(chkMango, hostMango ? null : L.T("mangohud não está instalado.")); return; }
        string? branch = flatpaks.Metadata(app).RuntimeBranch;
        bool ok = branch is not null && flatpaks.HasMangoHud(branch);
        chkMango.IsEnabled = ok;
        ToolTip.SetTip(chkMango, ok ? L.T($"Jogo Flatpak: usa a extensão MangoHud para Flatpak (runtime {branch}).") : L.T("Jogo Flatpak: o MangoHud do sistema não entra no Flatpak; precisa da extensão MangoHud para Flatpak."));
        if (ok) return;
        mangoFlatpakBox.IsVisible = true; mangoFlatpakProgress.IsVisible = false; mangoFlatpakStatus.Foreground = Palette.Warn;
        mangoFlatpakBranch = branch;
        mangoFlatpakInstall.IsVisible = branch is not null;
        mangoFlatpakStatus.Text = branch is null ? L.T("Não consegui ler o runtime deste Flatpak; o MangoHud fica desligado para ele.") : L.T($"MangoHud para Flatpak não instalado (runtime {branch}).");
        ToolTip.SetTip(mangoFlatpakStatus, L.T("Jogos Flatpak rodam isolados do sistema. O overlay e o FPS medido precisam da extensão oficial ") + FlatpakLaunch.MangoHudExtension + L.T(" na versão do runtime do jogo."));
    }

    private async Task InstallFlatpakMangoAsync()
    {
        if (mangoFlatpakBranch is not { } branch || current is not { } game) return;
        if (owner() is { } window && !await ConfirmDialog.AskAsync(window, L.T("MangoHud para Flatpak"), L.T($"Instalar a extensão {FlatpakLaunch.MangoHudExtension} ({branch}) do Flathub?\n\nSó para o seu usuário, sem senha. Precisa de internet."), L.T("Instalar"), L.T("Cancelar"))) return;
        mangoFlatpakInstall.IsEnabled = false; mangoFlatpakProgress.IsVisible = true;
        mangoFlatpakStatus.Foreground = Palette.Muted; mangoFlatpakStatus.Text = L.T("Baixando a extensão do Flathub…");
        (int Code, string Output) r;
        try { r = await BoundedProcess.RunAsync("flatpak", FlatpakApps.InstallMangoHud(branch), TimeSpan.FromMinutes(10), 64 * 1024, lifetime); }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { r = (-1, e.Message); }
        catch (OperationCanceledException) { return; }
        mangoFlatpakInstall.IsEnabled = true; mangoFlatpakProgress.IsVisible = false;
        if (r.Code == 0 && flatpaks.HasMangoHud(branch))
        {
            if (current == game) UpdateFlatpakMango(game);
            status.Text = L.T("MangoHud para Flatpak instalado. Marque MangoHud para usar o overlay neste jogo.");
            return;
        }
        string last = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        mangoFlatpakStatus.Foreground = Palette.Warn;
        mangoFlatpakStatus.Text = L.T("Não consegui instalar") + (last.Length > 0 ? ": " + (last.Length > 160 ? last[..160] + "…" : last) : ".");
    }

    /// <summary>Fills the Steam ID of the selected Windows game from its name (automatic: only when empty, quietly when nothing is found).</summary>
    private async Task LookUpSteamIdAsync(bool automatic)
    {
        if (current is not { IsWindowsManual: true } game || profile is not { } p) return;
        if (automatic && p.ProtonGameId is { Length: > 0 }) return;
        protonIdStatus.Foreground = Palette.Faint; protonIdStatus.Text = L.T("Buscando o ID Steam…");
        var find = FindSteamId ?? ((n, ct) => SteamIds.FindAsync(n, ct));
        var found = await find(game.Name, lifetime);
        if (found is null && GameNameFromFolder(game.Id) is { } folderName && folderName != game.Name) found = await find(folderName, lifetime); // "MK10" → "Mortal Kombat XL"
        if (current != game) return;
        if (found is { } f)
        {
            loading = true; protonId.Text = f.Id; loading = false;
            p.ProtonGameId = f.Id; SaveProfile(game, p); UpdateTags();
            protonIdStatus.Text = L.T($"ID encontrado na loja Steam: {f.Name}.");
            FetchArtLater(game, f.Id);
        }
        else protonIdStatus.Text = automatic ? "" : p.ProtonGameId is { Length: > 0 } kept ? L.T($"Nenhum jogo com esse nome na loja Steam. O ID {kept} continua salvo.") : L.T("Nenhum jogo com esse nome na loja Steam. Digite o ID no campo.");
    }

    /// <summary>Renames the selected hand-added game in the library (its profile and files stay the same).</summary>
    private async Task RenameCurrentAsync()
    {
        if (current is not { Store: "Manual" } game) return;
        string? name = AskName is { } ask ? await ask(game.Name)
            : owner() is { } window ? await TextInputDialog.AskAsync(window, L.T("Renomear jogo"), L.T("Nome que aparece na biblioteca"), game.Name) : null;
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name == game.Name) return;
        if (name.Length > 80) name = name[..80];
        string exe = game.Id;
        void rename(IlmenyxSettings s) { foreach (var c in s.CustomGames.Where(c => c.Exe == exe)) c.Name = name; }
        if (Save(rename) is { } fresh) settings.CustomGames = fresh.CustomGames; else rename(settings);
        await ScanAsync(); Select(games.FirstOrDefault(g => g.Store == "Manual" && g.Id == exe));
        status.Text = L.T("Nome alterado.");
    }

    /// <summary>Removes the selected hand-added game (and its profile) from the library after confirmation. Never deletes files.</summary>
    private async Task RemoveCurrentAsync()
    {
        if (current is not { Store: "Manual" } game) return;
        bool yes = ConfirmRemove is { } ask ? await ask(game.Name)
            : owner() is { } window && await ConfirmDialog.AskAsync(window, L.T("Remover jogo"), L.T($"Remover \"{game.Name}\" da biblioteca?\n\nOs arquivos do jogo não são apagados."), L.T("Remover"), L.T("Cancelar"));
        if (!yes) return;
        string key = SettingsKey(game), exe = game.Id;
        void remove(IlmenyxSettings s) { s.CustomGames.RemoveAll(c => c.Exe == exe); s.GameProfiles.Remove(key); }
        if (Save(remove) is { } fresh) { settings.CustomGames = fresh.CustomGames; settings.GameProfiles = fresh.GameProfiles; } else remove(settings);
        Select(null); await ScanAsync();
    }

    private void SetExe()
    {
        if (current is null || profile is null) return;
        string path = (exePath.Text ?? "").Trim();
        if (!LinuxGameLibrary.IsRunnableFile(path) && !LinuxGameLibrary.IsWindowsExe(path)) { status.Text = L.T("Caminho inválido: escolha um executável do Linux ou um .exe de Windows (Procurar… abre o explorador de arquivos)."); return; }
        profile.ExePath = path; SaveProfile(current, profile); ShowExe(current, profile); status.Text = L.T("Executável definido.");
    }

    private void RebuildHelpers()
    {
        helpers.Children.Clear();
        if (profile is null) return;
        foreach (var h in profile.Helpers.ToList())
        {
            var remove = new Button { Content = L.T("Remover"), FontSize = 11, Padding = new Thickness(10, 4) };
            var grid = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
            var text = new TextBlock { Text = h.Display + (h.CloseWithGame ? L.T("  ·  fecha com o jogo") : ""), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(remove, 1); grid.Children.Add(text); grid.Children.Add(remove);
            remove.Click += (_, _) => { profile.Helpers.Remove(h); SaveProfile(current, profile); RebuildHelpers(); UpdateTags(); };
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
            if (parts.Count == 0 || !LinuxGameLibrary.IsRunnableFile(parts[0])) { status.Text = L.T("Informe o caminho completo de um programa executável (e, opcionalmente, seus argumentos)."); return; }
            profile.Helpers.Add(new HelperProgram { Path = parts[0], Args = string.Join(" ", parts.Skip(1).Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)), CloseWithGame = helperClose.IsChecked == true });
            SaveProfile(current, profile); helperPath.Text = ""; RebuildHelpers(); UpdateTags();
        }
        catch (FormatException e) { status.Text = e.Message; }
    }

    private async Task ApplySteamAsync(Button button)
    {
        if (current is not { IsSteam: true } g || profile is null || owner() is not { } window) return;
        string options = GameLaunchOptions.BuildSteamOptions(profile, gpuVendor, profile.MangoHud && OperatingSystem.IsLinux() ? MangoHudLogReader.EnsureConfig(settings) : File.Exists(MangoHudLogReader.ConfigPath()) ? MangoHudLogReader.ConfigPath() : null, ExtrasNow());
        button.IsEnabled = false;
        try
        {
            if (steam.IsRunning)
            {
                if (!await ConfirmDialog.AskAsync(window, L.T("Jogos · Steam"), L.T("A Steam precisa ser fechada para gravar as opções de inicialização (senão ela as sobrescreve ao sair).\n\nFechar a Steam agora? Jogos abertos serão encerrados. É feito um backup do arquivo antes de alterar."))) return;
                status.Text = L.T("Fechando a Steam…");
                if (!await steam.ShutdownAsync(lifetime)) { status.Text = L.T("A Steam não fechou. Feche-a manualmente e tente de novo."); return; }
            }
            string? error = await Task.Run(() => steam.SetLaunchOptions(g.Id, options));
            status.Text = error ?? (options.Length > 0 ? L.T($"Opções gravadas na Steam: {options}. Clique em Jogar (a Steam abre sozinha).") : L.T("Opções da Steam limpas para este jogo."));
            steamNow.Text = L.T("Opções atuais na Steam: ") + (steam.GetLaunchOptions(g.Id) is { Length: > 0 } cur ? cur : L.T("(nenhuma)"));
        }
        catch (OperationCanceledException) { status.Text = "Cancelado."; }
        finally { button.IsEnabled = true; }
    }

    private async Task PlayAsync()
    {
        if (launching is not null) { launching.Cancel(); return; }
        if (current is not { } g || profile is not { } p) return;
        launching = CancellationTokenSource.CreateLinkedTokenSource(lifetime); play.Content = L.T("Parar acompanhamento");
        playProgress.IsVisible = true; status.Text = L.T($"Abrindo {g.Name}…");
        if (p.MangoHud && OperatingSystem.IsLinux()) MangoHudLogReader.EnsureConfig(settings); // overlay + FPS log ready without visiting the Overlay page
        try
        {
            var session = await launch(g, p, gpuVendor, t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    status.Text = t;
                    if (t.Contains(L.T(" em execução"), StringComparison.Ordinal) || t.StartsWith(L.T("Não detectei"), StringComparison.Ordinal)) playProgress.IsVisible = false;
                }),
                exe => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (p.ExePath != exe) { p.ExePath = exe; SaveProfile(g, p); if (current == g) exeText.Text = L.T("Executável: ") + exe; } }), launching.Token);
            // Measured on this PC (MangoHud log of this launch): kept locally for the FPS page, at most FpsSession.MaxEntries sessions.
            if (session is { IsValid: true }) { if (Save(s => s.AddFpsSession(session)) is { } fresh) settings.FpsSessions = fresh.FpsSessions; else settings.AddFpsSession(session); }
        }
        finally { launching.Dispose(); launching = null; play.Content = L.T("Jogar"); playProgress.IsVisible = false; }
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

    /// <summary>Artwork downloaded for a hand-added game (Steam CDN cache of the Compatibility page); a miss is not remembered, so a later download shows up.</summary>
    public static Bitmap? FromFile(string? path, bool hero)
    {
        if (path is null || !File.Exists(path)) return null;
        string key = path + "|" + (hero ? "1280" : "460");
        lock (Cache) if (Cache.TryGetValue(key, out var hit) && hit is not null) return hit;
        lock (Cache) Cache.Remove(key);
        return Load(path, hero ? 1280 : 460);
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
    /// <param name="appIcon">Shown centered when there is no artwork (Linux games without a screenshot), instead of the initials.</param>
    public static Border Cover(Bitmap? art, string name, double height, Bitmap? appIcon = null)
    {
        var b = new Border { Height = height, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius + 2), ClipToBounds = true };
        if (art is not null) { b.Background = new ImageBrush { Source = art, Stretch = Stretch.UniformToFill }; return b; }
        b.Background = UiKit.HeroBrush();
        if (appIcon is not null)
        {
            b.Child = new Image { Source = appIcon, Height = Math.Max(16, height * 0.62), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return b;
        }
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
