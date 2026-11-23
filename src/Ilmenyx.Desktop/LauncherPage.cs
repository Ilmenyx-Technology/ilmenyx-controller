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
using Avalonia.VisualTree;
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
    /// <summary>Entries joined into a visible game (same program), by the visible game's key. Display only: settings and profiles keep them.</summary>
    private IReadOnlyDictionary<string, IReadOnlyList<LibGame>> merged = new Dictionary<string, IReadOnlyList<LibGame>>();
    private LibGame? current; private GameProfile? profile;
    // Per game key: the follow-up of a launch (RunAsync), the achievement watch, and the games whose process was seen running.
    private readonly Dictionary<string, CancellationTokenSource> following = [], watchers = [];
    private readonly HashSet<string> running = [], opened = [], launched = [];
    private readonly Button closeGame = new() { Name = "BtnCloseGame", Content = L.T("Fechar jogo"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 0) };
    private readonly HashSet<string> closing = [];
    // While the selected game is opening or running: one discreet note per card ("Vale na próxima vez…") and the controls that must not change under a live game.
    private readonly List<TextBlock> liveNotes = [];
    private readonly List<(Control Control, object? Tip, string Why)> liveLocks = [];
    private readonly Dictionary<string, GameProfile> launchProfiles = []; // the profile each live launch started with
    private bool stopping, installingMango;
    private readonly Button stopWatch = new() { Name = "BtnStopWatch", Content = L.T("Parar acompanhamento"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 0) };
    private Avalonia.Threading.DispatcherTimer? poller; private bool attached, ticking;
    /// <summary>Whether a game's process exists (game, executable). Default: <see cref="GameProcesses"/> on /proc. Settable for fixtures.</summary>
    public Func<LibGame, string?, CancellationToken, Task<bool>>? IsRunning { get; init; }
    private bool loading;
    private readonly TextBlock count = new() { Name = "LGameCount", Text = L.T("Procurando seus jogos…"), Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Name = "LGameSearch", PlaceholderText = L.T("Buscar jogo…") };
    private readonly StackPanel list = new() { Name = "LGameList", Spacing = 8 };
    private readonly TextBlock empty = new() { Name = "LEmpty", Text = L.T("Escolha um jogo na lista."), Foreground = Palette.Muted };
    private readonly ScrollViewer detail = new() { Name = "LDetail", IsVisible = false };
    private readonly TextBlock title = new() { Name = "LTitle", FontSize = 32, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock also = new() { Name = "LAlso", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = false };
    private readonly TextBlock sub = new() { Name = "LSub", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button play = UiKit.Primary("BtnPlay", L.T("Jogar"), 40, 14);
    // Visual feedback between JOGAR and the game's window (Proton can take a while): a moving bar until the game process is seen.
    private readonly ProgressBar playProgress = new() { Name = "LPlayProgress", IsIndeterminate = true, IsVisible = false, Height = 4, MinHeight = 4, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock status = new() { Name = "LStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly CheckBox chkPlan = new() { Name = "LChkPlan", Content = L.T("Perfil de energia Alto desempenho enquanto joga (volta ao anterior depois)") };
    private readonly CheckBox chkGpu = new() { Name = "LChkGpu", Content = L.T("Forçar a placa de vídeo dedicada (PRIME offload)") };
    private readonly CheckBox chkGameMode = new() { Name = "LChkGameMode", Content = L.T("GameMode (gamemoderun)") };
    private readonly CheckBox chkMango = new() { Name = "LChkMango", Content = L.T("MangoHud (overlay de desempenho)") };
    private readonly CheckBox chkMeasure = new() { Name = "LChkMeasure", Content = L.T("Medir FPS (sem mostrar nada na tela)") };
    private readonly TextBlock measureNote = new() { Name = "LMeasureNote", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, -2, 0, 8) };
    private readonly CheckBox chkGuard = new() { Name = "LChkGuard", Content = L.T("Proteção contra travamento em tela cheia (vsync)") };
    private readonly CheckBox chkWayland = new() { Name = "LChkWayland", Content = GameExtras.WaylandLabel };
    private readonly CheckBox chkLowLat = new() { Name = "LChkLowLat", Content = GameExtras.LowLatencyLabel };
    private readonly TextBlock extrasNote = new() { Name = "LExtrasNote", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
    private readonly StackPanel extrasBox = new() { Name = "LExtrasBox", IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
    private string? protonName;
    /// <summary>The one reusable notice for settings that only take effect at launch; shown on every card that has them while the game is live.</summary>
    private TextBlock LiveNote(string? text = null, Thickness? margin = null)
    {
        var note = new TextBlock { Name = "LLiveNote" + liveNotes.Count, Text = text ?? L.T("Vale na próxima vez que o jogo abrir."), Foreground = Palette.Muted, FontSize = 11, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = margin ?? new Thickness(0, 0, 0, 8) };
        liveNotes.Add(note); return note;
    }

    /// <summary>A control that is disabled (with the reason as its tip) while the selected game is live.</summary>
    private void LockWhileLive(Control c, string why) { liveLocks.Add((c, ToolTip.GetTip(c), why)); ToolTip.SetShowOnDisabled(c, true); }

    private readonly Dictionary<CheckBox, TextBlock> changeNotes = [];
    /// <summary>A launch-only setting changed under a live game: says so next to the control and in the status line (the card notice alone sits above the group and is easy to miss).</summary>
    private void NoteLiveChange(CheckBox c)
    {
        if (loading || !IsLive(current?.Key)) return;
        string text = L.T($"{(c.Content as string ?? "").Split(" (")[0]}: vale na próxima vez que o jogo abrir.");
        if (!changeNotes.TryGetValue(c, out var n))
        {
            n = new TextBlock { Name = "LChangeNote_" + c.Name, Foreground = Palette.AccentText, FontSize = 11, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, -2, 0, 6) };
            if (c.Parent is Panel p) p.Children.Insert(p.Children.IndexOf(c) + 1, n);
            changeNotes[c] = n;
        }
        n.Text = text; n.IsVisible = true; status.Text = text;
    }

    private bool IsLive(string? key) => key is not null && (following.ContainsKey(key) || running.Contains(key));

    private void UpdateLive()
    {
        bool live = IsLive(current?.Key);
        foreach (var n in liveNotes) n.IsVisible = live;
        if (!live) foreach (var n in changeNotes.Values) n.IsVisible = false;
        foreach (var (c, tip, why) in liveLocks) { c.IsEnabled = !live; ToolTip.SetTip(c, live ? why : tip); }
        foreach (var undo in achFixBox.GetVisualDescendants().OfType<Button>()) undo.IsEnabled = !live;
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control { IsEffectivelyEnabled: false }) (play.IsEnabled ? play : backToLibrary).Focus();
    }
    /// <summary>Wayland session check (default: the real environment). Settable for fixtures.</summary>
    public Func<bool>? WaylandSession { get; init; }
    /// <summary>Newest UMU-Proton umu already downloaded (default: the real folder). Settable for fixtures.</summary>
    public Func<string?>? UmuProton { get; init; }
    private readonly TextBlock exeText = new() { Name = "LExeText", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox exePath = new() { Name = "LExePath", PlaceholderText = L.T("/caminho/completo/do/executavel") };
    private readonly TextBox extra = new() { Name = "LExtra", PlaceholderText = L.T("ex.: -novid -high +fps_max 0") };
    private readonly TextBox protonId = new() { Name = "LProtonId", PlaceholderText = L.T("ID Steam do jogo (opcional), ex.: 307780"), MaxLength = 10 };
    private readonly StackPanel achFixBox = new() { Name = "LAchFixBox", IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel protonBox = new() { Name = "LProtonBox", IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
    private readonly ComboBox runnerBox = new() { Name = "LRunner", HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private readonly TextBlock prefixText = new() { Name = "LPrefix", FontSize = 11.5, FontFamily = IlmenyxTheme.Mono, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock runnerStatus = new() { Name = "LRunnerStatus", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private List<(string Label, string? Value)> runnerChoices = [];
    private const string PickRunner = "\0pick"; // the "Escolher pasta…" entry of the runner list
    private readonly TextBlock protonIdStatus = new() { Name = "LProtonIdStatus", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button removeGame = new() { Name = "BtnRemoveGame", Content = L.T("Remover"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false };
    private readonly Button hideGame = new() { Name = "BtnHideGame", Content = L.T("Ocultar da biblioteca"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false };
    private readonly CheckBox showHidden = new() { Name = "LShowHidden", Content = L.T("Mostrar ocultos"), FontSize = 12 };
    private readonly Button coverFile = new() { Name = "BtnCoverFile", Content = L.T("Escolher imagem…"), FontSize = 11, Padding = new Thickness(12, 6) };
    private readonly Button coverUrl = new() { Name = "BtnCoverUrl", Content = L.T("Usar link…"), FontSize = 11, Padding = new Thickness(12, 6) };
    private readonly Button coverReset = new() { Name = "BtnCoverReset", Content = L.T("Remover capa"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false };
    private readonly TextBlock coverStatus = new() { Name = "LCoverStatus", Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button gameShortcut = new() { Name = "BtnGameShortcut", Content = L.T("Criar atalho"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false };
    /// <summary>Folder for a new shortcut (true = desktop, false = app menu); null = none. Settable for fixtures; default the user's real folders.</summary>
    public Func<bool, string?>? ShortcutFolder { get; init; }
    private readonly Button renameGame = new() { Name = "BtnRenameGame", Content = L.T("Renomear"), FontSize = 11, Padding = new Thickness(12, 6), IsVisible = false };
    private static readonly SteamAppLookup SteamIds = new();
    /// <summary>Steam ID search by name (default: the Steam store search). Settable for fixtures, so tests never go online.</summary>
    public Func<string, CancellationToken, Task<(string Id, string Name)?>>? FindSteamId { get; init; }
    /// <summary>Similar-name suggestions (game name, refused ids); default: the Steam store search. Settable for fixtures.</summary>
    public Func<string, IReadOnlySet<string>, CancellationToken, Task<IReadOnlyList<(string Id, string Name)>>>? SuggestSteamId { get; init; }
    private readonly StackPanel suggestBox = new() { Name = "SuggestBox", IsVisible = false, Margin = new Thickness(0, 10, 0, 0), Spacing = 6 };
    private readonly TextBlock suggestText = new() { Name = "SuggestText", FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private List<(string Id, string Name)> suggestions = [];
    private Action<string, string>? applySuggestion;
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
    private readonly TextBlock fpsBig = new() { Name = "FpsMeasuredTypical", FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Palette.Fg };
    private readonly TextBlock fpsRange = new() { Name = "FpsMeasuredRange", FontSize = 13, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock fpsInfo = new() { Name = "FpsMeasuredInfo", FontSize = 12, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock fpsEmpty = new() { Name = "FpsMeasuredEmpty", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox fpsGroupBox = new() { Name = "FpsMeasuredGroup", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
    private List<FpsGroup> fpsGroups = []; private bool loadingGroups;
    private readonly TextBlock qualityDetected = new() { Name = "QualityDetected", FontSize = 12, Foreground = Palette.Fg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock qualityActive = new() { Name = "QualityActive", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private DetectedQuality? detectedQuality; private int detectGen;
    /// <summary>Reads the quality the game itself uses (default: its Unreal/Unity settings file). Settable for fixtures.</summary>
    public Func<LibGame, GameProfile, Task<DetectedQuality?>>? DetectQuality { get; init; }
    private UiKit.ChipGroup<string>? modeChips, resChips, styleChips, gfxPresetChips, gfxUpChips; private UiKit.ChipGroup<int>? hzChips;
    public IReadOnlyList<LibGame> Games => games;
    public GameProfile? Profile => profile;

    /// <summary>The page with the real library and launcher (full app and the games-only window).</summary>
    public static LauncherPage Create(Func<Window?> owner, CancellationToken lifetime)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var gpus = new LinuxGpuCollector().Collect(PciIds.Load());
        // PRIME variables depend on the driver, not only the vendor: NVIDIA on nouveau is offloaded with DRI_PRIME like Mesa GPUs.
        string vendor = gpus.Count > 0 ? (gpus[0].Card.Vendor == "NVIDIA" && gpus[0].Raw.Driver != "nvidia" ? "NVIDIA-Mesa" : gpus[0].Card.Vendor) : "";
        bool multi = gpus.Count(x => !x.Raw.Integrated) + gpus.Count(x => x.Raw.Integrated) > 1;
        // Manual games: the experimental Proton options (when they apply) and, with the Otimiza option on, the bigger shader cache variables.
        return new LauncherPage(new LinuxGameLibrary(home), new SteamInfo(home), IlmenyxSettings.DefaultPath(),
            (g, p, v, status, learned, ct) => new LinuxGameLauncher().RunAsync(g, p, v, status, learned, ct,
                LinuxGameExtras.ManualContext(g, p, v, multi, LinuxGameExtras.UmuProtonName(home)), new LinuxShaderCache(home).IsOn), owner, vendor, multi, lifetime) { Margin = new Thickness(0, 4, 8, 12) };
    }

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
        showHidden.IsCheckedChanged += (_, _) =>
        {
            if (current is { } g && showHidden.IsChecked != true && IsHidden(g)) Select(null); else Filter();
            UpdateCount();
        };
        var addPath = new TextBox { Name = "LAddPath", PlaceholderText = L.T("/caminho/completo/do/jogo") };
        AutomationProperties.SetName(addPath, L.T("Caminho completo do executável do jogo"));
        var addStatus = new TextBlock { Name = "LAddStatus", Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var confirm = UiKit.Primary("BtnAddExeConfirm", L.T("Adicionar"), 14, 6);
        confirm.Click += async (_, _) =>
        {
            string path = DocumentPortal.RealPath(addPath.Text?.Trim() ?? "");
            if (!LinuxGameLibrary.IsRunnableFile(path) && !LinuxGameLibrary.IsWindowsExe(path)) { addStatus.Text = L.T("Escolha um jogo: um executável do Linux ou um .exe de Windows (que roda com Proton). Use Procurar… para abrir o explorador de arquivos."); return; }
            if (!settings.CustomGames.Any(c => c.Exe == path))
            {
                void add(IlmenyxSettings s) { if (!s.CustomGames.Any(c => c.Exe == path)) s.CustomGames.Add(new() { Name = LinuxGameLibrary.IsWindowsExe(path) ? GameNameFromFolder(DocumentPortal.HostPath(path)) : Path.GetFileNameWithoutExtension(path), Exe = path }); }
                if (Save(add) is { } fresh) settings.CustomGames = fresh.CustomGames; else add(settings);
            }
            addStatus.Text = "Adicionado."; await ScanAsync(); Select(games.FirstOrDefault(g => g.Store == "Manual" && g.Id == path));
            await LookUpSteamIdAsync(automatic: true);
        };
        var browseAdd = new Button { Name = "BtnAddExeBrowse", Content = L.T("Procurar…"), FontSize = 12, Padding = new Thickness(14, 6) };
        browseAdd.Click += async (_, _) => { if (await PickExecutableAsync() is { } picked) addPath.Text = picked; };
        addBox.Children.Add(addPath); addBox.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { browseAdd, confirm } }); addBox.Children.Add(addStatus);

        // Page header: title, library summary (live text) and the library actions.
        // A menu/desktop entry that opens only this page, in its own light window (no monitor, no tray), for whoever just wants to play.
        var gamesShortcut = new Button { Name = "BtnGamesShortcut", Content = L.T("Atalho dos Jogos"), Padding = new Thickness(14, 8), FontSize = 12 };
        ToolTip.SetTip(gamesShortcut, L.T("Cria um atalho que abre só os Jogos, numa janela leve, sem o resto do Ilmenyx."));
        gamesShortcut.Flyout = ShortcutMenu(desktop => CreateShortcut(desktop, null));
        var header = UiKit.PageHeader(L.T("Jogos"), null, new WrapPanel { ItemSpacing = 8, LineSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { scan, add, installWin, gamesShortcut } });
        count.FontSize = 12.5; ((StackPanel)header.Children[0]).Children.Add(count);
        Grid.SetColumnSpan(header, 3); Children.Add(header);

        var searchBox = new Grid { Children = { search, SearchIcon() } };
        search.Padding = new Thickness(34, 7, 10, 7);
        var tools = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Spacing = 8, Children = { searchBox, showHidden, addBox } };
        // The empty-library card sits over this column: keep it below the add box, or it covers Procurar…/Adicionar (seen 2026-11-12).
        tools.SizeChanged += (_, _) => emptyBox.Margin = new Thickness(0, 84 + (addBox.IsVisible ? addBox.Bounds.Height + tools.Spacing : 0), 0, 0);
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
        closeGame.Click += async (_, _) => await CloseGameAsync();
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); // screen readers hear Abrindo…, Em execução, Encerrado and errors
        AutomationProperties.SetLiveSetting(suggestText, AutomationLiveSetting.Polite);
        stopWatch.Click += async (_, _) => await StopWatchingAsync();

        AttachedToVisualTree += (_, _) => { attached = true; EnsureTimer(); if (current is { } c) _ = DetectSelectedAsync(c); };
        DetachedFromVisualTree += (_, _) => { attached = false; EnsureTimer(); };

        // Hero: the selected game (local Steam artwork when there is one, the brand gradient otherwise), its tags and the main action.
        title.FontSize = 26; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        sub.FontSize = 12.5; sub.MaxLines = 2; sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.Margin = new Thickness(0, 4, 0, 0);
        play.VerticalAlignment = VerticalAlignment.Bottom; play.Margin = new Thickness(16, 0, 0, 0); play.FontSize = 14;
        // Title and subtitle on top, the actions wrap underneath them: at 900 px wide or with 150-200% text the row must not push Jogar out of the column.
        var bottom = new Grid { RowDefinitions = new("Auto,Auto"), VerticalAlignment = VerticalAlignment.Bottom, Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { title, sub, also } } } };
        // Games the user added by hand can leave the library again (the files on disk are never touched).
        ToolTip.SetTip(removeGame, L.T("Tira o jogo da biblioteca do Ilmenyx. Os arquivos do jogo não são apagados."));
        removeGame.Click += async (_, _) => await RemoveCurrentAsync();
        ToolTip.SetTip(renameGame, L.T("Muda o nome que aparece na biblioteca."));
        renameGame.Click += async (_, _) => await RenameCurrentAsync();
        LockWhileLive(removeGame, L.T("Feche o jogo para tirá-lo da biblioteca."));
        LockWhileLive(renameGame, L.T("Feche o jogo para renomear: o nome ajuda a achar o processo dele."));
        ToolTip.SetTip(gameShortcut, L.T("Atalho que abre este jogo direto pelo Ilmenyx, com o perfil daqui."));
        gameShortcut.Flyout = ShortcutMenu(desktop => CreateShortcut(desktop, current));
        // Games that are not manual cannot be removed (they come from a store or a menu entry): they can be hidden instead.
        ToolTip.SetTip(hideGame, L.T("Esconde o jogo da biblioteca sem apagar nada. Marque Mostrar ocultos para vê-lo de novo."));
        hideGame.Click += (_, _) => ToggleHideCurrent();
        var actions = new WrapPanel { Name = "LHeroActions", ItemSpacing = 8, LineSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { gameShortcut, renameGame, removeGame, hideGame, stopWatch, closeGame, play } };
        foreach (var c in actions.Children.OfType<Control>()) { c.Margin = new Thickness(0); c.VerticalAlignment = VerticalAlignment.Center; }
        Grid.SetRow(actions, 1); bottom.Children.Add(actions);
        var heroTop = new DockPanel { Children = { backToLibrary, heroTags } }; heroTags.Margin = new Thickness(10, 0, 0, 0);
        var heroContent = new Grid { RowDefinitions = new("Auto,*,Auto"), Children = { heroTop } };
        Grid.SetRow(bottom, 2); heroContent.Children.Add(bottom);
        hero = UiKit.Hero(heroContent, height: 232, name: L.T("LHero")); hero.Height = double.NaN; hero.MinHeight = 232; // grows when the actions wrap
        if (hero.Child is Grid layers) layers.Children.Insert(0, heroScrim);

        ToolTip.SetTip(chkGuard, L.T("Em notebooks com duas GPUs, jogos de Windows em tela cheia sem vsync podem travar a tela inteira (só reiniciando). Com a proteção, o Proton sincroniza os quadros com o monitor; o FPS fica limitado à taxa da tela. Vale quando o jogo usa a GPU dedicada NVIDIA."));
        foreach (var c in new[] { chkPlan, chkGpu, chkGameMode, chkMango, chkMeasure, chkGuard }) { c.Margin = new Thickness(0, 0, 0, 6); c.FontSize = 13; c.IsCheckedChanged += (_, _) => { SaveFromControls(); NoteLiveChange(c); }; }
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
        ToolTip.SetTip(chkMeasure, L.T("O MangoHud entra escondido só para gravar o FPS e alimentar o DESEMPENHO MEDIDO. Desligue em jogos com anti-cheat ou incompatíveis com MangoHud."));
        if (!hasGm) ToolTip.SetTip(chkGameMode, L.T("gamemoderun não está instalado.")); if (!hasMango) ToolTip.SetTip(chkMango, L.T("mangohud não está instalado."));
        var setExe = new Button { Name = "BtnSetExe", Content = L.T("Definir executável"), FontSize = 12, Padding = new Thickness(14, 7) }; setExe.Click += (_, _) => SetExe();
        var browseExe = new Button { Name = "BtnSetExeBrowse", Content = L.T("Procurar…"), FontSize = 12, Padding = new Thickness(14, 7) };
        string exeWhy = L.T("Feche o jogo para trocar o executável: o Ilmenyx o usa para saber se o jogo está aberto.");
        LockWhileLive(exePath, exeWhy); LockWhileLive(setExe, exeWhy); LockWhileLive(browseExe, exeWhy);
        browseExe.Click += async (_, _) => { if (await PickExecutableAsync(current?.InstallDir) is { } picked) { exePath.Text = picked; SetExe(); } };
        var recommended = new Button { Name = "BtnRecommended", Content = L.T("Perfil para mais FPS"), FontSize = 12, Padding = new Thickness(14, 7) }; recommended.Click += (_, _) => Recommended();
        exeText.Margin = new Thickness(0, 6, 0, 0);
        var perf = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Oc, L.T("DESEMPENHO")), LiveNote(), chkPlan, chkGpu, chkGameMode, chkMango, mangoFlatpakBox, chkMeasure, measureNote, chkGuard, extrasBox, exeText, new Border { Height = 6 }, exePath, new Border { Height = 8 },
                new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { browseExe, setExe, recommended } }, LiveNote(L.T("Feche o jogo para trocar o executável."), new Thickness(0, 6, 0, 0)), suggestBox, protonBox, achFixBox,
                new TextBlock { Text = L.T("O perfil de energia vale só durante o jogo e volta ao anterior. Prioridade de processo elevada exige privilégios no Linux; o GameMode cuida disso quando instalado. O executável é descoberto sozinho; se errar, informe o caminho certo."), Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }
            }
        });

        void Chosen(Action<GameProfile> set) { if (loading || profile is null) return; set(profile); SaveProfile(current, profile); UpdatePreview(); }
        modeChips = new("LModePanel", [(L.T("Padrão do jogo"), "default"), (L.T("Tela cheia"), "full"), (L.T("Sem bordas"), "borderless"), (L.T("Janela"), "window")], "default", v => Chosen(p => p.WinMode = v));
        resChips = new("LResPanel", [(L.T("Padrão"), "default"), ("1280x720", "1280x720"), ("1920x1080", "1920x1080"), ("2560x1440", "2560x1440"), ("3840x2160", "3840x2160")], "default", v => Chosen(p => p.Res = v));
        // What the user plays with inside the game (not applied): recorded with each MangoHud session, which the measured-FPS card groups by resolution and quality.
        gfxPresetChips = new("LGfxPresetPanel", [(L.T("Automático"), ""), (L.T("Baixo"), "Baixo"), (L.T("Médio"), "Médio"), (L.T("Alto"), "Alto"), ("Ultra", "Ultra")], "", v => { Chosen(p => p.GfxPreset = v); UpdateQualityActive(); });
        gfxUpChips = new("LGfxUpscalePanel", [(L.T("Não informado"), ""), (L.T("Nativo"), "Native"), (L.T("Qualidade"), "Quality"), (L.T("Desempenho"), "Performance")], "", v => Chosen(p => p.GfxUpscaling = v));
        hzChips = new("LHzPanel", [(L.T("Padrão"), 0), ("60", 60), ("120", 120), ("144", 144), ("165", 165), ("240", 240)], 0, v => Chosen(p => p.Hz = v));
        styleChips = new("LStylePanel", [("Source / CS2", "source"), ("Unity", "unity"), ("Unreal", "unreal")], "source", v => Chosen(p => p.Style = v));
        extra.TextChanged += (_, _) => SaveFromControls();
        // Windows games (.exe) run with Proton through the bundled umu-launcher; the Steam ID lets it apply the game's known fixes.
        protonBox.Children.Add(UiKit.Label(L.T("PROTON (JOGO DE WINDOWS)")));
        var findId = new Button { Name = "BtnFindSteamId", Content = L.T("Buscar"), FontSize = 11, Padding = new Thickness(12, 5), Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(findId, L.T("Procura o ID pelo nome do jogo na loja Steam (só o nome é enviado)."));
        findId.Click += async (_, _) => await LookUpSteamIdAsync(automatic: false);
        var yes = new Button { Name = "BtnSuggestYes", Content = L.T("Sim"), FontSize = 11, Padding = new Thickness(14, 5) };
        var no = new Button { Name = "BtnSuggestNo", Content = L.T("Não"), FontSize = 11, Padding = new Thickness(14, 5) };
        yes.Click += (_, _) => { if (suggestions.Count > 0 && applySuggestion is { } apply) { var s = suggestions[0]; HideSuggestion(); apply(s.Id, s.Name); } };
        no.Click += (_, _) => RejectSuggestion();
        suggestBox.Children.Add(suggestText); suggestBox.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { yes, no } });
        var idRow = new DockPanel(); DockPanel.SetDock(findId, Dock.Right); idRow.Children.Add(findId); idRow.Children.Add(protonId);
        protonBox.Children.Add(new Border { Height = 6 }); protonBox.Children.Add(idRow); protonBox.Children.Add(protonIdStatus);
        var protonHelp = new TextBlock { Text = L.T("Roda com Proton, sem a Steam. Com o ID Steam, o Proton aplica as correções conhecidas do jogo."), Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        ToolTip.SetTip(protonHelp, L.T("Na primeira vez o Proton é baixado (~1 GB) e o \"Windows\" do jogo é preparado; demora alguns minutos. O ID é o número em store.steampowered.com/app/ID; o Ilmenyx tenta achá-lo pelo nome quando o jogo é adicionado."));
        protonBox.Children.Add(protonHelp);
        // Which Proton or Wine runs the game, and where its "C:" drive (Wine prefix) lives. Automatic unless the user chooses.
        protonBox.Children.Add(new Border { Height = 12 }); protonBox.Children.Add(UiKit.Label(L.T("VERSÃO DO PROTON OU WINE")));
        AutomationProperties.SetName(runnerBox, L.T("Versão do Proton ou Wine"));
        var fetchRunner = new Button { Name = "BtnRunnerDownload", Content = L.T("Baixar versão…"), FontSize = 11, Padding = new Thickness(12, 5), Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(fetchRunner, L.T("Baixa uma versão específica do GE-Proton ou do Proton (UMU-Proton) pelo GitHub e já seleciona para este jogo."));
        fetchRunner.Click += async (_, _) =>
        {
            if (profile is null || owner() is not { } window) return;
            var dialog = (RunnerDialog ?? (() => new RunnerDownloadDialog()))();
            await dialog.ShowDialog(window);
            if (dialog.Installed is not { } folder || profile is null) return;
            profile.Runner = folder; SaveProfile(current, profile); LoadRunner(profile); protonName = Path.GetFileName(folder);
            runnerStatus.Foreground = Palette.Faint; runnerStatus.Text = L.T($"{Path.GetFileName(folder)} baixado e selecionado para este jogo.");
        };
        var runnerRow = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetColumn(fetchRunner, 1); runnerRow.Children.Add(runnerBox); runnerRow.Children.Add(fetchRunner);
        protonBox.Children.Add(runnerRow);
        string runnerWhy = L.T("Feche o jogo para trocar a versão do Proton ou o prefixo.");
        LockWhileLive(runnerBox, runnerWhy); LockWhileLive(fetchRunner, runnerWhy);
        runnerBox.SelectionChanged += async (_, _) =>
        {
            if (loading || profile is null || runnerBox.SelectedIndex < 0 || runnerBox.SelectedIndex >= runnerChoices.Count) return;
            string? value = runnerChoices[runnerBox.SelectedIndex].Value;
            if (value == PickRunner)
            {
                string? folder = await PickFolderAsync(L.T("Escolha a pasta do Proton ou do Wine"));
                if (folder is null || !GameRunners.IsRunner(folder))
                {
                    LoadRunner(profile);
                    if (folder is not null) { runnerStatus.Foreground = Palette.Warn; runnerStatus.Text = L.T("Esta pasta não é um Proton nem um Wine: falta o arquivo \"proton\" ou \"bin/wine\" dentro dela."); }
                    return;
                }
                value = folder;
            }
            profile.Runner = value; SaveProfile(current, profile); LoadRunner(profile);
            protonName = value is not null && Path.IsPathFullyQualified(value) ? Path.GetFileName(value) : (UmuProton ?? (() => LinuxGameExtras.UmuProtonName()))();
            runnerStatus.Foreground = Palette.Faint; runnerStatus.Text = L.T("Salvo. Vale na próxima vez que o jogo abrir.");
        };
        protonBox.Children.Add(new Border { Height = 12 }); protonBox.Children.Add(UiKit.Label(L.T("PREFIXO DO WINE (O \"C:\" DO JOGO)")));
        var pickPrefix = new Button { Name = "BtnPrefixPick", Content = L.T("Escolher pasta…"), FontSize = 11, Padding = new Thickness(12, 5), Margin = new Thickness(8, 0, 0, 0) };
        var defaultPrefix = new Button { Name = "BtnPrefixDefault", Content = L.T("Padrão"), FontSize = 11, Padding = new Thickness(12, 5), Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(pickPrefix, L.T("Use uma pasta vazia ou um prefixo que já existe (de outro launcher, por exemplo). Saves e instalações do prefixo anterior não são movidos."));
        pickPrefix.Click += async (_, _) =>
        {
            if (profile is null || await PickFolderAsync(L.T("Escolha a pasta do prefixo do Wine")) is not { } folder) return;
            if (GameRunners.PrefixProblem(folder) is { } problem) { runnerStatus.Foreground = Palette.Warn; runnerStatus.Text = problem; return; }
            profile.WinePrefix = folder; SaveProfile(current, profile); LoadRunner(profile);
            runnerStatus.Foreground = Palette.Faint; runnerStatus.Text = L.T("Salvo. Vale na próxima vez que o jogo abrir.");
        };
        defaultPrefix.Click += (_, _) => { if (profile is null) return; profile.WinePrefix = null; SaveProfile(current, profile); LoadRunner(profile); runnerStatus.Foreground = Palette.Faint; runnerStatus.Text = L.T("Prefixo padrão do Ilmenyx de volta."); };
        LockWhileLive(pickPrefix, runnerWhy); LockWhileLive(defaultPrefix, runnerWhy);
        var prefixRow = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetColumn(pickPrefix, 1); Grid.SetColumn(defaultPrefix, 2);
        prefixRow.Children.Add(prefixText); prefixRow.Children.Add(pickPrefix); prefixRow.Children.Add(defaultPrefix);
        protonBox.Children.Add(prefixRow); protonBox.Children.Add(runnerStatus); protonBox.Children.Add(LiveNote(runnerWhy, new Thickness(0, 4, 0, 0)));
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
                CardTitle(Icons.Gpu, L.T("GRÁFICOS (ARGUMENTOS DE INICIALIZAÇÃO)")), LiveNote(), Row(L.T("Modo de janela"), Wrap(modeChips.Panel)), Row(L.T("Resolução"), Wrap(resChips.Panel)), Row(L.T("Taxa de atualização (Hz)"), Wrap(hzChips.Panel)), Row(L.T("Tipo de argumento (depende do jogo)"), Wrap(styleChips.Panel)),
                new TextBlock { Text = "Extras", Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) }, extra, presets, UiKit.Label(L.T("LINHA FINAL")), code, steamBox, gfxNote
            }
        });
        var quality = UiKit.Card(new StackPanel
        {
            Name = "QualityCard",
            Children =
            {
                CardTitle(Icons.Gpu, L.T("QUALIDADE NO JOGO")),
                new TextBlock { Text = L.T("O Ilmenyx lê, sem alterar nada, o arquivo de configuração do próprio jogo (Unreal e Unity) para saber a qualidade gráfica e a resolução em uso. Cada sessão medida guarda esse valor, e o desempenho medido deste jogo é separado por resolução e qualidade."), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) },
                LiveNote(), qualityDetected, qualityActive,
                Row(L.T("Qualidade gráfica"), Wrap(gfxPresetChips.Panel)), Row(L.T("Upscaling (DLSS / FSR)"), Wrap(gfxUpChips.Panel)),
            }
        });
        var measured = UiKit.Card(new StackPanel { Name = "FpsMeasuredCard", Children = { CardTitle(Icons.Fps, L.T("DESEMPENHO MEDIDO")), fpsGroupBox, fpsBig, fpsRange, fpsInfo, fpsEmpty } });
        measured.Margin = new Thickness(0, 12, 0, 0);
        fpsGroupBox.SelectionChanged += (_, _) => { if (!loadingGroups && fpsGroupBox.SelectedIndex >= 0) ShowGroup(fpsGroupBox.SelectedIndex); };
        AutomationProperties.SetName(fpsGroupBox, L.T("Resolução e qualidade do desempenho mostrado"));
        AutomationProperties.SetName(fpsBig, L.T("FPS típico medido")); 
        var addHelper = new Button { Name = "BtnAddHelper", Content = L.T("Adicionar"), FontSize = 12, Padding = new Thickness(14, 7) }; addHelper.Click += (_, _) => AddHelper();
        var help = UiKit.Card(new StackPanel
        {
            Children =
            {
                CardTitle(Icons.Apps, L.T("PROGRAMAS AUXILIARES")), LiveNote(), new TextBlock { Text = L.T("Abrem antes do jogo (ex.: monitoramento) e podem fechar junto com ele. Iniciados sem shell, com os argumentos que você escrever após o caminho."), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) },
                helpers, helperPath, new WrapPanel { Margin = new Thickness(0, 10, 0, 0), ItemSpacing = 12, LineSpacing = 6, Children = { helperClose, addHelper } }
            }
        });
        status.Margin = new Thickness(4, 12, 0, 4);
                coverFile.Click += async (_, _) => await ChooseCoverFileAsync();
        coverUrl.Click += async (_, _) => await ChooseCoverUrlAsync();
        coverReset.Click += (_, _) => ResetCover();
        ToolTip.SetTip(coverFile, L.T("Copia a imagem para o cache do Ilmenyx; o arquivo original não é usado nem alterado."));
        ToolTip.SetTip(coverUrl, L.T("Baixa a imagem do link (só https) quando você confirma."));
        var coverCard = UiKit.Card(new StackPanel { Children = { CardTitle(Icons.Games, L.T("CAPA")), new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { coverFile, coverUrl, coverReset } }, coverStatus } });
        coverCard.Margin = new Thickness(0, 12, 0, 0);
        detail.Content = new StackPanel { Margin = new Thickness(0, 0, 8, 0), Children = { hero, playProgress, status, coverCard, measured, UiKit.SectionHeader(L.T("Perfil do jogo")), perf, quality, gfx, help } };
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
            // Games added through the Flatpak file chooser before 0.34.2 were saved under a portal alias: random name, no cover, lone file.
            if (settings.CustomGames.Any(c => DocumentPortal.IsAlias(c.Exe)))
            {
                bool move(IlmenyxSettings s) => DocumentPortal.Migrate(s, x => DocumentPortal.RealPath(x), GameNameFromFolder);
                if (Save(s => move(s)) is { } fresh) { settings.CustomGames = fresh.CustomGames; settings.GameProfiles = fresh.GameProfiles; } else move(settings);
            }
            var groups = GameDedup.Group(await library.ScanAsync(settings.CustomGames.ToList(), lifetime),
                x => DocumentPortal.IsAlias(x) ? (DocumentPortal.HostPath(x) is var host && host != x ? host : null) : x, ProfileData);
            games = groups.Select(x => x.Game).ToList(); merged = groups.Where(x => x.Merged.Count > 0).ToDictionary(x => x.Game.Key, x => x.Merged);
            Filter();
            UpdateCount();
        }
        catch (OperationCanceledException) { count.Text = L.T("Busca cancelada."); }
    }

    private IReadOnlyList<LibGame> MergedOf(LibGame g) => merged.GetValueOrDefault(g.Key) ?? [];

    /// <summary>Profile data of an entry, to pick which of two hand-added duplicates stays visible: FPS sessions by name, own cover.</summary>
    private int ProfileData(LibGame g) => settings.FpsSessions.Count(f => string.Equals(f.Game, g.Name, StringComparison.OrdinalIgnoreCase)) + (OwnCover(g) is null ? 0 : 1);

    /// <summary>Hidden keys, where hiding any entry of a joined group hides the group (the other entry does not bring it back).</summary>
    private HashSet<string> HiddenKeys() => GameDedup.HiddenKeys(settings.HiddenGames, merged);

    private bool IsHidden(LibGame g) => HiddenKeys().Contains(g.Key);

    private List<LibGame> Visible() => GameCovers.Visible(games, HiddenKeys(), showHidden.IsChecked == true).ToList();
    private List<LibGame> Shown() { string q = (search.Text ?? "").Trim(); return Visible().Where(g => q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList(); }

    private int HiddenCount() => showHidden.IsChecked == true ? 0 : games.Count(IsHidden);

    private void UpdateCount()
    {
        var v = Visible();
        count.Text = v.Count == 0 ? HiddenCount() is > 0 and var hidden ? L.T($"Jogos ocultos: {hidden}. Marque Mostrar ocultos para vê-los.") : L.T("Nenhum jogo encontrado. Use + Adicionar executável para incluir um à mão.") : $"{v.Count} {(v.Count == 1 ? L.T("jogo") : L.T("jogos"))} · {string.Join(" · ", v.GroupBy(g => g.Kind ?? g.Store).Select(x => $"{x.Key} {x.Count()}"))}";
    }

    private void Filter()
    {
        string? keep = current?.Key;
        list.Children.Clear();
        var shown = Shown();
        // Asymmetric grid: the selected game (or the first one) is a wide featured tile, the others pair up below it.
        string? big = current is null ? null : shown.Any(g => g.Key == keep) ? keep : shown.FirstOrDefault()?.Key; // browsing: all covers equal
        Grid? row = null; int col = 0;
        foreach (var g in shown)
        {
            var item = Tile(g, g.Key == big, g.Key == keep);
            AutomationProperties.SetName(item, g.Name + " (" + g.Subtitle + ")");
            if (IsHidden(g)) item.Opacity = 0.55;
            var game = g; item.Click += (_, _) => { Select(game); FocusTile(game); };
            if (g.Key == big || columns == 1) { list.Children.Add(item); row = null; continue; }
            if (row is null || col >= columns) { row = new Grid { ColumnDefinitions = new(string.Join(",8,", Enumerable.Repeat("*", columns))), Margin = new Thickness(0, 0, 0, 8) }; list.Children.Add(row); col = 0; }
            Grid.SetColumn(item, col * 2); row.Children.Add(item); col++;
        }
        emptyBox.IsVisible = current is null && shown.Count == 0;
        empty.Text = games.Count == 0 ? L.T("Nenhum jogo na biblioteca.") : Visible().Count == 0 && HiddenCount() is > 0 and var hiddenN ? L.T($"Jogos ocultos: {hiddenN}. Marque Mostrar ocultos para vê-los.") : L.T("Nenhum jogo com esse nome.");
    }

    private Button Tile(LibGame g, bool large, bool selected)
    {
        var art = ArtFor(g, hero: false);
        var body = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                GameArt.Cover(art, g.Name, appIcon: art is null && IconFor(g) is { } icon ? GameArt.FromFile(nativeArt.Icon(icon), false) : null, height: current is null ? 118 : large ? (columns == 1 ? 104 : 128) : (columns == 1 ? 0 : 68)),
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
        double width = Bounds.Width > 0 ? Bounds.Width : 1100;
        // Very narrow (a minimum window with 200% text): the game's detail takes the whole page; ← Biblioteca (or Esc) goes back to the library.
        bool single = !browse && width < 560;
        Grid.SetColumnSpan(leftPane, browse ? 3 : 1); leftPane.IsVisible = !single;
        Grid.SetColumn(detail, single ? 0 : 2); Grid.SetColumnSpan(detail, single ? 3 : 1);
        detail.IsVisible = !browse;
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
        current = g; HideSuggestion();
        if (g is null) { profile = null; also.IsVisible = false; ApplyLayout(force: true); UpdateButtons(); return; }
        ApplyLayout(force: true);
        if (!settings.GameProfiles.TryGetValue(SettingsKey(g), out var p)) { p = new GameProfile(); settings.GameProfiles[SettingsKey(g)] = p; }
        loading = true; profile = p;
        foreach (var n in changeNotes.Values) n.IsVisible = false;
        title.Text = g.Name; sub.Text = g.InstallDir.Length > 0 ? $"{g.Subtitle} · {g.InstallDir}" : g.ExePath is { } exe ? $"{g.Subtitle} · {exe} {g.Args}".TrimEnd() : g.Subtitle;
        var art = ArtFor(g, hero: true); heroArt = art is not null;
        if (art is null && g.Store == "Manual")
        {
            if ((p.ProtonGameId ?? p.ArtSteamId) is { Length: > 0 } artId) FetchArtLater(g, artId);
            else if (!g.IsWindowsManual && p.CoverPath is null && artTried.Add("lookup:" + g.Key)) _ = LookUpSteamIdAsync(automatic: true); // cover only; Proton settings stay .exe-only
        }
        coverReset.IsVisible = p.CoverPath is { Length: > 0 }; coverStatus.Text = "";
        hero.Background = art is null ? UiKit.HeroBrush() : new ImageBrush { Source = art, Stretch = Stretch.UniformToFill };
        heroScrim.IsVisible = heroArt; title.Foreground = heroArt ? Brushes.White : Palette.Fg; sub.Foreground = also.Foreground = heroArt ? GameArt.OnImageMuted : Palette.Muted;
        also.IsVisible = MergedOf(g).Count > 0; also.Text = also.IsVisible ? L.T("Também aparece como: ") + GameDedup.AlsoAppearsAs(MergedOf(g)) : "";
        chkPlan.IsChecked = p.HighPerfPlan; chkGpu.IsChecked = p.GpuHigh && multiGpu; chkGameMode.IsChecked = p.GameMode; chkMango.IsChecked = p.MangoHud; chkMeasure.IsChecked = p.MeasureFps; UpdateFlatpakMango(g); UpdateMeasure(); chkGuard.IsChecked = p.FullscreenGuard; chkGuard.IsVisible = multiGpu && gpuVendor == "NVIDIA";
        modeChips!.Select(p.WinMode); resChips!.Select(p.Res); hzChips!.Select(p.Hz); styleChips!.Select(p.Style); gfxPresetChips!.Select(p.GfxPreset ?? ""); gfxUpChips!.Select(p.GfxUpscaling ?? ""); extra.Text = p.Extra; exePath.Text = p.ExePath ?? g.ExePath ?? "";
        protonBox.IsVisible = g.IsWindowsManual; protonId.Text = p.ProtonGameId ?? ""; protonIdStatus.Text = ""; runnerStatus.Text = "";
        if (g.IsWindowsManual) LoadRunner(p);
        removeGame.IsVisible = renameGame.IsVisible = g.Store == "Manual"; gameShortcut.IsVisible = true;
        hideGame.IsVisible = g.Store != "Manual" || IsHidden(g); hideGame.Content = IsHidden(g) ? L.T("Mostrar na biblioteca") : L.T("Ocultar da biblioteca");
        chkWayland.IsChecked = p.ProtonWayland; chkLowLat.IsChecked = p.LowLatency; extrasBox.IsVisible = g.IsSteam || g.IsWindowsManual;
        protonName = g.IsSteam ? steam.CompatToolName(g.Id) : !g.IsWindowsManual ? null
            : p.Runner is { Length: > 0 } chosen && Path.IsPathFullyQualified(chosen) ? Path.GetFileName(chosen) : (UmuProton ?? (() => LinuxGameExtras.UmuProtonName()))();
        RebuildHelpers(); RebuildAchFix();
        steamBox.IsVisible = g.IsSteam; steamNow.Text = g.IsSteam ? L.T("Opções atuais na Steam: ") + (steam.GetLaunchOptions(g.Id) is { Length: > 0 } cur ? cur : L.T("(nenhuma)")) : "";
        gfxNote.Text = g.IsSteam ? L.T("Os argumentos e variáveis só passam a valer na Steam depois de Aplicar nas opções da Steam (a Steam é fechada nessa hora). Depois, o Jogar abre pela Steam normalmente.")
            : g.LaunchUri is not null ? L.T("Esta loja abre o jogo por um atalho que não aceita argumentos: use as opções de inicialização da própria loja. Desempenho e programas auxiliares funcionam normalmente.")
            : L.T("Os argumentos são enviados direto ao executável quando você clica em Jogar.");
        status.Text = following.ContainsKey(g.Key) ? L.T($"Abrindo {g.Name}…") : running.Contains(g.Key) ? L.T($"{g.Name} em execução.") : ""; loading = false; UpdatePreview(); ShowExe(g, p);
        Filter();
        UpdateButtons(); _ = DetectSelectedAsync(g);
        RefreshMeasured(); _ = DetectQualityAsync(g, p);
    }

    private void RefreshMeasured()
    {
        fpsGroups = current is { } g ? FpsHistory.Build(settings.FpsSessions, g.Name, sameGame: MergedOf(g).Select(m => m.Name).ToList()) : [];
        bool any = fpsGroups.Count > 0;
        fpsEmpty.IsVisible = !any; fpsBig.IsVisible = fpsRange.IsVisible = fpsInfo.IsVisible = any; fpsGroupBox.IsVisible = fpsGroups.Count > 1;
        if (!any)
        {
            fpsEmpty.Text = (current is { } cg && (cg.IsSteam || cg.LaunchUri is not null)
                ? L.T("Na Steam e nas outras lojas o FPS só é medido com o MangoHud nas opções de inicialização (na Steam: marque MangoHud e use Aplicar nas opções da Steam). ")
                : L.T("A média aparece depois de você jogar abrindo o jogo pelo Ilmenyx, com Medir FPS ou MangoHud ligado, por pelo menos 1 minuto. ")) + L.T("Só conta o jogo em si: os 20 s iniciais, os 3 s finais e as travadas de carregamento ficam de fora, para o menu e o carregamento não inflarem o número. Nenhum valor é inventado.");
            return;
        }
        loadingGroups = true; fpsGroupBox.ItemsSource = fpsGroups.Select(g => (g.Resolution ?? L.T("resolução não informada")) + " · " + (g.Quality is { } q ? QualityLabel(q) : L.T("qualidade não informada"))).ToList(); fpsGroupBox.SelectedIndex = 0; loadingGroups = false;
        ShowGroup(0);
    }

    private void ShowGroup(int i)
    {
        if (i < 0 || i >= fpsGroups.Count) return;
        var g = fpsGroups[i];
        fpsBig.Text = L.T($"{g.TypicalFps:0} FPS");
        fpsRange.Text = L.T($"Faixa típica {g.P10Fps:0} a {g.P90Fps:0} FPS · 1% low {g.Low1Fps:0}");
        string time = g.Hours < 1 ? L.T($"{g.Hours * 60:0} min") : L.T($"{g.Hours:0.#} h");
        fpsInfo.Text = (g.Sessions == 1 ? L.T($"1 sessão · {time} neste PC") : L.T($"{g.Sessions} sessões · {time} neste PC")) + L.T($" · última em {g.Last.ToLocalTime():dd/MM/yyyy HH:mm}")
            + (g.CappedAt is { } cap ? L.T($". Limitado a {cap:0} FPS (vsync ou limite do jogo): o PC pode render mais.") : "");
    }

    private async Task DetectQualityAsync(LibGame g, GameProfile p)
    {
        detectedQuality = null; qualityDetected.Text = L.T("Procurando a configuração do jogo…"); UpdateQualityActive();
        int gen = ++detectGen;
        {
            var det = await (DetectQuality?.Invoke(g, p) ?? DetectRealAsync(g, p));
            if (gen != detectGen || current?.Key != g.Key) return;
            detectedQuality = det;
            qualityDetected.Text = det is null
                ? L.T("Não detectado: o Ilmenyx só sabe ler jogos Unreal e Unity, e o jogo precisa já ter sido aberto uma vez. Escolha a qualidade abaixo, se quiser.")
                : L.T($"Detectado em {Path.GetFileName(det.File)}: ") + string.Join(", ", new[] { det.Level.Length > 0 ? QualityLabel(det.Level) + (det.Preset is null && det.Engine == "Unity" ? L.T(" (posição na lista do jogo, não um preset)") : "") : null,
                    det.Resolution?.Replace('x', '×'), det.FpsLimit is { } l ? L.T($"limite de {l} FPS") : null }.Where(x => x is not null));
            UpdateQualityActive();
        }
    }

    private static async Task<DetectedQuality?> DetectRealAsync(LibGame g, GameProfile p)
    {
        try
        {
            return await Task.Run(() =>
            {
                string? exe = p.ExePath ?? g.ExePath ?? LinuxGameLibrary.FindMainExe(g.InstallDir, g.Name);
                return GameQualityDetector.Detect(g.Name, exe, GameQualityDetector.Prefixes(g, p, exe));
            }).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException) { return null; }
    }

    private static string QualityLabel(string q) => q switch
    {
        "Baixo" => L.T("Baixo"), "Médio" => L.T("Médio"), "Alto" => L.T("Alto"), "Ultra" => L.T("Ultra"), "Cinemático" => L.T("Cinemático"), "Personalizado" => L.T("Personalizado"),
        _ when q.StartsWith("Unity nível ", StringComparison.Ordinal) => L.T($"Unity nível {q[12..]}"),
        _ when q.StartsWith("nível ", StringComparison.Ordinal) => L.T($"nível {q[6..]}"),
        _ => q,
    };

    /// <summary>Says which quality goes into the next session: the chip the user chose wins over the detected one.</summary>
    private void UpdateQualityActive()
    {
        if (profile is not { } p) return;
        qualityActive.Text = p.GfxPreset is { Length: > 0 } m ? L.T($"Valendo: sua escolha ({QualityLabel(m)}). Ela prevalece sobre a detecção; use Automático para voltar a usar a detectada.")
            : detectedQuality?.Preset is { } d ? L.T($"Valendo: a detectada ({QualityLabel(d)}).")
            : detectedQuality is { Level.Length: > 0 } u ? L.T($"Valendo: {u.Engine} {QualityLabel(u.Level)} (só agrupa o histórico; escolha uma qualidade abaixo para mapear).")
            : L.T("Valendo: nenhuma qualidade informada; as sessões ficam num grupo sem qualidade.");
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
        profile.HighPerfPlan = chkPlan.IsChecked == true; profile.GpuHigh = chkGpu.IsChecked == true; profile.GameMode = chkGameMode.IsChecked == true; profile.MangoHud = chkMango.IsChecked == true; profile.MeasureFps = chkMeasure.IsChecked == true; profile.FullscreenGuard = chkGuard.IsChecked == true;
        profile.ProtonWayland = chkWayland.IsChecked == true; profile.LowLatency = chkLowLat.IsChecked == true;
        profile.Extra = (extra.Text ?? "").Trim();
        SaveProfile(current, profile); UpdatePreview(); UpdateMeasure();
    }

    /// <summary>"Medir FPS" only applies where the launcher itself starts the game (not Steam or store shortcuts) and needs MangoHud (host, packaged, or the Flatpak extension).</summary>
    private void UpdateMeasure()
    {
        bool applies = current is { } g && !g.IsSteam && g.LaunchUri is null;
        chkMeasure.IsVisible = measureNote.IsVisible = applies;
        if (!applies) return;
        bool available = chkMango.IsEnabled, overlay = chkMango.IsChecked == true;
        chkMeasure.IsEnabled = available && !overlay;
        measureNote.Text = !available ? L.T("Indisponível: o MangoHud não está instalado (em jogo Flatpak, falta a extensão acima). O jogo abre normalmente, sem medir.")
            : overlay ? L.T("O MangoHud marcado acima já mede o FPS.")
            : chkMeasure.IsChecked == true ? L.T("O MangoHud entra escondido, só para gravar o FPS. Desmarque em jogos com anti-cheat ou incompatíveis com MangoHud.")
            : L.T("Desligado: este jogo não terá desempenho medido, a menos que você marque o MangoHud.");
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

    /// <summary>Fills the Proton/Wine list (automatic, latest GE, what is on this PC, the profile's own folder, pick a folder) and the prefix line.</summary>
    private void LoadRunner(GameProfile p)
    {
        bool was = loading; loading = true;
        var found = (Runners ?? (() => GameRunners.Find()))();
        runnerChoices = [(L.T("Automático (UMU-Proton, recomendado)"), null), (L.T("GE-Proton mais recente (baixa na primeira vez)"), GameRunners.LatestGe),
            .. found.Select(r => (r.Name + (r.Wine ? " (Wine)" : ""), (string?)r.Path))];
        if (p.Runner is { Length: > 0 } own && own != GameRunners.LatestGe && found.All(r => r.Path != own))
            runnerChoices.Add((Path.GetFileName(own) + (GameRunners.IsRunner(own) ? L.T(" (pasta escolhida)") : L.T(" (não encontrado: o jogo abre no automático)")), own));
        runnerChoices.Add((L.T("Escolher pasta…"), PickRunner));
        runnerBox.ItemsSource = runnerChoices.Select(c => c.Label).ToList();
        runnerBox.SelectedIndex = Math.Max(0, runnerChoices.FindIndex(c => c.Value == (p.Runner is { Length: > 0 } r ? r : null)));
        string? exe = p.ExePath ?? current?.ExePath;
        string prefix = p.WinePrefix is { Length: > 0 } w ? w : exe is null ? "" : LinuxGameLauncher.ProtonEnvironment(exe, p.ProtonGameId, LinuxGameLauncher.DefaultPrefixRoot())[0].Value;
        prefixText.Text = (p.WinePrefix is { Length: > 0 } ? "" : L.T("Padrão: ")) + prefix; ToolTip.SetTip(prefixText, prefix);
        loading = was;
    }

    /// <summary>Test seam: the "Baixar versão…" window (default: the real one, which talks to GitHub).</summary>
    public Func<RunnerDownloadDialog>? RunnerDialog { get; set; }

    /// <summary>Test seam: the Proton/Wine builds offered (default: what is found on this PC).</summary>
    public Func<IReadOnlyList<GameRunner>>? Runners { get; set; }

    private async Task<string?> PickFolderAsync(string title)
    {
        if (owner() is not { } window) return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
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
        return files.Count > 0 && files[0].TryGetLocalPath() is { } local ? DocumentPortal.RealPath(local) : null;
    }

    /// <summary>Artwork of a library game: Steam's local cache; for a hand-added Windows game with a Steam ID, that ID's local Steam
    /// cache or the image already downloaded for it (Steam CDN, kept in the Compatibility cache).</summary>
    private Bitmap? ArtFor(LibGame g, bool hero)
    {
        if (CoverOf(g) is { } cover && GameArt.FromFile(cover, hero) is { } custom) return custom;
        if (g.IsSteam) return GameArt.Steam(steamRoot, g.Id, hero);
        if (g.Store == "Linux")
        {
            if (nativeArt.Cached(g.Id) is { } shot) return GameArt.FromFile(shot, hero);
            FetchNativeArtLater(g); return null;
        }
        if (g.Store != "Manual" || !settings.GameProfiles.TryGetValue(SettingsKey(g), out var gp) || (gp.ProtonGameId ?? gp.ArtSteamId) is not { Length: > 0 } id) return null;
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
        mangoFlatpakBox.IsVisible = false; mangoFlatpakBranch = null; mangoFlatpakInstall.IsEnabled = !installingMango; mangoFlatpakProgress.IsVisible = installingMango;
        string? app = g.ExePath is { } exe && Path.GetFileName(exe) == "flatpak" ? GameProcesses.FlatpakApp(g) : null;
        if (app is null) { chkMango.IsEnabled = hostMango; ToolTip.SetTip(chkMango, hostMango ? null : L.T("mangohud não está instalado.")); return; }
        string? branch = flatpaks.Metadata(app).RuntimeBranch;
        bool ok = branch is not null && flatpaks.HasMangoHud(branch);
        chkMango.IsEnabled = ok;
        ToolTip.SetTip(chkMango, ok ? L.T($"Jogo Flatpak: usa a extensão MangoHud para Flatpak (runtime {branch}).") : L.T("Jogo Flatpak: o MangoHud do sistema não entra no Flatpak; precisa da extensão MangoHud para Flatpak."));
        if (ok) return;
        mangoFlatpakBox.IsVisible = true; mangoFlatpakStatus.Foreground = Palette.Warn;
        mangoFlatpakBranch = branch;
        mangoFlatpakInstall.IsVisible = branch is not null;
        mangoFlatpakStatus.Text = branch is null ? L.T("Não consegui ler o runtime deste Flatpak; o MangoHud fica desligado para ele.") : L.T($"MangoHud para Flatpak não instalado (runtime {branch}).");
        ToolTip.SetTip(mangoFlatpakStatus, L.T("Jogos Flatpak rodam isolados do sistema. O overlay e o FPS medido precisam da extensão oficial ") + FlatpakLaunch.MangoHudExtension + L.T(" na versão do runtime do jogo."));
    }

    private async Task InstallFlatpakMangoAsync()
    {
        if (mangoFlatpakBranch is not { } branch || current is not { } game) return;
        if (owner() is { } window && !await ConfirmDialog.AskAsync(window, L.T("MangoHud para Flatpak"), L.T($"Instalar a extensão {FlatpakLaunch.MangoHudExtension} ({branch}) do Flathub?\n\nSó para o seu usuário, sem senha. Precisa de internet."), L.T("Instalar"), L.T("Cancelar"))) return;
        if (installingMango) return;
        installingMango = true; mangoFlatpakInstall.IsEnabled = false; mangoFlatpakProgress.IsVisible = true;
        mangoFlatpakStatus.Foreground = Palette.Muted; mangoFlatpakStatus.Text = L.T("Baixando a extensão do Flathub…");
        (int Code, string Output) r;
        try { r = await BoundedProcess.RunAsync("flatpak", FlatpakApps.InstallMangoHud(branch), TimeSpan.FromMinutes(10), 64 * 1024, lifetime); }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { r = (-1, e.Message); }
        catch (OperationCanceledException) { installingMango = false; return; }
        installingMango = false; mangoFlatpakInstall.IsEnabled = true; mangoFlatpakProgress.IsVisible = false;
        if (current != game) return; // another game is open now: its card must not get this game's result
        if (r.Code == 0 && flatpaks.HasMangoHud(branch))
        {
            UpdateFlatpakMango(game); UpdateMeasure();
            status.Text = L.T("MangoHud para Flatpak instalado. Marque MangoHud para usar o overlay neste jogo.");
            return;
        }
        string last = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        mangoFlatpakStatus.Foreground = Palette.Warn;
        mangoFlatpakStatus.Text = L.T("Não consegui instalar") + (last.Length > 0 ? ": " + (last.Length > 160 ? last[..160] + "…" : last) : ".");
    }

    /// <summary>Fills the Steam ID of the selected Windows game from its name (automatic: only when empty, quietly when nothing is found).</summary>
    private async Task LookUpSteamIdAsync(bool automatic, bool renamed = false)
    {
        if (current is not { Store: "Manual" } game || profile is not { } p) return;
        if (!game.IsWindowsManual) { if (automatic) await LookUpArtIdAsync(game, p, renamed); return; }
        if (automatic && !renamed && p.ProtonGameId is { Length: > 0 }) return;
        if (renamed) p.RejectedSteamIds.Clear();
        protonIdStatus.Foreground = Palette.Faint; protonIdStatus.Text = L.T("Buscando o ID Steam…");
        var find = FindSteamId ?? ((n, ct) => SteamIds.FindAsync(n, ct));
        var found = await find(game.Name, lifetime);
        if (found is null && !renamed && GameNameFromFolder(game.Id) is { } folderName && folderName != game.Name) found = await find(folderName, lifetime); // "MK10" → "Mortal Kombat XL"
        if (current != game) return;
        if (found is { } f)
        {
            loading = true; protonId.Text = f.Id; loading = false;
            p.ProtonGameId = f.Id; SaveProfile(game, p); UpdateTags();
            protonIdStatus.Text = L.T($"ID encontrado na loja Steam: {f.Name}.");
            FetchArtLater(game, f.Id);
        }
        else
        {
            protonIdStatus.Text = automatic ? "" : p.ProtonGameId is { Length: > 0 } kept ? L.T($"Nenhum jogo com esse nome na loja Steam. O ID {kept} continua salvo.") : L.T("Nenhum jogo com esse nome na loja Steam. Digite o ID no campo.");
            await SuggestAsync(game, p, (id, name) =>
            {
                loading = true; protonId.Text = id; loading = false;
                p.ProtonGameId = id; SaveProfile(game, p); UpdateTags();
                protonIdStatus.Foreground = Palette.Faint; protonIdStatus.Text = L.T($"ID encontrado na loja Steam: {name}.");
                FetchArtLater(game, id);
            });
        }
    }

    /// <summary>"Você quis dizer X?" after no exact match: shows the closest names one at a time; Sim applies it, Não remembers the refusal.</summary>
    private async Task SuggestAsync(LibGame game, GameProfile p, Action<string, string> apply)
    {
        var rejected = p.RejectedSteamIds.ToHashSet();
        var list = SuggestSteamId is { } custom ? await custom(game.Name, rejected, lifetime) : await SteamIds.SuggestAsync(game.Name, lifetime, rejected);
        if (current != game || list.Count == 0) return;
        suggestions = [.. list]; applySuggestion = apply; ShowSuggestion();
    }

    private void ShowSuggestion()
    {
        string q = L.T($"Você quis dizer {suggestions[0].Name}?");
        suggestText.Text = q; AutomationProperties.SetName(suggestBox, q); suggestBox.IsVisible = true;
    }

    private void HideSuggestion() { suggestBox.IsVisible = false; suggestions = []; applySuggestion = null; }

    private void RejectSuggestion()
    {
        if (suggestions.Count == 0 || current is not { } game || profile is not { } p) { HideSuggestion(); return; }
        p.RejectedSteamIds.Add(suggestions[0].Id); SaveProfile(game, p);
        suggestions.RemoveAt(0);
        if (suggestions.Count == 0) HideSuggestion(); else ShowSuggestion();
    }

    /// <summary>Non-.exe manual game: finds the Steam ID by name only for the cover (kept in ArtSteamId, never in the Proton settings).</summary>
    private async Task LookUpArtIdAsync(LibGame game, GameProfile p, bool renamed)
    {
        if (!renamed && p.ArtSteamId is { Length: > 0 }) return;
        if (renamed) p.RejectedSteamIds.Clear();
        var find = FindSteamId ?? ((n, ct) => SteamIds.FindAsync(n, ct));
        var found = await find(game.Name, lifetime);
        if (current != game) return;
        if (found is not { } f) { await SuggestAsync(game, p, (id, _) => { p.ArtSteamId = id; SaveProfile(game, p); FetchArtLater(game, id); }); return; }
        p.ArtSteamId = f.Id; SaveProfile(game, p); FetchArtLater(game, f.Id);
    }

    private string? OwnCover(LibGame g) => settings.GameProfiles.TryGetValue(SettingsKey(g), out var gp) && gp.CoverPath is { Length: > 0 } c ? c : null;

    /// <summary>The game's own cover, else that of an entry joined into it.</summary>
    private string? CoverOf(LibGame g) => OwnCover(g) ?? MergedOf(g).Select(OwnCover).FirstOrDefault(c => c is not null);

    /// <summary>The menu entry's icon: the game's own, or that of a Linux entry that starts the same program as a manual game.</summary>
    private string? IconFor(LibGame g) => g.Icon ?? MergedOf(g).FirstOrDefault(n => n.Icon is not null)?.Icon;

    /// <summary>Hides (or shows again) the selected game that is not manual. Profile and settings stay; hiding selects the next visible game.</summary>
    private void ToggleHideCurrent()
    {
        if (current is not { } g || g.Store == "Manual" && !IsHidden(g)) return;
        string key = g.Key; bool hide = !IsHidden(g); var keys = MergedOf(g).Select(m => m.Key).Append(key).ToList();
        var shown = Shown(); int at = shown.FindIndex(x => x.Key == key);
        void set(IlmenyxSettings s) { s.HiddenGames.RemoveAll(keys.Contains); if (hide) s.HiddenGames.Add(key); }
        if (Save(set) is { } fresh) settings.HiddenGames = fresh.HiddenGames; else set(settings);
        if (hide && showHidden.IsChecked != true)
        {
            var after = Shown(); Select(after.Count == 0 ? null : after[Math.Min(Math.Max(at, 0), after.Count - 1)]);
        }
        else Select(g);
        UpdateCount();
    }

    private async Task ChooseCoverFileAsync()
    {
        if (current is not { } g || owner() is not { } window) return;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Escolha a imagem da capa"), AllowMultiple = false,
            FileTypeFilter = [new(L.T("Imagens")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.PNG", "*.JPG", "*.JPEG", "*.WEBP"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } local) return;
        string real = DocumentPortal.RealPath(local), source = File.Exists(real) ? real : local; // the portal alias still reads inside Flatpak
        await ApplyCoverAsync(g, () => Task.Run(() => GameCovers.CopyLocal(source, GameCovers.Folder(), g.Key)));
    }

    private async Task ChooseCoverUrlAsync()
    {
        if (current is not { } g || owner() is not { } window) return;
        string? text = await TextInputDialog.AskAsync(window, L.T("Capa por link"), L.T("Link https:// da imagem (PNG, JPEG ou WebP, até 8 MB). O Ilmenyx baixa só quando você confirma."), "", 2048, L.T("Usar"), "LCoverUrl");
        if (string.IsNullOrWhiteSpace(text)) return;
        await ApplyCoverAsync(g, () => { var url = GameCovers.ValidateUrl(text); return GameCovers.DownloadAsync(url, GameCovers.Folder(), g.Key, lifetime); });
    }

    private async Task ApplyCoverAsync(LibGame g, Func<Task<string>> make)
    {
        string path;
        coverStatus.Foreground = Palette.Muted; coverStatus.Text = L.T("Preparando a capa…");
        coverFile.IsEnabled = coverUrl.IsEnabled = coverReset.IsEnabled = false;
        try { path = await make(); }
        catch (CoverException e) { if (current?.Key == g.Key) { coverStatus.Foreground = Palette.Warn; coverStatus.Text = L.T("Não consegui usar esta imagem: ") + e.Message; } return; }
        finally { coverFile.IsEnabled = coverUrl.IsEnabled = coverReset.IsEnabled = true; }
        string key = SettingsKey(g);
        void set(IlmenyxSettings s) { if (!s.GameProfiles.TryGetValue(key, out var gp)) s.GameProfiles[key] = gp = new(); gp.CoverPath = path; }
        if (Save(set) is { } fresh) settings.GameProfiles = fresh.GameProfiles; else set(settings);
        RefreshCover(g, path);
    }

    private void ResetCover()
    {
        if (current is not { } g) return;
        string key = SettingsKey(g); string? old = OwnCover(g);
        void clear(IlmenyxSettings s) { if (s.GameProfiles.TryGetValue(key, out var gp)) gp.CoverPath = null; }
        if (Save(clear) is { } fresh) settings.GameProfiles = fresh.GameProfiles; else clear(settings);
        try { if (old is not null && Path.GetDirectoryName(old) == GameCovers.Folder()) File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        RefreshCover(g, old);
        coverStatus.Foreground = Palette.Muted; coverStatus.Text = L.T("Capa removida.");
    }

    /// <summary>Repaints tile and hero after the cover changed (the decoded copies of the old file leave the session cache).</summary>
    private void RefreshCover(LibGame g, string? changed)
    {
        GameArt.Forget(changed);
        if (current?.Key == g.Key) { Select(g); coverStatus.Foreground = Palette.Muted; coverStatus.Text = CoverOf(g) is null ? "" : L.T("Capa definida."); } else Filter();
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
        await LookUpSteamIdAsync(automatic: true, renamed: true); // a new name means a new search for the ID and the cover; no match keeps the saved ID
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

    private void RebuildAchFix()
    {
        achFixBox.Children.Clear();
        achFixBox.IsVisible = profile is { AchievementFixFiles.Count: > 0 };
        if (profile is not { AchievementFixFiles.Count: > 0 }) return;
        var undo = new Button { Name = "BtnAchUndo", Content = L.T("Desfazer"), FontSize = 11, Padding = new Thickness(10, 4) };
        var grid = new Grid { ColumnDefinitions = new("*,Auto") };
        var text = new TextBlock { Text = L.T("Lista de conquistas criada pelo Ilmenyx na pasta do jogo"), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(undo, 1); grid.Children.Add(text); grid.Children.Add(undo);
        undo.Click += (_, _) =>
        {
            if (profile is null) return;
            AchievementFiles.UndoEmulator(profile.AchievementFixFiles); profile.AchievementFixFiles = []; SaveProfile(current, profile); RebuildAchFix();
        };
        achFixBox.Children.Add(grid);
        UpdateLive();
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

    /// <summary>Opens a game from a shortcut ("--play KEY"): selects it and presses Jogar.</summary>
    public async Task PlayByKeyAsync(string key)
    {
        if (games.Count == 0) await ScanAsync(); // shortcut: a scan already started by Loaded may run twice, only costs time
        if (games.FirstOrDefault(g => g.Key == key || MergedOf(g).Any(m => m.Key == key)) is not { } game) { count.Text = L.T("O atalho aponta para um jogo que não está mais na biblioteca. Crie o atalho de novo."); return; }
        Select(game);
        if (!following.ContainsKey(game.Key)) await PlayAsync(); // already running: PlayAsync says so
    }

    private static MenuFlyout ShortcutMenu(Func<bool, string?> create)
    {
        var menu = new MenuFlyout();
        foreach (var (label, desktop) in new[] { (L.T("No menu de aplicativos"), false), (L.T("Na área de trabalho"), true) })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => create(desktop);
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>Writes a shortcut that opens <paramref name="game"/> (null = the games-only window) and says where. Returns the file.</summary>
    public string? CreateShortcut(bool desktop, LibGame? game)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? folder = ShortcutFolder is { } pick ? pick(desktop) : desktop ? AppShortcuts.DesktopFolder(home) : AppShortcuts.MenuFolder(home);
        var target = game is null ? count : status;
        if (folder is null) { target.Text = L.T("Não achei a pasta da área de trabalho. Use o menu de aplicativos."); return null; }
        var self = AppShortcuts.SelfCommand(DesktopBackend.AppId);
        string text = game is null
            ? AppShortcuts.Entry(L.T("Jogos — Ilmenyx Controller"), [.. self, AppShortcuts.GamesArg], DesktopBackend.AppId, L.T("Biblioteca de jogos do Ilmenyx Controller"), DesktopBackend.AppId)
            : AppShortcuts.Entry(game.Name, [.. self, AppShortcuts.PlayArg, game.Key], ShortcutIcon(game), L.T($"Abre {game.Name} pelo Ilmenyx Controller, com o perfil do jogo"), DesktopBackend.AppId);
        try
        {
            string path = AppShortcuts.Write(folder, game is null ? "ilmenyx-jogos.desktop" : AppShortcuts.GameFileName(game.Key), text);
            target.Text = desktop ? L.T("Atalho criado na área de trabalho. Se o sistema perguntar, permita abrir.") : L.T("Atalho criado no menu de aplicativos.");
            ToolTip.SetTip(target, path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { target.Text = L.T("Não consegui criar o atalho: ") + e.Message; return null; }
    }

    /// <summary>The game's own menu icon, else its cover (Steam cache or the art Ilmenyx downloaded), else the Ilmenyx icon.</summary>
    private string ShortcutIcon(LibGame g)
    {
        if (g.Icon is { Length: > 0 } icon) return icon;
        if (g.IsSteam && steamRoot is not null && g.Id.All(char.IsAsciiDigit))
            foreach (var n in new[] { g.Id + "_icon.jpg", g.Id + "_library_600x900.jpg", Path.Combine(g.Id, "library_600x900.jpg"), g.Id + "_header.jpg" })
                if (Path.Combine(steamRoot, "appcache", "librarycache", n) is var f && File.Exists(f)) return f;
        if (settings.GameProfiles.GetValueOrDefault(SettingsKey(g))?.ProtonGameId is { Length: > 0 } id && ArtCache.CachedArt(id) is { } art) return art;
        return DesktopBackend.AppId;
    }

    private string? ExeOf(LibGame g) => settings.GameProfiles.GetValueOrDefault(g.Key)?.ExePath ?? g.ExePath;

    private async Task<bool> RunningNowAsync(LibGame g, CancellationToken ct) =>
        IsRunning is { } f ? await f(g, ExeOf(g), ct) : (await GameProcesses.FindAsync("/proc", g, ExeOf(g), ct)).Count > 0;

    private void SetRunning(string key, bool on)
    {
        if (!(on ? running.Add(key) : running.Remove(key))) return;
        RunningGames.Set(key, on);
        UpdateButtons();
    }

    /// <summary>Jogar / Abrindo… / Em execução and the stop button for the selected game, from the per-game state. Keeps the keyboard focus on a live button.</summary>
    private void UpdateButtons()
    {
        string? k = current?.Key;
        bool opening = k is not null && following.ContainsKey(k), on = k is not null && running.Contains(k);
        bool playFocus = play.IsFocused, stopFocus = stopWatch.IsFocused;
        play.Content = on ? L.T("Em execução") : opening ? L.T("Abrindo…") : L.T("Jogar");
        play.IsEnabled = !opening && !on;
        stopWatch.IsVisible = k is not null && (opening || watchers.ContainsKey(k));
        closeGame.IsVisible = on && !closing.Contains(k!);
        playProgress.IsVisible = opening && !on && !opened.Contains(k!);
        if (playFocus && !play.IsEnabled) (stopWatch.IsVisible ? stopWatch : backToLibrary).Focus();
        else if (stopFocus && !stopWatch.IsVisible) (play.IsEnabled ? play : backToLibrary).Focus();
        UpdateLive();
    }

    /// <summary>"Fechar jogo": confirmation, SIGTERM to the game's own processes, up to 10 s of waiting, then a second confirmation for SIGKILL.</summary>
    private async Task CloseGameAsync()
    {
        if (current is not { } g || owner() is not { } window || !closing.Add(g.Key)) return;
        string key = g.Key; bool hadFocus = closeGame.IsFocused;
        void Say(string text) { if (current?.Key == key) status.Text = text; }
        try
        {
            closeGame.IsVisible = false;
            if (!await ConfirmDialog.AskAsync(window, L.T("Fechar jogo"), L.T($"Fechar {g.Name}? O progresso que não foi salvo no jogo pode ser perdido."), L.T("Fechar jogo"), L.T("Cancelar"))) return;
            Say(L.T($"Fechando {g.Name}…"));
            Task<IReadOnlyList<int>> Find(CancellationToken ct) => GameProcesses.FindAsync("/proc", g, ExeOf(g), ct);
            bool gone = await GameCloser.CloseAsync(Find, GameCloser.SignalAsync, false, TimeSpan.FromSeconds(10), lifetime);
            if (!gone)
            {
                if (!await ConfirmDialog.AskAsync(window, L.T("Forçar fechamento"), L.T($"{g.Name} não fechou em 10 s. Forçar o fechamento encerra o jogo na hora e o progresso não salvo é perdido."), L.T("Forçar fechamento"), L.T("Cancelar")))
                { Say(L.T($"{g.Name} continua aberto.")); return; }
                gone = await GameCloser.CloseAsync(Find, GameCloser.SignalAsync, true, TimeSpan.FromSeconds(3), lifetime);
            }
            Say(gone ? L.T($"{g.Name} foi fechado.") : L.T($"{g.Name} continua aberto: o sistema não encerrou o processo."));
            ApplyRunning(g, !gone);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or TimeoutException or FileNotFoundException) { Say(L.T("Não consegui fechar o jogo: ") + e.Message); }
        finally
        {
            closing.Remove(key); UpdateButtons();
            if (hadFocus && current?.Key == key) (closeGame.IsVisible ? closeGame : play.IsEnabled ? play : backToLibrary).Focus(); // the button was hidden during the question
        }
    }

    /// <summary>Name of a game that is open or being followed (the window must not close silently over it), or null.</summary>
    /// <summary>True when a game is being followed (not only open): closing the window then stops that following.</summary>
    public bool ActiveGameFollowed => following.Count > 0;

    public string? ActiveGameName => following.Keys.Concat(running).Select(k => games.FirstOrDefault(x => x.Key == k)?.Name).FirstOrDefault(n => n is not null);

    /// <summary>Stops following every game and waits (at most <paramref name="limit"/>) for the undo of temporary changes (power profile) to finish.</summary>
    public async Task ReleaseAsync(TimeSpan limit)
    {
        foreach (var f in following.Values.ToList()) f.Cancel();
        var end = DateTime.UtcNow + limit;
        while (following.Count > 0 && DateTime.UtcNow < end) await Task.Delay(50);
    }

    /// <summary>
    /// Window close guard: with a game open or followed, asks first; on "Fechar o Ilmenyx" the following stops, the power profile is restored
    /// (3 s at most) and the window closes. Closing the window quits the app (the tray only takes over on minimise), so there is no hide case.
    /// </summary>
    public static void GuardClose(Window w, Func<LauncherPage?> page)
    {
        bool allowed = false, asking = false;
        w.Closing += (_, e) =>
        {
            if (allowed || page() is not { ActiveGameName: { } name } p) return;
            e.Cancel = true;
            if (asking) return;
            asking = true;
            _ = Ask();
            async Task Ask()
            {
                try
                {
                    if (!w.IsVisible) w.Show();
                    string warning = p.ActiveGameFollowed ? L.T($"O jogo {name} continua aberto. O Ilmenyx vai parar o acompanhamento e restaurar o perfil de energia.") : L.T($"O jogo {name} continua aberto. Fechar o Ilmenyx não fecha o jogo.");
                    if (!await ConfirmDialog.AskAsync(w, L.T("Fechar o Ilmenyx"), warning, L.T("Fechar o Ilmenyx"), L.T("Cancelar"))) return;
                    await p.ReleaseAsync(TimeSpan.FromSeconds(3));
                    allowed = true; w.Close();
                }
                finally { asking = false; }
            }
        };
    }

    /// <summary>"Parar acompanhamento": explains what stops (only what is true for this launch) and asks first; the safe answer is the focused one.</summary>
    private async Task StopWatchingAsync()
    {
        if (current is not { } g || stopping || owner() is not { } window) return;
        string key = g.Key; bool followed = following.ContainsKey(key), hadFocus = stopWatch.IsFocused;
        if (!followed && !watchers.ContainsKey(key)) return;
        stopping = true;
        try
        {
            var launch = launchProfiles.GetValueOrDefault(key) ?? profile ?? new GameProfile();
            bool measured = launch.MangoHud || (launch.MeasureFps && !g.IsSteam && g.LaunchUri is null && chkMango.IsEnabled);
            string text = StopTracking.Explain(g.Name, launch, followed, g.IsWindowsManual, measured);
            if (!await ConfirmDialog.AskAsync(window, L.T("Parar o acompanhamento?"), text, L.T("Parar acompanhamento"), L.T("Continuar acompanhando"))) return;
            if (following.TryGetValue(key, out var f)) f.Cancel(); // RunAsync undoes the power profile and closes the helpers marked to close with the game
            EndWatch(key); UpdateButtons();
        }
        finally
        {
            stopping = false; UpdateButtons();
            if (hadFocus && !stopWatch.IsVisible && current?.Key == key) (closeGame.IsVisible ? closeGame : play.IsEnabled ? play : backToLibrary).Focus();
        }
    }

    private void EndWatch(string key)
    {
        if (!watchers.Remove(key, out var w)) return;
        w.Cancel(); w.Dispose();
    }

    private async Task DetectSelectedAsync(LibGame g)
    {
        try { bool on = await RunningNowAsync(g, lifetime); if (current?.Key == g.Key) ApplyRunning(g, on); }
        catch (OperationCanceledException) { }
        EnsureTimer();
    }

    /// <summary>Records what the process check found. The achievement watch ends when the game it follows is gone.</summary>
    private void ApplyRunning(LibGame g, bool on)
    {
        string key = g.Key; bool was = running.Contains(key);
        SetRunning(key, on);
        if (on || following.ContainsKey(key)) return;
        EndWatch(key);
        if (was && current?.Key == key) { status.Text = L.T($"{g.Name} encerrado."); UpdateButtons(); }
    }

    /// <summary>The check runs every 2 s while the page is on screen (for the selected game) and while a launch, a watch or a running game is tracked.</summary>
    private void EnsureTimer()
    {
        bool need = !lifetime.IsCancellationRequested && (attached || following.Count + watchers.Count + running.Count > 0);
        if (need && poller is null)
        {
            poller = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            poller.Tick += async (_, _) => await TickAsync();
            poller.Start();
        }
        else if (!need && poller is not null) { poller.Stop(); poller = null; }
    }

    private async Task TickAsync()
    {
        if (ticking) return;
        ticking = true;
        try
        {
            var keys = new HashSet<string>(following.Keys); keys.UnionWith(watchers.Keys); keys.UnionWith(running);
            if (attached && IsEffectivelyVisible && current is { } c) keys.Add(c.Key);
            foreach (var key in keys)
            {
                if (games.FirstOrDefault(x => x.Key == key) is { } g) ApplyRunning(g, await RunningNowAsync(g, lifetime));
                else { SetRunning(key, false); if (!following.ContainsKey(key)) EndWatch(key); } // the game left the library
            }
        }
        catch (OperationCanceledException) { }
        finally { ticking = false; EnsureTimer(); }
    }

    /// <summary>Updates only this game's profile in the current settings (the profile object kept by a long task goes stale when a cover, a rescan or a removal replaces the profiles).</summary>
    private void UpdateProfile(string key, Action<GameProfile> change)
    {
        void apply(IlmenyxSettings s) { if (!s.GameProfiles.TryGetValue(key, out var gp)) s.GameProfiles[key] = gp = new(); change(gp); }
        if (Save(apply) is { } fresh) settings.GameProfiles = fresh.GameProfiles; else apply(settings);
        if (current?.Key == key) profile = settings.GameProfiles.GetValueOrDefault(key);
    }

    private async Task PlayAsync()
    {
        if (current is not { } g || profile is not { } p) return;
        string key = g.Key;
        if (following.ContainsKey(key)) return;
        var launchProfile = p.Clone(); launchProfiles[key] = launchProfile; // the launch keeps this copy: ticking things while it runs only counts next time
        var follow = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        following[key] = follow; opened.Remove(key); launched.Remove(key);
        status.Text = L.T($"Abrindo {g.Name}…");
        UpdateButtons(); EnsureTimer();
        var ready = new TaskCompletionSource();
        try
        {
            if (running.Contains(key) || await RunningNowAsync(g, follow.Token)) { SetRunning(key, true); status.Text = L.T("Já está em execução."); return; }
            if (p.MangoHud && OperatingSystem.IsLinux()) MangoHudLogReader.EnsureConfig(settings); // overlay + FPS log ready without visiting the Overlay page
            EndWatch(key);
            var watch = watchers[key] = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            UpdateButtons();
            _ = WatchAchievementsAsync(g, p, ready, watch.Token);
            // The emulator reads its settings at startup: wait a little for the achievement list so the fix is in place for this launch.
            await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(5), follow.Token));
            launched.Add(key);
            var session = await launch(g, launchProfile, gpuVendor, t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (current?.Key == key) status.Text = t;
                    if (t.Contains(L.T(" em execução"), StringComparison.Ordinal)) { opened.Add(key); SetRunning(key, true); }
                    else if (t.StartsWith(L.T("Não detectei"), StringComparison.Ordinal)) opened.Add(key);
                    UpdateButtons();
                }),
                exe => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (settings.GameProfiles.GetValueOrDefault(key)?.ExePath == exe) return;
                    UpdateProfile(key, gp => gp.ExePath = exe);
                    if (current?.Key == key) exeText.Text = L.T("Executável: ") + exe;
                }), follow.Token);
            // Measured on this PC (MangoHud log of this launch): kept locally for the measured-FPS card, at most FpsSession.MaxEntries sessions.
            if (session is { IsValid: true }) { if (Save(s => s.AddFpsSession(session)) is { } fresh) settings.FpsSessions = fresh.FpsSessions; else settings.AddFpsSession(session); if (current?.Key == key) { RefreshMeasured(); if (profile is { } pp) _ = DetectQualityAsync(current, pp); } }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is not OperationCanceledException) { if (current?.Key == key) status.Text = L.T("Não consegui abrir o jogo: ") + e.Message; }
        finally
        {
            following.Remove(key); follow.Dispose(); launchProfiles.Remove(key);
            try { ApplyRunning(g, await RunningNowAsync(g, lifetime)); } catch (OperationCanceledException) { } // a running game keeps its watch and the Jogar stays off
            UpdateButtons(); EnsureTimer();
        }
    }

    /// <summary>
    /// Windows copy without Steam that carries a Steam emulator: while it runs, each new achievement in its save becomes a system
    /// notification with the achievement's name and icon (Steam games are left to Steam's own notice).
    /// </summary>
    private async Task WatchAchievementsAsync(LibGame g, GameProfile p, TaskCompletionSource ready, CancellationToken ct)
    {
        if (!g.IsWindowsManual || !OperatingSystem.IsLinux()) { ready.TrySetResult(); return; }
        string[] roots = steamRoot is null ? [] : [steamRoot];
        var now = settings.GameProfiles.GetValueOrDefault(g.Key) ?? p; // the profile as it is now, with the executable the launcher will run
        string? exeNow = now.ExePath ?? g.ExePath;
        try
        {
            if (await Task.Run(() => AchievementFiles.Find(g, now, roots), ct) is not { Origin: AchievementOrigin.Emulator } source) { ready.TrySetResult(); return; }
            IReadOnlyList<AchievementDef> defs = [];
            _ = Task.Run(async () =>
            {
                try
                {
                    defs = (await AchievementFiles.LoadAsync(source, roots, Achievements.SteamLanguage(L.Current), ArtCache.AchievementsAsync, ct)).Defs;
                    var made = defs.Count == 0 || exeNow is null ? [] : AchievementFiles.PrepareEmulator(AchievementFiles.GameFolder(exeNow), source.AppId, defs);
                    if (made.Count > 0)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            UpdateProfile(g.Key, gp => gp.AchievementFixFiles = [.. gp.AchievementFixFiles, .. made]);
                            if (current?.Key == g.Key) { RebuildAchFix(); status.Text = L.T("Conquistas preparadas: o Ilmenyx criou a lista de conquistas que o emulador do jogo precisa.") + (launched.Contains(g.Key) ? L.T(" Vale na próxima vez que o jogo abrir.") : ""); }
                        });
                }
                catch (OperationCanceledException) { }
                finally { ready.TrySetResult(); }
            }, ct);
            await AchievementFiles.WatchAsync(source, () => defs, async d =>
            {
                try
                {
                    string? picture = null;
                    try { if (d.Icon.Length > 0) picture = await ArtCache.AchievementIconAsync(source.AppId, d.Icon, ct); }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { }
                    var shown = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => AchievementToast.TryShow(d.Name, d.Description, g.Name, picture, () => _ = DesktopNotification.PlaySoundAsync()));
                    if (!shown) _ = DesktopNotification.PlaySoundAsync(); // one sound per notice: a toast plays its own when it appears
                    // With our own toast the system notice only goes to the history (low urgency); otherwise it is critical so it pops over the game.
                    await DesktopNotification.SendAsync(L.T("Conquista desbloqueada"), d.Name + (d.Description.Length > 0 ? " — " + d.Description : "") + "\n" + g.Name, CancellationToken.None, picture, urgent: !shown, quiet: shown);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (current?.Key == g.Key) status.Text = L.T("Conquista desbloqueada: ") + d.Name; });
                }
                catch (Exception e) when (e is not OperationCanceledException) { } // one failed notice must not end the watch
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally { ready.TrySetResult(); }
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

    /// <summary>Drops the decoded copies of a file (a cover that was replaced or removed).</summary>
    public static void Forget(string? path)
    {
        if (path is null) return;
        lock (Cache) foreach (var k in Cache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.Ordinal)).ToList()) Cache.Remove(k);
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
