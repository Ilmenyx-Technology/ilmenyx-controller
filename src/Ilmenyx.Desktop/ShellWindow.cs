// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Layout follows the original RESET EXPLODE structure (grouped rounded sidebar,
// header with brand and current page, live footer, overview with chart/chips/components table); no original assets are used.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed partial class ShellWindow : Window
{
    private static readonly LinuxUsageProvider usageProvider = new();
    private readonly UsageViewModel usage = new(usageProvider);
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
    private readonly TextBlock titlePage = new() { Name = "TitlePage", Foreground = Palette.Muted, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
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
        // Header actions: round icon buttons; their accessible name (and Tag) is the action they perform.
        var theme = UiKit.IconButton("ToggleTheme", Palette.IsLight ? Icons.Moon : Icons.Sun, Palette.IsLight ? "Tema escuro" : "Tema claro");
        themeButton = theme;
        theme.Click += (_, _) =>
        {
            Palette.Apply(!Palette.IsLight); appSettings.ThemeLight = Palette.IsLight; RebuildAmbient();
            foreach (var (k, button) in nav) Style(k, button, k == CurrentPage);
            UiKit.SetAction(theme, Palette.IsLight ? Icons.Moon : Icons.Sun, Palette.IsLight ? "Tema escuro" : "Tema claro");
            bool light = appSettings.ThemeLight;
            try { IlmenyxSettings.Update(settingsPath, s => s.ThemeLight = light); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
        var pause = UiKit.IconButton("ToggleMonitor", Icons.Pause, "Pausar monitor");
        pause.Click += async (_, _) =>
        {
            if (usage.IsRunning) await usage.StopAsync(); else usage.Start(stop.Token);
            UiKit.SetAction(pause, usage.IsRunning ? Icons.Pause : Icons.Play, usage.IsRunning ? "Pausar monitor" : "Retomar monitor");
            if (IsVisible && !stop.IsCancellationRequested) pause.Focus();
        };
        var header = new Grid { Name = "ShellHeader", ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 18, 22, 14) };
        // Context help: "?" next to the page title opens the Help Center at this page's section.
        var pageHelp = UiKit.IconButton("PageHelp", Icons.Help, help[HelpKeys.HelpContext], 28);
        pageHelp.Click += (_, _) => OpenHelp(CurrentPage);
        pageHelpButton = pageHelp;
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0), Children = { titlePage, pageHelp } });
        var searchBox = SearchBox(); Grid.SetColumn(searchBox, 1); header.Children.Add(searchBox);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Children = { theme, pause, MachineChip() } };
        Grid.SetColumn(actions, 2); header.Children.Add(actions);

        var main = new Grid { RowDefinitions = new("Auto,*") };
        main.Children.Add(header);
        host.Margin = new Thickness(0, 0, 22, 18); Grid.SetRow(host, 1); main.Children.Add(host);
        var body = new Grid { ColumnDefinitions = new("214,*") };
        body.Children.Add(Sidebar()); Grid.SetColumn(main, 1); body.Children.Add(main);
        var root = new Grid { Name = "ShellRoot", Background = Palette.Bg };
        foreach (var layer in IlmenyxTheme.AmbientLayers()) root.Children.Add(layer);
        root.Children.Add(body);
        root.Children.Add(tourLayer);
        Content = root;
        Select("dash");
        Opened += async (_, _) =>
        {
            if (!OperatingSystem.IsLinux()) { hardware.SetStatus("Backend disponível apenas no Linux."); return; }
            usage.Start(stop.Token);
            toasts = new DeviceToastService(() => LinuxDeviceWatcher.Read()) { Enabled = appSettings.DeviceToasts }; toasts.Notified += t => { sessionEvents.Insert(0, (DateTimeOffset.Now, t.Title, t.Text)); if (sessionEvents.Count > 50) sessionEvents.RemoveAt(50); overviewRefresh?.Invoke(); }; toasts.Start();
            _ = RefreshBlockedBadgeAsync();
            _ = FirstRunAsync();
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
        foreach (var (k, button) in nav) Style(k, button, k == key);
        CurrentPage = key; titlePage.Text = entry.Title;
        if (pageHelpButton is not null) pageHelpButton.IsVisible = key != "help";
    }

    private readonly Dictionary<string, (TextBlock Label, Path Icon, Border Bar)> navParts = new();
    private static readonly Dictionary<string, string> NavIcons = new()
    {
        ["dash"] = Icons.Overview, ["system"] = Icons.System, ["sensors"] = Icons.Sensors, ["gpu"] = Icons.Gpu, ["disks"] = Icons.Disks, ["energy"] = Icons.Energy, ["net"] = Icons.Network,
        ["procs"] = Icons.Processes, ["launcher"] = Icons.Games, ["compat"] = Icons.Compat, ["gamecheck"] = Icons.Validate, ["fps"] = Icons.Fps, ["bottleneck"] = Icons.Bottleneck, ["bench"] = Icons.Bench, ["oc"] = Icons.Oc,
        ["overlay"] = Icons.Overlay, ["validate"] = Icons.Validate, ["stress"] = Icons.Stress, ["makerports"] = Icons.Ports, ["makerdrv"] = Icons.SerialDrivers, ["optimize"] = Icons.Optimize,
        ["lighting"] = Icons.Lighting, ["startup"] = Icons.Startup, ["apps"] = Icons.Apps, ["driver"] = Icons.Drivers, ["nvdriver"] = Icons.Gpu, ["report"] = Icons.Report, ["blocked"] = Icons.Blocked, ["tools"] = Icons.About, ["help"] = Icons.Help,
    };

    /// <summary>Active item: a small accent bar at the left edge, accent text and icon. No filled capsule.</summary>
    private void Style(string key, Button b, bool on)
    {
        // Dark: only the bar and accent text. Light (white reference): also a pale accent capsule.
        var a = ((ISolidColorBrush)Palette.Accent).Color;
        b.Background = on && Palette.IsLight ? new SolidColorBrush(Color.FromArgb(0x1C, a.R, a.G, a.B)) : Brushes.Transparent;
        if (!navParts.TryGetValue(key, out var p)) return;
        p.Label.Foreground = on ? Palette.AccentText : Palette.Muted; p.Label.FontWeight = on ? FontWeight.SemiBold : FontWeight.Medium;
        p.Icon.Fill = on ? Palette.AccentText : Palette.Faint; p.Bar.Opacity = on ? 1 : 0;
    }

    private void SetNavLabel(string key, string text) { if (navParts.TryGetValue(key, out var p)) p.Label.Text = text; }

    private Button NavItem(string key, string label)
    {
        var bar = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Background = Palette.Accent, Opacity = 0, VerticalAlignment = VerticalAlignment.Center,
            Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = IlmenyxTheme.Normal, Easing = IlmenyxTheme.Premium } } };
        var icon = UiKit.Icon(NavIcons.GetValueOrDefault(key, Icons.ChevronRight), 17, Palette.Faint);
        var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new Grid { ColumnDefinitions = new("14,26,*") };
        row.Children.Add(bar); Grid.SetColumn(icon, 1); row.Children.Add(icon); Grid.SetColumn(text, 2); row.Children.Add(text);
        var b = new Button
        {
            Name = "Nav" + key, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 8, 10, 8), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), BorderThickness = new Thickness(0), Margin = new Thickness(0, 1), Background = Brushes.Transparent,
        };
        AutomationProperties.SetName(b, label);
        b.Click += (_, _) => Select(key);
        nav[key] = b; navParts[key] = (text, icon, bar); Style(key, b, false); return b;
    }

    /// <summary>Sidebar on the same surface as the content: brand, grouped navigation and a compact live readout; separation by spacing only.</summary>
    private Control Sidebar()
    {
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 22, 12, 20) };
        if (AppIcon.Symbol is { } symbol)
            brand.Children.Add(new Image { Name = "ProductSymbol", Source = symbol, Height = 26, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = {
            new TextBlock { Name = "ProductName", Text = ProductIdentity.Name, FontWeight = FontWeight.Bold, FontSize = 16 },
            new TextBlock { Name = "MaintainerSignature", Text = ProductIdentity.Signature, FontSize = 11, Foreground = Palette.Faint } } });

        var items = new StackPanel { Margin = new Thickness(8, 0, 12, 8) };
        items.Children.Add(NavItem("dash", "Visão geral"));
        void Group(string title, params (string Key, string Label)[] entries)
        {
            var list = new StackPanel();
            foreach (var (key, label) in entries) list.Children.Add(NavItem(key, label));
            var chevron = UiKit.Icon(Icons.ChevronDown, 12, Palette.Faint);
            var head = new Button
            {
                Content = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Faint, LetterSpacing = 0.8 }, chevron } },
                Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 14, 0, 2), Padding = new Thickness(14, 4, 10, 4),
            };
            Grid.SetColumn(chevron, 1);
            AutomationProperties.SetName(head, "Grupo " + title);
            head.Click += (_, _) => { list.IsVisible = !list.IsVisible; chevron.Data = Geometry.Parse(list.IsVisible ? Icons.ChevronDown : Icons.ChevronRight); };
            foreach (var (key, _) in entries) navExpand[key] = () => { if (!list.IsVisible) { list.IsVisible = true; chevron.Data = Geometry.Parse(Icons.ChevronDown); } };
            items.Children.Add(head); items.Children.Add(list);
        }
        Group("DIAGNÓSTICO", ("system", "Sistema"), ("sensors", "Sensores"), ("gpu", "GPU"), ("disks", "Discos"), ("energy", "Energia"), ("net", "Rede"), ("procs", "Processos"));
        Group("JOGOS", ("launcher", "Jogos"), ("compat", "Compatibilidade"), ("gamecheck", "Jogo não abre?"), ("fps", "Estimativa de FPS"), ("bottleneck", "Detector de gargalo"));
        Group("DESEMPENHO", ("bench", "Benchmark"), ("oc", "OC"), ("overlay", "Overlay"), ("validate", "Validações reduzidas"), ("stress", "Stress test"));
        Group("MAKER", ("makerports", "Portas"), ("makerdrv", "Drivers seriais"));
        Group("FERRAMENTAS", ("optimize", "Otimiza"), ("lighting", "Integração"), ("startup", "Inicialização"), ("apps", "Instalar apps"), ("driver", "Drivers"), ("nvdriver", "Driver NVIDIA"), ("report", "Relatório"), ("blocked", "Bloqueados (!)"), ("tools", "Requisitos e sobre"));
        // Help Center at the bottom of the menu (also reachable from the header "?" and the search).
        var helpItem = NavItem("help", help[HelpKeys.HelpNav]); helpItem.Margin = new Thickness(0, 14, 0, 1); items.Children.Add(helpItem);

        var temp = new TextBlock { Name = "SideTemp", Text = "—", FontSize = 12, FontFamily = IlmenyxTheme.Mono };
        var load = new TextBlock { Name = "SideLoad", Text = "—", FontSize = 12, Foreground = Palette.Muted, FontFamily = IlmenyxTheme.Mono };
        var state = new TextBlock { Name = "SideMonitor", Text = "monitor parado", FontSize = 11, Foreground = Palette.Faint };
        usage.Updated += () =>
        {
            temp.Text = usage.Last?.CpuTempC is { } t ? t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " °C" : "—";
            load.Text = UsageViewModel.Pct(usage.Last?.CpuPercent);
        };
        usage.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(usage.IsRunning)) state.Text = usage.IsRunning ? "monitor ativo" : "monitor parado"; };
        var readout = new Grid { ColumnDefinitions = new("Auto,*,Auto"), RowDefinitions = new("Auto,Auto") };
        var cpuIcon = UiKit.Icon(Icons.Cpu, 15, Palette.Faint); cpuIcon.Margin = new Thickness(0, 0, 8, 0);
        readout.Children.Add(cpuIcon); Grid.SetColumn(temp, 1); readout.Children.Add(temp); Grid.SetColumn(load, 2); readout.Children.Add(load);
        Grid.SetRow(state, 1); Grid.SetColumnSpan(state, 3); state.Margin = new Thickness(0, 4, 0, 0); readout.Children.Add(state);
        // The original shows the reason and a CORRIGIR button when CPU temperature/power are missing; here: reason + command (never run).
        // A visible call to action (a small text link was missed by the owner in a real test, 2026-11-07).
        var fix = new Button
        {
            Name = "SensorHelp", IsVisible = false, Background = Palette.WarnBg, Foreground = Palette.Warn, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 7), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = {
                UiKit.Icon(Icons.Blocked, 15, Palette.Warn),
                new StackPanel { Children = {
                    new TextBlock { Text = "Liberar sensores da CPU", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.Warn },
                    new TextBlock { Text = "consumo/temperatura bloqueados", FontSize = 11, Foreground = Palette.Warn, Opacity = 0.8 } } } } },
        };
        AutomationProperties.SetName(fix, "Por que a temperatura ou o consumo da CPU não aparecem");
        fix.Click += async (_, _) => await new SensorHelpDialog(CurrentFixes(), PrivilegedFix.Available ? script => PrivilegedFix.RunAsync(script, stop.Token) : null).ShowDialog(this);
        usage.Updated += () => fix.IsVisible = CurrentFixes().Count > 0;
        Grid.SetRow(fix, 2); Grid.SetColumnSpan(fix, 3); readout.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); readout.Children.Add(fix);
        sensorFixButton = fix;
        var footer = new StackPanel
        {
            Margin = new Thickness(14, 6, 14, 18),
            Children =
            {
                (sideReadout = new Border { Name = "SideReadout", Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(14, 11), Child = readout }),
                new TextBlock { Name = "SideVersion", Text = "v" + (typeof(ShellWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?"), Foreground = Palette.Faint, FontSize = 11, Margin = new Thickness(6, 10, 0, 0) }
            }
        };
        var dock = new DockPanel { Name = "Sidebar" };
        DockPanel.SetDock(brand, Dock.Top); dock.Children.Add(brand);
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        dock.Children.Add(navScroll = new ScrollViewer { Name = "NavScroll", Content = items });
        return dock;
    }

    /// <summary>Page finder: type part of a page name, a Help Center section or a common question and pick it (offline, nothing leaves the app).</summary>
    private Control SearchBox()
    {
        var targets = new Dictionary<string, Action>(StringComparer.Ordinal);
        foreach (var (key, entry) in pages) { var k = key; targets.TryAdd(entry.Title, () => Select(k)); }
        foreach (var section in help.Sections)
        {
            var k = section.Key;
            targets.TryAdd(help[HelpKeys.HelpSearchPrefix] + section.Title, () => OpenHelp(k));
            foreach (var faq in section.Faq) targets.TryAdd(faq.Question, () => OpenHelp(k));
        }
        var box = new AutoCompleteBox
        {
            Name = "GlobalSearch", PlaceholderText = "Buscar no Ilmenyx (páginas, ferramentas e ajuda)", HorizontalAlignment = HorizontalAlignment.Stretch, MinimumPrefixLength = 1,
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal, ItemsSource = targets.Keys.OrderBy(t => t, StringComparer.CurrentCulture).ToList(), Padding = new Thickness(34, 7, 10, 7),
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string title && targets.TryGetValue(title, out var go)) { go(); box.Text = ""; }
        };
        var icon = UiKit.Icon(Icons.Search, 16, Palette.Faint); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); icon.IsHitTestVisible = false;
        // Flexible: shrinks with the window (at 900 px it must not run under the header actions), up to 460 px.
        var host = new Grid { Name = "GlobalSearchHost", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 160, MaxWidth = 460, Children = { box, icon } };
        searchHost = host;
        return host;
    }

    /// <summary>Where the reference has the user profile: this machine (DMI product name, read-only). Opens the System page.</summary>
    private Button MachineChip()
    {
        string model = "Este PC";
        try { if (File.Exists("/sys/class/dmi/id/product_name") && File.ReadAllText("/sys/class/dmi/id/product_name").Trim() is { Length: > 0 and < 80 } m) model = m.Split('_')[0]; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var avatar = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Palette.AccentContainer, Child = UiKit.Icon(Icons.System, 16, Palette.OnAccentContainer) };
        var chip = new Button
        {
            Name = "MachineChip", Background = Brushes.Transparent, Padding = new Thickness(4, 2, 8, 2), CornerRadius = new CornerRadius(IlmenyxTheme.Pill),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { avatar, new TextBlock { Text = model, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis }, UiKit.Icon(Icons.ChevronDown, 12, Palette.Faint) } },
        };
        AutomationProperties.SetName(chip, "Este computador: " + model + " — abrir Sistema");
        chip.Click += (_, _) => Select("system");
        return chip;
    }

    /// <summary>
    /// First run: once the first reading shows what is blocked, offer every fix together (one password). Shown at most once per user
    /// (settings PermissionsAsked); the sidebar button keeps the same fixes for later. ILMENYX_NO_ONBOARDING=1 skips it (automated runs).
    /// The returned task completes once the screen is closed (or nothing needed it), so the welcome tour waits for it.
    /// </summary>
    private Task OfferPermissionsOnce()
    {
        if (appSettings.PermissionsAsked || Environment.GetEnvironmentVariable("ILMENYX_NO_ONBOARDING") == "1" || !PrivilegedFix.Available) return Task.CompletedTask;
        // Completes when the screen was closed or there was nothing to offer, so the welcome tour comes only after it.
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnUpdated()
        {
            if (usage.Last is null) return;
            usage.Updated -= OnUpdated;
            var fixes = CurrentFixes().Where(f => f.Apply is { Length: > 0 }).ToList();
            if (fixes.Count == 0) { settled.TrySetResult(); return; } // nothing blocked now; ask the first time something is
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    try { IlmenyxSettings.Update(settingsPath, s => s.PermissionsAsked = true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    appSettings.PermissionsAsked = true;
                    await new PermissionsOnboardingDialog(fixes, script => PrivilegedFix.RunAsync(script, stop.Token)).ShowDialog(this);
                }
                finally { settled.TrySetResult(); }
            });
        }
        usage.Updated += OnUpdated;
        return settled.Task;
    }

    private IReadOnlyList<SensorFix> CurrentFixes()
    {
        var access = usageProvider.CpuPowerState switch { RaplState.Available => CpuPowerAccess.Available, RaplState.NoPermission => CpuPowerAccess.NoPermission, RaplState.NotPresent => CpuPowerAccess.NotPresent, _ => CpuPowerAccess.Unknown };
        bool vm = File.Exists("/sys/class/dmi/id/sys_vendor") && SysfsText.Read("/sys/class/dmi/id/sys_vendor", 128) is var v && (v.Contains("QEMU") || v.Contains("VMware") || v.Contains("innotek") || v.Contains("Microsoft Corporation") || v.Contains("Xen"));
        return SensorHelp.Build(usage.Last is { } l && l.CpuTempC is null, access, usage.Last?.CpuModel, usageProvider.CpuPowerZone, vm);
    }

    private void RebuildAmbient()
    {
        if (Content is not Grid root) return;
        foreach (var old in root.Children.OfType<Border>().Where(b => b.Name?.StartsWith("Ambient", StringComparison.Ordinal) == true).ToList()) root.Children.Remove(old);
        var layers = IlmenyxTheme.AmbientLayers();
        for (int i = 0; i < layers.Count; i++) root.Children.Insert(i, layers[i]);
    }

    private Func<string, Task<string?>> Destination(string stem) => extension => JsonSaveDialog.ChooseAsync(this, $"ilmenyx-{stem}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension);

    public void OpenClassicDiagnostics()
    {
        if (classic is { IsVisible: true }) { classic.Activate(); return; }
        classic = new DashboardWindow(); classic.Show(this);
    }
}
