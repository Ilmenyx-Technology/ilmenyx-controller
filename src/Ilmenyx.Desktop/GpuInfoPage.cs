// New Ilmenyx additions (layout follows the original GPU page: chips, highlight card, GPU-Z style sections, live sensors): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
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
    private readonly TextBlock name = new() { Name = "GpuName", FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock subtitle = new() { Name = "GpuSubtitle", Foreground = Palette.Muted, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock vendor = new() { Name = "GpuVendor", FontSize = 30, FontWeight = FontWeight.Black, Foreground = Palette.Faint, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel sections = new() { Name = "GpuSections" };
    private readonly StackPanel extras = new() { Name = "GpuExtras" };
    private readonly StackPanel live = new() { Name = "GpuLive" };
    private readonly TextBlock status = new() { Name = "GpuStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<(GpuRaw Raw, GpuCard Card)> gpus = [];
    private int selected;
    private CancellationTokenSource? poll;
    // Responsive widths: at the window minimum (900 px) the fixed 330 + 200 columns left ~70 px for values (seen in a 900 px Wayland window, 2026-11-06).
    public const double NarrowBodyWidth = 760;
    private readonly ColumnDefinition sideColumn = new(330, GridUnitType.Pixel);
    private readonly List<ColumnDefinition> labelColumns = [];
    private double labelWidth = 200;
    public IReadOnlyList<GpuCard> Cards => gpus.Select(g => g.Card).ToList();

    public GpuInfoPage(CancellationToken lifetime)
    {
        this.lifetime = lifetime; RowDefinitions = new("Auto,Auto,*");
        var header = new Grid { Margin = new Thickness(0, 4, 0, 18) };
        header.Children.Add(new StackPanel { Children = { new TextBlock { Text = "Placa de vídeo", FontSize = 32, FontWeight = FontWeight.Bold }, new TextBlock { Text = "Especificações completas e sensores em tempo real", Foreground = Palette.Muted } } });
        header.Children.Add(chips);
        var highlight = new Border
        {
            Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(24, 20), Margin = new Thickness(0, 0, 0, 14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new Grid { Children = { new StackPanel { Children = { name, subtitle } }, vendor } },
                    new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(10, 6), Child = new TextBlock { Name = "GpuValidationNotice", Text = ValidationNotices.AmdNvidia, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap, FontSize = 13 } },
                    status
                }
            }
        };
        Grid.SetRow(highlight, 1);
        var body = new Grid { Name = "GpuBody", ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(16, GridUnitType.Pixel), sideColumn } };
        body.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        body.Children.Add(new ScrollViewer { Name = "GpuSectionsScroll", Content = sections, Padding = new Thickness(0, 0, 6, 0) });
        var side = new Border
        {
            Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(20, 16, 12, 12),
            Child = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(0, 0, 10, 0), Children = { new TextBlock { Text = "SENSORES AO VIVO", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, Margin = new Thickness(0, 0, 0, 6) }, extras, live } } }
        };
        Grid.SetColumn(side, 2); body.Children.Add(side);
        Grid.SetRow(body, 2);
        Children.Add(header); Children.Add(highlight); Children.Add(body);
        Loaded += async (_, _) => { if (IsVisible) StartPolling(); if (gpus.Count == 0) await LoadAsync(); };
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
                var chip = new Button { Name = "GpuChip" + i, Content = gpus[i].Card.Name, FontSize = 11, Padding = new Thickness(14, 5), CornerRadius = new CornerRadius(16), Margin = new Thickness(6, 0, 0, 0) };
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
        for (int i = 0; i < chips.Children.Count; i++) if (chips.Children[i] is Button b) { b.Background = i == index ? Palette.Fg : Brushes.Transparent; b.Foreground = i == index ? Palette.OnFg : Palette.Muted; }
        sections.Children.Clear(); labelColumns.Clear();
        foreach (var section in card.Sections)
        {
            var rows = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var row in section.Children)
            {
                var labelColumn = new ColumnDefinition(labelWidth, GridUnitType.Pixel); labelColumns.Add(labelColumn);
                var g = new Grid { ColumnDefinitions = { labelColumn, new ColumnDefinition(1, GridUnitType.Star) } };
                g.Children.Add(new TextBlock { Text = row.Header, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 10, 5) });
                var value = new TextBlock { Text = row.Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5) }; Grid.SetColumn(value, 1); g.Children.Add(value);
                rows.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = g });
            }
            sections.Children.Add(new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(22, 16, 22, 10), Margin = new Thickness(0, 0, 0, 14), Child = new StackPanel { Children = { new TextBlock { Text = section.Header, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint }, rows } } });
        }
        extras.Children.Clear(); live.Children.Clear();
    }

    /// <summary>Narrow body: thinner sensor column and label column, so section values keep a readable width.</summary>
    private void Fit(double bodyWidth)
    {
        bool narrow = bodyWidth < NarrowBodyWidth;
        sideColumn.Width = new GridLength(narrow ? 250 : 330);
        labelWidth = narrow ? 130 : 200;
        foreach (var c in labelColumns) c.Width = new GridLength(labelWidth);
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
        extras.Children.Clear(); live.Children.Clear();
        foreach (var (k, v) in GpuLive.Extras(t))
        {
            var g = new Grid { Margin = new Thickness(0, 5), ColumnDefinitions = new("*,Auto") }; g.Children.Add(new TextBlock { Text = k, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
            var value = new TextBlock { Text = v, FontFamily = new FontFamily("monospace"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(value, 1); g.Children.Add(value); extras.Children.Add(g);
        }
        var rows = GpuLive.Rows(t);
        if (rows.Count == 0 && GpuLive.Extras(t).Count == 0)
            live.Children.Add(new TextBlock { Name = "GpuLiveEmpty", Text = "Esta GPU não expõe contadores sem privilégios (típico de iGPU Intel; NVIDIA exige nvidia-smi, AMD usa sysfs).", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        foreach (var r in rows)
        {
            var label = new TextBlock(); label.Inlines!.Add(new Avalonia.Controls.Documents.Run(r.Category) { Foreground = Palette.Faint }); label.Inlines.Add(new Avalonia.Controls.Documents.Run(r.Name) { Foreground = Palette.Muted });
            // Real columns: a long label ("VRAM reservada · Ocupação") wraps instead of running under the value.
            label.TextWrapping = TextWrapping.Wrap;
            var g = new Grid { ColumnDefinitions = new("*,Auto") }; g.Children.Add(label);
            var value = new TextBlock { Text = r.Value, FontFamily = new FontFamily("monospace"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(value, 1); g.Children.Add(value);
            live.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6), Child = g });
        }
    }
}
