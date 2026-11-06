// New Ilmenyx additions (layout follows the original GPU page: chips, highlight card, GPU-Z style sections, live sensors; cinematic hero and live tiles 2026-11-06): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class GpuInfoPage : Grid
{
    private readonly LinuxGpuCollector collector = new();
    private readonly PciIds ids = PciIds.Load();
    private readonly CancellationToken lifetime;
    private readonly WrapPanel chips = new() { Name = "GpuChips", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock name = new() { Name = "GpuName", FontSize = 26, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock subtitle = new() { Name = "GpuSubtitle", FontSize = 13, Foreground = Palette.Muted, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock vendor = new() { Name = "GpuVendor", FontSize = 38, FontWeight = FontWeight.Black, Foreground = Palette.Fg, Opacity = 0.18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, -4, 0, 0) };
    private readonly WrapPanel tags = new() { Name = "GpuTags", Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel sections = new() { Name = "GpuSections" };
    private readonly StackPanel extras = new() { Name = "GpuExtras" };
    private readonly StackPanel live = new() { Name = "GpuLive" };
    private readonly TextBlock status = new() { Name = "GpuStatus", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<(GpuRaw Raw, GpuCard Card)> gpus = [];
    private int selected;
    private CancellationTokenSource? poll;
    // Responsive widths: at the window minimum (900 px) the content area is ~640 px wide (seen in a 900 px Wayland window, 2026-11-06).
    // Wide: section rows in two columns; narrow: one column with a thinner label, so values keep a readable width.
    public const double NarrowBodyWidth = 760;
    private readonly List<ColumnDefinition> labelColumns = [];
    private readonly List<UniformGrid> rowGrids = [];
    private readonly UniformGrid liveTiles = new() { Name = "GpuLiveTiles", Columns = 4, Margin = new Thickness(0, 0, -10, 0) };
    private double labelWidth = 170;
    private int rowColumns = 2;
    public IReadOnlyList<GpuCard> Cards => gpus.Select(g => g.Card).ToList();

    public GpuInfoPage(CancellationToken lifetime)
    {
        this.lifetime = lifetime;
        var notice = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(10, 5), Margin = new Thickness(0, 6, 0, 8),
            Child = new TextBlock { Name = "GpuValidationNotice", Text = ValidationNotices.AmdNvidia, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap, FontSize = 12 }
        };
        var heroText = new StackPanel { Children = { UiKit.Label("ADAPTADOR GRÁFICO"), name, subtitle, tags, notice, status } };
        heroText.Children[0].Margin = new Thickness(0, 0, 0, 6);
        var heroGrid = new Grid { ColumnDefinitions = new("*,Auto"), Children = { heroText } };
        Grid.SetColumn(vendor, 1); heroGrid.Children.Add(vendor);
        var hero = UiKit.Hero(heroGrid, name: "GpuHero"); hero.Height = double.NaN; hero.MinHeight = 190; FollowTheme(hero);

        var page = new StackPanel { Name = "GpuBody", Margin = new Thickness(0, 0, 10, 16) };
        page.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        page.Children.Add(UiKit.PageHeader("Placa de vídeo", "Especificações completas e sensores em tempo real", chips));
        page.Children.Add(hero);
        page.Children.Add(UiKit.SectionHeader("Sensores ao vivo", new TextBlock { Text = "atualiza a cada 2 s", FontSize = 11.5, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center }));
        page.Children.Add(live); page.Children.Add(extras);
        page.Children.Add(UiKit.SectionHeader("Especificações"));
        page.Children.Add(sections);
        Children.Add(new ScrollViewer { Name = "GpuSectionsScroll", Content = page });
        Loaded += async (_, _) => { if (IsVisible) StartPolling(); if (gpus.Count == 0) await LoadAsync(); };
    }

    /// <summary>The hero fill is built once; rebuild it on a theme switch so text stays legible in light and dark.</summary>
    private static void FollowTheme(Border hero)
    {
        hero.ActualThemeVariantChanged += (_, _) =>
        {
            hero.Background = UiKit.HeroBrush();
            if (hero.Child is Grid { Children: [Border overlay, ..] }) overlay.Opacity = Palette.IsLight ? 0 : 1;
        };
    }

    public async Task LoadAsync()
    {
        status.Text = "Lendo as GPUs (sysfs e, se houver, nvidia-smi — somente leitura)…";
        try
        {
            var smi = await LinuxGpuCollector.QuerySmiAsync(lifetime);
            gpus = await Task.Run(() => collector.Collect(ids, smi), lifetime);
            chips.Children.Clear();
            for (int i = 0; i < gpus.Count; i++)
            {
                int index = i;
                var chip = new Button { Name = "GpuChip" + i, Content = gpus[i].Card.Name, FontSize = 11, Padding = new Thickness(13, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderBrush = Palette.Line, BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 0, 6) };
                chip.Click += (_, _) => Show(index);
                chips.Children.Add(chip);
            }
            if (gpus.Count == 0) { name.Text = "Nenhuma GPU PCI encontrada"; subtitle.Text = ""; vendor.Text = ""; status.Text = "O inventário DRM/PCI não listou GPUs."; return; }
            Show(Math.Min(selected, gpus.Count - 1));
            status.Text = $"{gpus.Count} GPU(s) · lido às {DateTime.Now:HH:mm:ss}. Contadores AMD/NVIDIA: veja o aviso acima.";
        }
        catch (OperationCanceledException) { status.Text = "Leitura cancelada."; }
    }

    public void Show(int index)
    {
        if (index < 0 || index >= gpus.Count) return;
        selected = index; var card = gpus[index].Card;
        name.Text = card.Name; subtitle.Text = card.Subtitle; vendor.Text = card.Vendor.ToUpperInvariant();
        for (int i = 0; i < chips.Children.Count; i++) if (chips.Children[i] is Button b) UiKit.ChipStyle(b, i == index);
        tags.Children.Clear();
        foreach (var tag in HeroTags(card)) tags.Children.Add(UiKit.Tag(tag));
        tags.IsVisible = tags.Children.Count > 0;
        sections.Children.Clear(); labelColumns.Clear(); rowGrids.Clear();
        foreach (var section in card.Sections)
        {
            var rows = new UniformGrid { Columns = rowColumns, Margin = new Thickness(0, 8, 0, 0) }; rowGrids.Add(rows);
            foreach (var row in section.Children)
            {
                var labelColumn = new ColumnDefinition(labelWidth, GridUnitType.Pixel); labelColumns.Add(labelColumn);
                var g = new Grid { ColumnDefinitions = { labelColumn, new ColumnDefinition(1, GridUnitType.Star) } };
                g.Children.Add(new TextBlock { Text = row.Header, Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 10, 6) });
                var value = new TextBlock { Text = row.Value, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6) }; Grid.SetColumn(value, 1); g.Children.Add(value);
                rows.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 0, 22, 0), Child = g });
            }
            sections.Children.Add(UiKit.Card(new StackPanel { Children = { UiKit.Label(section.Header), rows } }));
        }
        extras.Children.Clear(); live.Children.Clear();
    }

    /// <summary>Short metadata capsules for the hero: architecture, process, memory, driver (only values the card really has).</summary>
    private static IEnumerable<string> HeroTags(GpuCard card)
    {
        string? Value(string section, string header)
        {
            var v = card.Sections.FirstOrDefault(s => s.Header == section)?.Children.FirstOrDefault(r => r.Header == header)?.Value;
            if (string.IsNullOrWhiteSpace(v) || v == "—") return null;
            int cut = v.IndexOf(" (", StringComparison.Ordinal); if (cut > 0) v = v[..cut];
            return v.Length <= 34 ? v : null;
        }
        var memory = card.Sections.FirstOrDefault(s => s.Header == "MEMÓRIA")?.Children.FirstOrDefault()?.Header is "Tamanho" ? Value("MEMÓRIA", "Tamanho") : null;
        string? driver = Value("DRIVER E BIOS", "Driver do kernel") is { } d && d != "sem driver" ? "Driver " + d + (Value("DRIVER E BIOS", "Versão do driver") is { } ver ? " " + ver : "") : null;
        foreach (var t in new[] { Value("IDENTIFICAÇÃO", "Arquitetura"), Value("IDENTIFICAÇÃO", "Processo"), memory is null ? null : "VRAM " + memory, Value("MEMÓRIA", "Tipo"), Value("BARRAMENTO", "Interface"), driver })
            if (t is not null) yield return t;
    }

    /// <summary>Narrow body: one column of rows and a thinner label column, so section values keep a readable width.</summary>
    private void Fit(double bodyWidth)
    {
        bool narrow = bodyWidth < NarrowBodyWidth;
        labelWidth = narrow ? 130 : 170; rowColumns = narrow ? 1 : 2;
        foreach (var c in labelColumns) c.Width = new GridLength(labelWidth);
        foreach (var g in rowGrids) g.Columns = rowColumns;
        liveTiles.Columns = Math.Clamp((int)((bodyWidth + 10) / 190), 2, 5);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;
        if (IsVisible) StartPolling(); else { poll?.Cancel(); poll = null; }
    }

    private void StartPolling()
    {
        if (poll is not null) return;
        poll = CancellationTokenSource.CreateLinkedTokenSource(lifetime); var token = poll.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                do
                {
                    if (gpus.Count > selected)
                    {
                        var t = await collector.TelemetryAsync(gpus[selected].Raw, token);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!token.IsCancellationRequested) ShowLive(t); });
                    }
                } while (await timer.WaitForNextTickAsync(token));
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void ShowLive(GpuTelemetry t)
    {
        extras.Children.Clear(); live.Children.Clear(); liveTiles.Children.Clear();
        var pairs = GpuLive.Extras(t);
        if (pairs.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            foreach (var (k, v) in pairs) wrap.Children.Add(Pair(k, v));
            extras.Children.Add(wrap);
        }
        var rows = GpuLive.Rows(t);
        if (rows.Count == 0 && pairs.Count == 0)
            live.Children.Add(new TextBlock { Name = "GpuLiveEmpty", Text = "Esta GPU não expõe contadores sem privilégios (típico de iGPU Intel; NVIDIA exige nvidia-smi, AMD usa sysfs).", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 10) });
        foreach (var r in rows)
        {
            string category = r.Category.TrimEnd(' ', '·');
            var value = new TextBlock { Text = r.Value, FontSize = 20, FontFamily = IlmenyxTheme.Mono, TextTrimming = TextTrimming.CharacterEllipsis };
            var tile = UiKit.StatTile(category.ToUpperInvariant(), value, new TextBlock { Text = r.Name }, TileIcon(category), TileTint(category));
            tile.Margin = new Thickness(0, 0, 10, 10); tile.Padding = new Thickness(16, 12);
            liveTiles.Children.Add(tile);
        }
        if (liveTiles.Children.Count > 0) live.Children.Add(liveTiles);
    }

    private static string TileIcon(string category) => category switch
    {
        "Temperatura" => Icons.Temperature, "Carga" => Icons.Gpu, "Clock" => Icons.Fps, "Tensão" or "Energia" => Icons.Oc,
        _ when category.StartsWith("VRAM", StringComparison.Ordinal) => Icons.Memory, "Ventoinha" => Icons.Sensors, _ => Icons.Gpu,
    };

    private static IBrush TileTint(string category) => category switch
    {
        "Temperatura" => Palette.DataTemperature, "Carga" => Palette.DataGpu, "Clock" => Palette.DataCpu, "Tensão" or "Energia" => Palette.Energy,
        _ when category.StartsWith("VRAM", StringComparison.Ordinal) => Palette.DataMemory, _ => Palette.Faint,
    };

    /// <summary>Compact key/value capsule for secondary live readings (PCIe link, perf state, limiter).</summary>
    private static Border Pair(string key, string value)
    {
        var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        text.Inlines!.Add(new Avalonia.Controls.Documents.Run(key + "  ") { Foreground = Palette.Muted });
        text.Inlines.Add(new Avalonia.Controls.Documents.Run(value) { Foreground = Palette.Fg, FontFamily = IlmenyxTheme.Mono });
        return new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(12, 5), Margin = new Thickness(0, 0, 8, 8), Child = text };
    }
}
