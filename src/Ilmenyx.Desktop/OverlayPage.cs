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
    private readonly TextBlock preview = new() { Name = "OverlayPreview", FontFamily = new FontFamily("monospace"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button toggle = UiKit.Primary("BtnOverlayToggle", "MOSTRAR OVERLAY", 22, 11);
    private readonly TextBlock mangoState = new() { Name = "MangoState", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private readonly TextBox launch = new() { Name = "OvLaunchOption", IsReadOnly = true, IsVisible = false, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace") };
    private readonly TextBlock status = new() { Name = "OverlayStatus", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12 };
    private HashSet<string> show;
    private OverlayWindow? window;
    private GpuTelemetry? gpu; private int tick;
    private UiKit.ChipGroup<string>? corners; private UiKit.ChipGroup<double>? scales;
    public bool OverlayVisible => window is { IsVisible: true };
    public IReadOnlyCollection<string> Selected => show;
    public IReadOnlyList<OverlayLine> Lines { get; private set; } = [];

    public OverlayPage(OverlayServices services, UsageViewModel usage, string settingsPath, CancellationToken lifetime)
    {
        this.services = services; this.usage = usage; this.settingsPath = settingsPath; this.lifetime = lifetime; Spacing = 4;
        settings = IlmenyxSettings.Load(settingsPath);
        show = OverlayMetrics.Sanitize(settings.OverlayItems).ToHashSet();
        Children.Add(new StackPanel { Margin = new Thickness(0, 4, 0, 20), Children = { new TextBlock { Text = "Overlay", FontSize = 26, FontWeight = FontWeight.Bold }, new TextBlock { Text = "Métricas sobre a tela. A janela do Ilmenyx mostra o sistema; dentro do jogo, o MangoHud desenha com o mesmo conjunto.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } });
        BuildItems();
        Button Preset(string name, string text, Func<HashSet<string>> keys) { var b = new Button { Name = name, Content = text }; b.Click += (_, _) => { show = keys(); Changed(); BuildItems(); }; return b; }
        corners = new("OvCornerPanel", [("SUPERIOR ESQ.", "top-left"), ("SUPERIOR DIR.", "top-right"), ("INFERIOR ESQ.", "bottom-left"), ("INFERIOR DIR.", "bottom-right")], settings.OverlayCorner, c => { settings.OverlayCorner = c; Changed(); });
        scales = new("OvScalePanel", [("80%", 0.8), ("100%", 1.0), ("125%", 1.25), ("150%", 1.5)], NearestScale(settings.OverlayScale), v => { settings.OverlayScale = v; Changed(); });
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { UiKit.Label("O QUE MOSTRAR"), items,
            new WrapPanel { Children = { Preset("OvPresetDefault", "PADRÃO", OverlayMetrics.Defaults), Preset("OvPresetMin", "MÍNIMO", OverlayMetrics.Minimal), Preset("OvPresetAll", "TUDO", OverlayMetrics.Everything) } },
            new WrapPanel { Children = { Field("CANTO", corners.Panel), Field("TAMANHO", scales.Panel) } } } }));
        toggle.Click += (_, _) => ToggleWindow();
        Children.Add(UiKit.Card(new StackPanel { Spacing = 8, Children = { UiKit.Label("OVERLAY DO ILMENYX"), toggle, UiKit.Label("PRÉVIA"), preview,
            new TextBlock { Text = "É uma janela comum do Ilmenyx, sem injeção em jogos. Se fica acima de um jogo em tela cheia, e se os cliques passam por ela, depende do gerenciador de janelas (no Wayland o compositor decide). Para o jogo, use o MangoHud abaixo. FPS só aparece para jogos iniciados com a configuração gerada abaixo.", FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap } } }));
        var gen = new Button { Name = "BtnMangoConfig", Content = "GERAR CONFIGURAÇÃO DO MANGOHUD" }; gen.Click += (_, _) => { if (OperatingSystem.IsLinux()) WriteMango(); };
        Children.Add(UiKit.Card(new StackPanel { Spacing = 8, Children = { UiKit.Label("MANGOHUD (OVERLAY DENTRO DO JOGO)"), mangoState, gen, launch, status } }));
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
        foreach (var group in OverlayMetrics.All.GroupBy(m => m.Group))
        {
            items.Children.Add(new TextBlock { Text = group.Key.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, Margin = new Thickness(0, 8, 0, 2) });
            var wrap = new WrapPanel();
            foreach (var m in group)
            {
                var cb = new CheckBox { Name = "Ov_" + m.Key, Content = m.Label, IsChecked = show.Contains(m.Key), Margin = new Thickness(0, 0, 18, 4) };
                cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) show.Add(m.Key); else show.Remove(m.Key); Changed(); };
                wrap.Children.Add(cb);
            }
            items.Children.Add(wrap);
        }
    }

    private void Changed()
    {
        settings.OverlayItems = OverlayMetrics.All.Select(m => m.Key).Where(show.Contains).ToList();
        try { settings.Save(settingsPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Configuração não salva: " + e.Message; }
        _ = TickAsync();
    }

    private void RefreshMangoState()
    {
        mangoState.Text = services.MangoHudInstalled()
            ? "MangoHud encontrado. A configuração gerada é um arquivo próprio do Ilmenyx; o seu MangoHud.conf não é alterado."
            : "MangoHud não está instalado (pacote mangohud da sua distribuição). A configuração pode ser gerada mesmo assim.";
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void WriteMango()
    {
        try
        {
            string config = MangoHudConfig.Build(show, settings.OverlayCorner, settings.OverlayScale, services.LogFolder);
            MangoHudLogReader.WriteConfig(services.ConfigPath, config, services.LogFolder);
            launch.Text = MangoHudConfig.LaunchOption(services.ConfigPath); launch.IsVisible = true;
            status.Text = $"Configuração salva em {services.ConfigPath}. Copie a opção acima nas opções de inicialização do jogo (Steam: Propriedades → Opções de inicialização) ou marque MangoHud no Launcher. O registro de FPS (CSV) é gravado em {services.LogFolder} e lido só enquanto um jogo estiver rodando.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { status.Text = "Não foi possível gerar a configuração: " + e.Message; }
    }

    private void ToggleWindow()
    {
        if (window is { IsVisible: true }) { window.Close(); window = null; toggle.Content = "MOSTRAR OVERLAY"; return; }
        window = new OverlayWindow { Corner = settings.OverlayCorner }; window.SetScale(settings.OverlayScale);
        window.Closed += (_, _) => { window = null; toggle.Content = "MOSTRAR OVERLAY"; };
        window.Show(); toggle.Content = "OCULTAR OVERLAY"; _ = TickAsync();
    }

    public async Task TickAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        if (tick++ % 2 == 0) { try { gpu = await services.Gpu(lifetime); } catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { gpu = null; } }
        var frames = services.Frames(DateTimeOffset.UtcNow);
        Lines = OverlayLines.Build(show, usage.Last, gpu, frames, null, DateTimeOffset.Now);
        preview.Text = Lines.Count == 0 ? "Nenhum item selecionado." : string.Join("\n", Lines.Select(l => (l.Label.Length > 0 ? l.Label.PadRight(5) : "") + l.Value));
        if (window is { IsVisible: true } w)
        {
            w.Corner = settings.OverlayCorner; w.SetScale(settings.OverlayScale);
            w.Update(Lines, frames?.RecentFrametimesMs ?? [], show.Contains("graph"));
        }
    }

    private void Shutdown() { timer.Stop(); window?.Close(); window = null; }
}
