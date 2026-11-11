// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class SensorPanel : Border
{
    public SensorPanel(SensorMonitorViewModel model, Func<string, Task<string?>> chooseDestination, CancellationToken lifetime)
    {
        DataContext = model;
        Background = Brushes.Transparent; Padding = new Thickness(0, 0, 10, 16); Margin = new Thickness(0, 4, 0, 0);
        var refresh = UiKit.Primary("RefreshSensors", L.T("Atualizar sensores")); refresh.Margin = new Thickness(0, 0, 8, 8);
        var monitor = new Button { Name = "MonitorSensors", Content = L.T("Monitorar (2 s)"), Margin = new Thickness(0, 0, 8, 8) };
        var stop = new Button { Name = "StopSensors", Content = L.T("Parar monitor"), Margin = new Thickness(0, 0, 8, 8) };
        var export = new Button { Name = "ExportSensors", Content = L.T("Exportar sensores JSON"), Margin = new Thickness(0, 0, 8, 8) };
        var exportHtml = new Button { Name = "ExportSensorsHtml", Content = L.T("Exportar sensores HTML"), Margin = new Thickness(0, 0, 8, 8) };
        refresh.Bind(IsEnabledProperty, new Binding(nameof(model.CanRefresh)));
        monitor.Bind(IsEnabledProperty, new Binding(nameof(model.CanPoll)));
        stop.Bind(IsEnabledProperty, new Binding(nameof(model.IsPolling)));
        export.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        exportHtml.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        refresh.Click += async (_, _) => await model.RefreshAsync(lifetime);
        monitor.Click += (_, _) => model.StartPolling(lifetime);
        stop.Click += async (_, _) => await model.StopPollingAsync();
        export.Click += async (_, _) => await Export(false);
        exportHtml.Click += async (_, _) => await Export(true);
        async Task Export(bool html)
        {
            try { await model.ExportAsync(html, chooseDestination, lifetime); }
            finally
            {
                var button = html ? exportHtml : export;
                if (IsEffectivelyVisible && button.IsEffectivelyEnabled && !lifetime.IsCancellationRequested) button.Focus();
            }
        }
        var status = new TextBlock { Name = "SensorStatus", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted, Margin = new Thickness(2, 0, 0, 0) };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)));
        var values = new TextBlock { Name = "SensorValues", TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = IlmenyxTheme.Mono, Foreground = Palette.Muted };
        values.Bind(TextBlock.TextProperty, new Binding(nameof(model.Text)));
        // Readable table built from the same snapshot; the raw text stays available (and identical to before) behind a toggle.
        var table = new StackPanel { Name = "SensorTable" };
        var raw = new Border { Name = "SensorRaw", IsVisible = false, Margin = new Thickness(0, 8, 0, 0), Child = values }; // direct child: stays in the visual tree while hidden
        var toggleRaw = new Button { Content = L.T("Mostrar texto bruto"), FontSize = 12, Padding = new Thickness(12, 5), Background = Brushes.Transparent, Foreground = Palette.Muted };
        toggleRaw.Click += (_, _) => { raw.IsVisible = !raw.IsVisible; toggleRaw.Content = raw.IsVisible ? L.T("Ocultar texto bruto") : L.T("Mostrar texto bruto"); };
        var view = new FriendlySensorView(table);
        var technical = new ToggleSwitch { Name = "SensorTechNames", OnContent = L.T("Nomes técnicos"), OffContent = L.T("Nomes técnicos"), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(technical, L.T("Nomes técnicos"));
        technical.IsCheckedChanged += (_, _) => { view.Technical = technical.IsChecked == true; view.Show(model.Snapshot); };
        void Fill() => view.Show(model.Snapshot);
        model.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(model.Snapshot) or null) Avalonia.Threading.Dispatcher.UIThread.Post(Fill); };
        Fill();
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                UiKit.PageHeader(L.T("Sensores"), L.T("Temperaturas, ventoinhas e bateria do seu PC. A régua mostra se cada valor está normal para esse tipo de peça.")),
                new TextBlock { Text = L.T("Monitorar atualiza a cada 2 segundos, sem guardar histórico. Todas as leituras do kernel, com os nomes originais, ficam no texto bruto no fim da página."), Foreground = Palette.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                new WrapPanel { Children = { refresh, monitor, stop, export, exportHtml, technical } }, status, view.Banner,
                new StackPanel { Margin = new Thickness(0, 4, 0, 0), Children = { table, new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-12, 10, 0, 0), Children = { toggleRaw } }, raw } }
            }
        };
    }

}

/// <summary>
/// The friendly Sensors view: one card per part of the PC (SensorFriendly), plain names, a ruler for temperatures and the verdict. Built once
/// per set of sensors; each new reading (every 2 s while monitoring) only changes texts and markers.
/// </summary>
internal sealed class FriendlySensorView(StackPanel host)
{
    public bool Technical { get; set; }
    public Border Banner { get; } = new() { Name = "SensorOverall", IsVisible = false, Background = Palette.Card, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(16, 10) };
    private readonly TextBlock bannerTag = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock bannerText = new() { FontSize = 13, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private string? layout;
    private readonly List<Action<IReadOnlyList<FriendlyCard>>> updates = [];
    private double width;

    private static IBrush LevelBrush(SensorLevel l) => l switch { SensorLevel.Critical => Palette.Error, SensorLevel.Hot => Palette.Warn, _ => Palette.Success };

    public void Show(SensorSnapshot? snapshot)
    {
        if (Banner.Child is null)
        {
            var tagBox = new Border { CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(10, 3), Background = Palette.Hover, Child = bannerTag, VerticalAlignment = VerticalAlignment.Center };
            var bannerGrid = new Grid { ColumnDefinitions = new("Auto,*") };
            bannerText.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(bannerText, 1);
            bannerGrid.Children.Add(tagBox); bannerGrid.Children.Add(bannerText);
            Banner.Child = bannerGrid;
            host.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - width) > 60) { width = e.NewSize.Width; layout = null; } };
        }
        if (snapshot is null) { Reset(L.T("Sem leitura de sensores ainda. Use \"Atualizar sensores\".")); return; }
        var cards = SensorFriendly.Build(snapshot);
        if (cards.Count == 0) { Reset(snapshot.Reason ?? L.T("Nenhum sensor exposto pelo kernel.")); return; }
        var overall = SensorFriendly.Overall(cards);
        Banner.IsVisible = true;
        bannerTag.Text = overall switch { SensorLevel.Critical => L.T("! Peça muito quente"), SensorLevel.Hot => L.T("! Algo está quente"), _ => L.T("✓ Tudo normal") };
        bannerTag.Foreground = LevelBrush(overall);
        bannerText.Text = overall switch
        {
            SensorLevel.Critical => L.T("Uma peça passou do limite seguro e já está reduzindo a velocidade. Veja o cartão em vermelho."),
            SensorLevel.Hot => L.T("Uma peça está quente. Normal durante jogos ou carga pesada; veja o cartão em âmbar."),
            _ => L.T("Nenhuma peça está quente."),
        };
        int hidden = snapshot.Readings.Count(r => r.Reading.Status != "available" || r.Reading.Value is null);
        string key = string.Join("|", cards.SelectMany(c => c.Readings.Select(r => c.Group + r.Name))) + Technical + width.ToString("0") + hidden;
        if (key == layout) { foreach (var u in updates) u(cards); return; }
        layout = key; updates.Clear(); host.Children.Clear();
        var built = cards.Select((c, i) => Card(c, i)).ToList();
        host.Children.Add(HealthCardView.Columns(built, 760, 12, host.Bounds.Width, c => ((StackPanel)((Border)c).Child!).Children.Count));
        foreach (var u in updates) u(cards);
        if (hidden > 0)
            host.Children.Add(new TextBlock { Name = "SensorHiddenCount", Text = L.T($"{hidden} sensores sem leitura não aparecem (o kernel os lista, mas não informa valor). Eles estão no texto bruto."), FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 0, 0) });
    }

    private void Reset(string message)
    {
        Banner.IsVisible = false; layout = null; updates.Clear(); host.Children.Clear();
        host.Children.Add(new TextBlock { Text = message, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
    }

    private static (string Icon, IBrush Tint) Look(SensorGroup g) => g switch
    {
        SensorGroup.Cpu => (Icons.Cpu, Palette.DataCpu), SensorGroup.Gpu => (Icons.Gpu, Palette.DataGpu), SensorGroup.Storage => (Icons.Disks, Palette.DataDisk),
        SensorGroup.Battery => (Icons.Energy, Palette.Energy), SensorGroup.Fans => (Icons.Sensors, Palette.DataNetwork), _ => (Icons.System, Palette.DataTemperature),
    };

    private Border Card(FriendlyCard card, int index)
    {
        var (icon, tint) = Look(card.Group);
        var iconTile = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Palette.Hover, Child = UiKit.Icon(icon, 17, tint), VerticalAlignment = VerticalAlignment.Center };
        ((Avalonia.Controls.Shapes.Path)iconTile.Child).HorizontalAlignment = HorizontalAlignment.Center; ((Avalonia.Controls.Shapes.Path)iconTile.Child).VerticalAlignment = VerticalAlignment.Center;
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = card.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap } } };
        if (card.Subtitle.Length > 0) title.Children.Add(new TextBlock { Text = card.Subtitle, FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        var headGrid = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new Thickness(0, 0, 0, 6) };
        title.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(title, 1); headGrid.Children.Add(iconTile); headGrid.Children.Add(title);
        var body = new StackPanel { Spacing = 2, Children = { headGrid } };
        // CPU cores fold into one line: "72 · 79 · 67 · 71 °C"; the package temperature keeps the ruler.
        var cores = card.Readings.Where(r => r.Group == SensorGroup.Cpu && r.Order is >= 10 and < 50).ToList();
        foreach (var r in card.Readings.Except(cores)) body.Children.Add(Reading(card.Group, r.Name));
        if (cores.Count > 0) body.Children.Add(Cores(cores.Count));
        var border = UiKit.Card(body, new Thickness(0)); border.Padding = new Thickness(18, 14); border.Name = "SensorCard_" + card.Group;
        return border;
    }

    private Control Reading(SensorGroup group, string name)
    {
        FriendlyReading? Find(IReadOnlyList<FriendlyCard> cards) => cards.FirstOrDefault(c => c.Group == group)?.Readings.FirstOrDefault(r => r.Name == name);
        var label = new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var labelLine = new WrapPanel { ItemSpacing = 6, Children = { label } };
        var value = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, FontFamily = IlmenyxTheme.Mono, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };
        var head = new Grid { ColumnDefinitions = new("*,Auto") }; Grid.SetColumn(value, 1); head.Children.Add(labelLine); head.Children.Add(value);
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { head } };
        var bar = new RangeBar { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        var ticks = new TextBlock { FontSize = 10.5, Foreground = Palette.Faint, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        var verdict = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 2, 0, 0) };
        var tech = new TextBlock { FontSize = 11, FontFamily = IlmenyxTheme.Mono, Foreground = Palette.Faint, IsVisible = Technical, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(bar); panel.Children.Add(ticks); panel.Children.Add(verdict); panel.Children.Add(tech);
        bool helpAdded = false;
        void Update(IReadOnlyList<FriendlyCard> cards)
        {
            if (Find(cards) is not { } r) return;
            value.Text = r.Value;
            tech.Text = r.Technical;
            if (!helpAdded && r.Help.Length > 0)
            {
                helpAdded = true;
                var help = new Border { MinWidth = 16, MinHeight = 16, Padding = new Thickness(5, 0), CornerRadius = new CornerRadius(999), Background = Palette.Hover, VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "?", FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = Palette.AccentText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                ToolTip.SetTip(help, r.Help); Avalonia.Automation.AutomationProperties.SetHelpText(label, r.Help);
                labelLine.Children.Add(help);
            }
            if (r.Range is { } range && r.Number is { } n)
            {
                bar.IsVisible = ticks.IsVisible = true; bar.Set(n, range);
                ticks.Text = L.T($"normal até {range.Hot:0} °C · quente {range.Hot:0}–{range.Critical:0} · acima de {range.Critical:0} reduz a velocidade");
            }
            verdict.IsVisible = r.Verdict.Length > 0; verdict.Text = r.Verdict;
            verdict.Foreground = r.Level is SensorLevel.None ? Palette.Muted : LevelBrush(r.Level);
            if (r.Level == SensorLevel.None && r.Help.Length > 0 && r.Range is null && r.Verdict.Length == 0 && r.Group == SensorGroup.Fans)
            { verdict.IsVisible = true; verdict.Text = r.Help; verdict.Foreground = Palette.Muted; }
        }
        updates.Add(Update);
        return panel;
    }

    private Control Cores(int count)
    {
        var label = new TextBlock { Text = L.T($"Núcleos ({count})"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var value = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, FontFamily = IlmenyxTheme.Mono, HorizontalAlignment = HorizontalAlignment.Right };
        var detail = new TextBlock { FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap };
        var head = new Grid { ColumnDefinitions = new("*,Auto") }; Grid.SetColumn(value, 1); head.Children.Add(label); head.Children.Add(value);
        updates.Add(cards =>
        {
            var cores = cards.FirstOrDefault(c => c.Group == SensorGroup.Cpu)?.Readings.Where(r => r.Order is >= 10 and < 50 && r.Number is not null).ToList() ?? [];
            if (cores.Count == 0) return;
            var hot = cores.MaxBy(r => r.Number)!;
            value.Text = hot.Value; value.Foreground = hot.Level is SensorLevel.Hot or SensorLevel.Critical ? LevelBrush(hot.Level) : Palette.Fg;
            detail.Text = L.T("mais quente: ") + hot.Name + " · " + string.Join(" · ", cores.Select(c => c.Value.Replace(" °C", "", StringComparison.Ordinal))) + " °C";
        });
        return new StackPanel { Margin = new Thickness(0, 10, 0, 0), Children = { head, detail } };
    }
}

/// <summary>Temperature ruler: green, amber and red zones of the part's range, with a marker at the current value.</summary>
internal sealed class RangeBar : Control
{
    private double value; private SensorRange range = SensorFriendly.CpuRange;
    public RangeBar() { Height = 12; HorizontalAlignment = HorizontalAlignment.Stretch; }
    public void Set(double v, SensorRange r) { if (v == value && r == range) return; value = v; range = r; InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, y = 3, h = 6;
        if (w <= 0) return;
        double hot = w * range.Hot / range.Max, crit = w * range.Critical / range.Max;
        static IBrush Soft(IBrush b) => new SolidColorBrush(((ISolidColorBrush)b).Color, 0.45);
        context.DrawRectangle(Soft(Palette.Success), null, new Rect(0, y, hot, h), 3, 3);
        context.DrawRectangle(Soft(Palette.Warn), null, new Rect(hot, y, crit - hot, h));
        context.DrawRectangle(Soft(Palette.Error), null, new Rect(crit, y, w - crit, h), 3, 3);
        double x = Math.Clamp(w * value / range.Max, 2, w - 2);
        context.DrawRectangle(Palette.Fg, new Pen(Palette.Bg, 2), new Rect(x - 2, 0, 4, Height), 2, 2);
    }
}
