// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Overlay tab: item picker with presets, corner, scale, on/off): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed record OverlayServices(Func<CancellationToken, Task<GpuTelemetry?>> Gpu, Func<DateTimeOffset, FrameStats?> Frames, string ConfigPath, string LogFolder, Func<bool> MangoHudInstalled);

/// <summary>Overlay settings page. The Ilmenyx window shows system metrics (and FPS when a game runs under the generated MangoHud config); MangoHud draws in-game.</summary>
public sealed class OverlayPage : StackPanel
{
    private readonly OverlayServices services;
    private readonly UsageViewModel usage;
    private readonly IlmenyxSettings settings;
    private readonly string settingsPath;
    private readonly CancellationToken lifetime;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel items = new() { Name = "OvItemsPanel", Spacing = 2 };
    private readonly TextBlock preview = new() { Name = "OverlayPreview", FontFamily = IlmenyxTheme.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Fg };
    private readonly Border previewBox = new() { Background = Palette.Glass, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(10, 8), Margin = new Thickness(12), MaxWidth = 260 };
    private readonly Button toggle = UiKit.Primary("BtnOverlayToggle", L.T("Mostrar overlay"), 22, 11);
    private readonly TextBlock mangoState = new() { Name = "MangoState", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private readonly TextBox launch = new() { Name = "OvLaunchOption", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = IlmenyxTheme.Mono };
    private readonly TextBlock saved = new() { Name = "OvSaved", Text = L.T("✓ Salvo"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.Success, Opacity = 0, VerticalAlignment = VerticalAlignment.Center };
    // Changes are saved automatically, a short moment after the last click (a burst of clicks writes once).
    private readonly DispatcherTimer saveDelay = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer savedFade = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool pendingSave;
    private readonly TextBlock status = new() { Name = "OverlayStatus", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12 };
    private HashSet<string> show;
    private OverlayWindow? window;
    private GpuTelemetry? gpu; private int tick;
    private UiKit.ChipGroup<string>? corners; private UiKit.ChipGroup<double>? scales;
    public bool OverlayVisible => window is { IsVisible: true };
    public IReadOnlyCollection<string> Selected => show;
    public IReadOnlyList<OverlayLine> Lines { get; private set; } = [];
    /// <summary>Number of completed automatic saves (settings + Ilmenyx MangoHud config).</summary>
    public int SaveCount { get; private set; }
    public bool SavePending => pendingSave;

    public OverlayPage(OverlayServices services, UsageViewModel usage, string settingsPath, CancellationToken lifetime)
    {
        this.services = services; this.usage = usage; this.settingsPath = settingsPath; this.lifetime = lifetime; Spacing = 4;
        settings = IlmenyxSettings.Load(settingsPath);
        show = OverlayMetrics.Sanitize(settings.OverlayItems).ToHashSet();
        Children.Add(UiKit.PageHeader("Overlay", L.T("Métricas sobre a tela. A janela do Ilmenyx mostra o sistema; dentro do jogo, o MangoHud desenha com o mesmo conjunto.")));
        BuildItems();
        Button Preset(string name, string text, Func<HashSet<string>> keys)
        {
            var b = new Button { Name = name, Content = text, FontSize = 11, Padding = new Thickness(13, 5), Margin = new Thickness(6, 0, 0, 0), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Background = Palette.Card, Foreground = Palette.Muted, BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
            b.Click += (_, _) => { show = keys(); Changed(); BuildItems(); }; return b;
        }
        corners = new("OvCornerPanel", [(L.T("Superior esq."), "top-left"), (L.T("Superior dir."), "top-right"), (L.T("Inferior esq."), "bottom-left"), (L.T("Inferior dir."), "bottom-right")], settings.OverlayCorner, c => { settings.OverlayCorner = c; Changed(); });
        scales = new("OvScalePanel", [("80%", 0.8), ("100%", 1.0), ("125%", 1.25), ("150%", 1.5)], NearestScale(settings.OverlayScale), v => { settings.OverlayScale = v; Changed(); });
        toggle.Click += (_, _) => ToggleWindow();

        // Hero: a live preview of the overlay on a mock screen, placed at the chosen corner and scale.
        previewBox.Child = preview;
        var screen = new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), MinHeight = 196, ClipToBounds = true, Child = previewBox };
        var side = new DockPanel { Margin = new Thickness(0, 0, 20, 0), Width = 210 };
        var intro = new StackPanel { Spacing = 6, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { UiKit.Icon(Icons.Overlay, 14, Palette.AccentText), UiKit.Label(L.T("OVERLAY DO ILMENYX")) } },
            new TextBlock { Text = L.T("Prévia ao vivo"), FontSize = 20, FontWeight = FontWeight.Bold },
            new TextBlock { Text = L.T("Os itens, o canto e o tamanho escolhidos abaixo valem para a janela do Ilmenyx e para a configuração do MangoHud."), FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } };
        DockPanel.SetDock(toggle, Dock.Bottom); toggle.HorizontalAlignment = HorizontalAlignment.Left; toggle.Margin = new Thickness(0, 14, 0, 0);
        side.Children.Add(toggle); side.Children.Add(intro);
        var heroGrid = new Grid { ColumnDefinitions = new("Auto,*") }; Grid.SetColumn(screen, 1); heroGrid.Children.Add(side); heroGrid.Children.Add(screen);
        Children.Add(BenchPage.Themed(UiKit.Hero(heroGrid, height: double.NaN, name: "OverlayHero")));
        Children.Add(new TextBlock { Text = L.T("É uma janela comum do Ilmenyx, sem injeção em jogos. Se fica acima de um jogo em tela cheia, e se os cliques passam por ela, depende do gerenciador de janelas (no Wayland o compositor decide). Para o jogo, use o MangoHud abaixo. FPS só aparece para jogos iniciados com a configuração do MangoHud do Ilmenyx (abaixo)."), FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 2, 0) });

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Children = { Preset("OvPresetDefault", L.T("Padrão"), OverlayMetrics.Defaults), Preset("OvPresetMin", L.T("Mínimo"), OverlayMetrics.Minimal), Preset("OvPresetAll", L.T("Tudo"), OverlayMetrics.Everything) } };
        Children.Add(UiKit.SectionHeader(L.T("O que mostrar"), presets));
        Children.Add(UiKit.Card(items));
        Children.Add(UiKit.SectionHeader(L.T("Posição e tamanho"), saved));
        Children.Add(UiKit.Card(new WrapPanel { Children = { Field(L.T("CANTO"), corners.Panel), Field(L.T("TAMANHO"), scales.Panel) } }));
        Children.Add(UiKit.SectionHeader(L.T("MangoHud · overlay dentro do jogo")));
        launch.Text = MangoHudConfig.LaunchOption(services.ConfigPath); AutomationProperties.SetName(launch, L.T("Opção de inicialização da Steam"));
        var launchLabel = UiKit.Label(L.T("OPÇÃO DE INICIALIZAÇÃO DA STEAM (OPCIONAL)"));
        status.Text = L.T("As escolhas desta página são salvas sozinhas e valem para a janela do Ilmenyx e para o MangoHud dos jogos abertos pela página Jogos com MangoHud marcado. ")
            + L.T("Se você abre o jogo direto pela Steam, copie a linha acima em Propriedades → Opções de inicialização.");
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { mangoState, launchLabel, launch, status } }));
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); SaveNow(); };
        savedFade.Tick += (_, _) => { savedFade.Stop(); saved.Opacity = 0; };
        saved.Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = IlmenyxTheme.Fast }];
        // Leaving the page or closing the window never loses a change still waiting for the delay.
        DetachedFromVisualTree += (_, _) => FlushSave();
        PlacePreview();
        timer.Tick += async (_, _) => await TickAsync(); timer.Start();
        AttachedToVisualTree += (_, _) => { if (!timer.IsEnabled) timer.Start(); };
        lifetime.Register(() => Dispatcher.UIThread.Post(Shutdown));
        RefreshMangoState(); _ = TickAsync();
    }

    private static double NearestScale(double v) => new[] { 0.8, 1.0, 1.25, 1.5 }.OrderBy(x => Math.Abs(x - v)).First();
    private static Control Field(string name, Control c) => new StackPanel { Margin = new Thickness(0, 0, 24, 6), Children = { UiKit.Label(name), new Border { Height = 4 }, c } };

    private void BuildItems()
    {
        items.Children.Clear();
        bool firstGroup = true;
        foreach (var group in OverlayMetrics.All.GroupBy(m => m.Group))
        {
            var head = UiKit.Label(group.Key.ToUpperInvariant()); head.Margin = new Thickness(0, firstGroup ? 0 : 10, 0, 6); firstGroup = false;
            items.Children.Add(head);
            var wrap = new WrapPanel();
            foreach (var m in group)
            {
                var cb = new CheckBox { Name = "Ov_" + m.Key, Content = m.Label, IsChecked = show.Contains(m.Key), FontSize = 12, MinHeight = 0, Padding = new Thickness(4, 0, 0, 0) };
                var chip = new Border { CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, 12, 1), Margin = new Thickness(0, 0, 6, 6), Child = cb };
                void Paint() { bool on = cb.IsChecked == true; chip.Background = on ? Palette.Hover : Palette.Card; chip.BorderBrush = on ? Palette.Accent : Palette.Line; cb.Foreground = on ? Palette.Fg : Palette.Muted; }
                cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) show.Add(m.Key); else show.Remove(m.Key); Paint(); Changed(); };
                Paint();
                wrap.Children.Add(chip);
            }
            items.Children.Add(wrap);
        }
    }

    /// <summary>Places the preview box at the chosen corner of the mock screen, at the chosen scale.</summary>
    private void PlacePreview()
    {
        string corner = settings.OverlayCorner ?? "top-left";
        previewBox.HorizontalAlignment = corner.EndsWith("right", StringComparison.Ordinal) ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        previewBox.VerticalAlignment = corner.StartsWith("bottom", StringComparison.Ordinal) ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        preview.FontSize = 12 * Math.Clamp(settings.OverlayScale, 0.8, 1.5);
    }

    private void Changed()
    {
        settings.OverlayItems = OverlayMetrics.All.Select(m => m.Key).Where(show.Contains).ToList();
        pendingSave = true; saveDelay.Stop(); saveDelay.Start();
        _ = TickAsync();
    }

    /// <summary>Writes a pending change now (used when leaving the page).</summary>
    public void FlushSave() { if (pendingSave) { saveDelay.Stop(); SaveNow(); } }

    /// <summary>Saves the selection in the settings and rewrites Ilmenyx's own MangoHud config (the user's MangoHud.conf is never touched).</summary>
    private void SaveNow()
    {
        pendingSave = false;
        var (items, corner, scale) = ((settings.OverlayItems ?? []).ToList(), settings.OverlayCorner, settings.OverlayScale);
        try { IlmenyxSettings.Update(settingsPath, s => { s.OverlayItems = items; s.OverlayCorner = corner; s.OverlayScale = scale; }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowError(L.T("Configuração não salva: ") + e.Message); return; }
        if (OperatingSystem.IsLinux() && !WriteMango()) return;
        SaveCount++;
        saved.Text = L.T("✓ Salvo"); saved.Foreground = Palette.Success; saved.Opacity = 1; savedFade.Stop(); savedFade.Start();
    }

    private void ShowError(string text)
    {
        saved.Text = L.T("Não salvo"); saved.Foreground = Palette.Error; saved.Opacity = 1; savedFade.Stop();
        status.Text = text;
    }

    private void RefreshMangoState()
    {
        mangoState.Text = services.MangoHudInstalled()
            ? L.T("MangoHud encontrado. O Ilmenyx usa um arquivo de configuração próprio, atualizado sozinho quando você muda algo acima; o seu MangoHud.conf não é alterado.")
            : L.T("MangoHud não está instalado (pacote mangohud da sua distribuição). As escolhas ficam salvas para quando ele estiver instalado.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private bool WriteMango()
    {
        try
        {
            string config = MangoHudConfig.Build(show, settings.OverlayCorner, settings.OverlayScale, services.LogFolder);
            MangoHudLogReader.WriteConfig(services.ConfigPath, config, services.LogFolder);
            status.Text = L.T($"Salvo em {services.ConfigPath}. Vale para a janela do Ilmenyx e para os jogos abertos pela página Jogos com MangoHud marcado. ")
                + L.T($"Se você abre o jogo direto pela Steam, copie a linha acima em Propriedades → Opções de inicialização. O registro de FPS (CSV) fica em {services.LogFolder} e só é lido enquanto um jogo está rodando.");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { ShowError(L.T("Não foi possível salvar a configuração do MangoHud: ") + e.Message); return false; }
    }

    private void ToggleWindow()
    {
        if (window is { IsVisible: true }) { window.Close(); window = null; toggle.Content = L.T("Mostrar overlay"); return; }
        window = new OverlayWindow { Corner = settings.OverlayCorner }; window.SetScale(settings.OverlayScale);
        window.Closed += (_, _) => { window = null; toggle.Content = L.T("Mostrar overlay"); };
        window.Show(); toggle.Content = L.T("Ocultar overlay"); _ = TickAsync();
    }

    public async Task TickAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        if (tick++ % 2 == 0) { try { gpu = await services.Gpu(lifetime); } catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { gpu = null; } }
        var frames = services.Frames(DateTimeOffset.UtcNow);
        Lines = OverlayLines.Build(show, usage.Last, gpu, frames, null, DateTimeOffset.Now);
        preview.Text = Lines.Count == 0 ? L.T("Nenhum item selecionado.") : string.Join("\n", Lines.Select(l => (l.Label.Length > 0 ? l.Label.PadRight(5) : "") + l.Value));
        PlacePreview();
        if (window is { IsVisible: true } w)
        {
            w.Corner = settings.OverlayCorner; w.SetScale(settings.OverlayScale);
            w.Update(Lines, frames?.RecentFrametimesMs ?? [], show.Contains("graph"));
        }
    }

    private void Shutdown() { FlushSave(); timer.Stop(); savedFade.Stop(); window?.Close(); window = null; }
}
