// New Ilmenyx additions: see LICENSING.md. Layout follows the original RESET EXPLODE structure (grouped rounded sidebar,
// header with brand and current page, live footer, overview with chart/chips/components table); no original assets are used.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed partial class ShellWindow : Window
{
    private readonly UsageViewModel usage = new(new LinuxUsageProvider());
    private readonly DashboardViewModel hardware = new(LinuxPlatform.CreateHardwareProvider());
    private readonly SensorMonitorViewModel sensors = new(LinuxPlatform.CreateSensorProvider());
    private readonly CpuValidationViewModel cpu = new(LinuxPlatform.CreateHardwareProvider(), new CpuBenchmarkRunner());
    private readonly MemoryValidationViewModel memory = new(LinuxPlatform.CreateHardwareProvider(), new MemoryValidationRunner());
    private readonly GameSimValidationViewModel simulation = new(LinuxPlatform.CreateHardwareProvider(), new GameSimValidationRunner());
    private readonly SmartImportViewModel smartImport = new(SmartctlReportImport.ReadFileAsync);
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<string, Button> nav = new();
    private readonly Dictionary<string, (string Title, Func<Control> Build, Control? Page)> pages = new();
    private readonly Grid host = new();
    private readonly TextBlock titlePage = new() { Name = "TitlePage", Foreground = Palette.Faint, FontSize = 13, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private CpuValidationPanel? cpuPanel;
    private StressPage? stressPage;
    private DashboardWindow? classic;
    private readonly string settingsPath;
    private DeviceToastService? toasts;
    private readonly IlmenyxSettings appSettings;

    public string CurrentPage { get; private set; } = "";

    public ShellWindow()
    {
        Icon = AppIcon.Window; Title = ProductIdentity.Name; Width = 1280; Height = 820; MinWidth = 900; MinHeight = 560; Background = Palette.Bg;
        settingsPath = IlmenyxSettings.DefaultPath(); appSettings = IlmenyxSettings.Load(settingsPath);
        if (appSettings.ThemeLight) Palette.Apply(true);
        BuildPages();
        var theme = new Button { Name = "ToggleTheme", Content = Palette.IsLight ? "Tema escuro" : "Tema claro", Padding = new Thickness(14, 6), Margin = new Thickness(0, 0, 8, 0) };
        theme.Click += (_, _) =>
        {
            Palette.Apply(!Palette.IsLight); appSettings.ThemeLight = Palette.IsLight; if (Content is Grid shellRoot) shellRoot.Background = IlmenyxTheme.Ambient(); theme.Content = Palette.IsLight ? "Tema escuro" : "Tema claro";
            try { appSettings.Save(settingsPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
        var pause = new Button { Name = "ToggleMonitor", Content = "Pausar monitor", Padding = new Thickness(14, 6) };
        pause.Click += async (_, _) =>
        {
            if (usage.IsRunning) await usage.StopAsync(); else usage.Start(stop.Token);
            pause.Content = usage.IsRunning ? "Pausar monitor" : "Retomar monitor";
            if (IsVisible && !stop.IsCancellationRequested) pause.Focus();
        };
        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(30, 10, 16, 6) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (AppIcon.Symbol is { } symbol)
            brand.Children.Add(new Image { Name = "ProductSymbol", Source = symbol, Height = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Name = "ProductName", Text = ProductIdentity.Name, FontWeight = FontWeight.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Name = "MaintainerSignature", Text = ProductIdentity.Signature, FontWeight = FontWeight.Light, FontSize = 13, Margin = new Thickness(10, 0, 0, 0), Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(titlePage);
        header.Children.Add(brand); var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { theme, pause } }; Grid.SetColumn(actions, 1); header.Children.Add(actions);

        var body = new Grid { ColumnDefinitions = new("250,*") };
        var sidebar = Sidebar(); body.Children.Add(sidebar);
        host.Margin = new Thickness(18, 0, 18, 14); Grid.SetColumn(host, 1); body.Children.Add(host);
        var root = new Grid { Name = "ShellRoot", RowDefinitions = new("Auto,*"), Background = IlmenyxTheme.Ambient() };
        Grid.SetRow(body, 1); root.Children.Add(header); root.Children.Add(body);
        Content = root;
        Select("dash");
        Opened += async (_, _) =>
        {
            if (!OperatingSystem.IsLinux()) { hardware.SetStatus("Backend disponível apenas no Linux."); return; }
            usage.Start(stop.Token);
            toasts = new DeviceToastService(() => LinuxDeviceWatcher.Read()) { Enabled = appSettings.DeviceToasts }; toasts.Notified += t => { sessionEvents.Insert(0, (DateTimeOffset.Now, t.Title, t.Text)); if (sessionEvents.Count > 50) sessionEvents.RemoveAt(50); overviewRefresh?.Invoke(); }; toasts.Start();
            _ = RefreshBlockedBadgeAsync();
            await hardware.RefreshAsync(stop.Token);
            await sensors.RefreshAsync(stop.Token);
        };
        Closed += async (_, _) =>
        {
            stop.Cancel(); toasts?.Dispose(); await usage.StopAsync(); usage.Dispose(); sensors.Dispose(); smartImport.Dispose();
            cpu.Dispose(); memory.Dispose(); simulation.Dispose(); cpuPanel?.Dispose(); stressPage?.Stop("Parado: janela fechada");
        };
    }

    public void Select(string key)
    {
        if (!pages.TryGetValue(key, out var entry)) throw new ArgumentException("Página desconhecida: " + key);
        if (entry.Page is null) { var page = entry.Build(); pages[key] = entry = (entry.Title, entry.Build, page); host.Children.Add(page); }
        foreach (var (k, e) in pages) if (e.Page is { } p) p.IsVisible = k == key;
        if (key == "dash") overviewRefresh?.Invoke();
        foreach (var (k, button) in nav) Style(button, k == key);
        CurrentPage = key; titlePage.Text = entry.Title;
    }

    private static void Style(Button b, bool on)
    {
        b.Background = on ? Palette.Card : Brushes.Transparent; b.Foreground = on ? Palette.Fg : Palette.Muted;
        b.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
    }

    private Button NavItem(string key, string label)
    {
        var b = new Button
        {
            Name = "Nav" + key, Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 9), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderThickness = new Thickness(0), Margin = new Thickness(0, 1)
        };
        AutomationProperties.SetName(b, label);
        b.Click += (_, _) => Select(key);
        nav[key] = b; Style(b, false); return b;
    }

    private Border Sidebar()
    {
        var items = new StackPanel { Margin = new Thickness(10, 12, 10, 8) };
        items.Children.Add(NavItem("dash", "Visão geral"));
        void Group(string title, params (string Key, string Label)[] entries)
        {
            var list = new StackPanel { Margin = new Thickness(8, 0, 0, 4) };
            foreach (var (key, label) in entries) list.Children.Add(NavItem(key, label));
            var head = new Button { Content = title, Background = Brushes.Transparent, Foreground = Palette.Faint, FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 2), Padding = new Thickness(14, 6) };
            AutomationProperties.SetName(head, "Grupo " + title);
            head.Click += (_, _) => list.IsVisible = !list.IsVisible;
            items.Children.Add(head); items.Children.Add(list);
        }
        Group("DIAGNÓSTICO", ("system", "Sistema"), ("sensors", "Sensores"), ("gpu", "GPU"), ("disks", "Discos"), ("energy", "Energia"), ("net", "Rede"), ("procs", "Processos"));
        Group("JOGOS", ("launcher", "Jogos"), ("compat", "Compatibilidade"), ("fps", "Estimativa de FPS"), ("bottleneck", "Detector de gargalo"));
        Group("DESEMPENHO", ("bench", "Benchmark"), ("oc", "OC"), ("overlay", "Overlay"), ("validate", "Validações reduzidas"), ("stress", "Stress test"));
        Group("MAKER", ("makerports", "Portas"), ("makerdrv", "Drivers seriais"));
        Group("FERRAMENTAS", ("optimize", "Otimiza"), ("lighting", "Integração"), ("startup", "Inicialização"), ("apps", "Instalar apps"), ("driver", "Drivers"), ("report", "Relatório"), ("blocked", "Bloqueados (!)"), ("tools", "Requisitos e sobre"));

        var temp = new TextBlock { Name = "SideTemp", Text = "—", FontSize = 12, FontFamily = new FontFamily("monospace") };
        var load = new TextBlock { Name = "SideLoad", Text = "—", FontSize = 12, Foreground = Palette.Muted, FontFamily = new FontFamily("monospace") };
        var state = new TextBlock { Name = "SideMonitor", Text = "monitor parado", FontSize = 12, FontFamily = new FontFamily("monospace") };
        usage.Updated += () =>
        {
            temp.Text = usage.Last?.CpuTempC is { } t ? t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " °C" : "—";
            load.Text = UsageViewModel.Pct(usage.Last?.CpuPercent);
        };
        usage.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(usage.IsRunning)) state.Text = usage.IsRunning ? "monitor ativo" : "monitor parado"; };
        var grid = new Grid { ColumnDefinitions = new("46,*,Auto"), RowDefinitions = new("Auto,Auto") };
        void Cell(Control c, int col, int row, int span = 1) { Grid.SetColumn(c, col); Grid.SetRow(c, row); Grid.SetColumnSpan(c, span); grid.Children.Add(c); }
        Cell(new TextBlock { Text = "CPU", Foreground = Palette.Faint, FontSize = 10.5, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 3) }, 0, 0);
        Cell(temp, 1, 0); Cell(load, 2, 0);
        Cell(new TextBlock { Text = "TESTE", Foreground = Palette.Faint, FontSize = 10.5, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 3) }, 0, 1);
        Cell(state, 1, 1, 2);
        var footer = new StackPanel
        {
            Margin = new Thickness(16, 6, 16, 16),
            Children =
            {
                new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10), Child = grid },
                new TextBlock { Name = "SideVersion", Text = "v" + (typeof(ShellWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?"), Foreground = Palette.Faint, FontSize = 11, Margin = new Thickness(4, 10, 0, 0) }
            }
        };
        var dock = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Name = "NavScroll", Content = items });
        return new Border { Name = "Sidebar", Margin = new Thickness(14, 0, 0, 14), Background = Palette.Glass, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.ContainerRadius), Child = dock };
    }

    private Func<string, Task<string?>> Destination(string stem) => extension => JsonSaveDialog.ChooseAsync(this, $"ilmenyx-{stem}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension);

    public void OpenClassicDiagnostics()
    {
        if (classic is { IsVisible: true }) { classic.Activate(); return; }
        classic = new DashboardWindow(); classic.Show(this);
    }
}
