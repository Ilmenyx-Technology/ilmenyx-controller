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
        var refresh = UiKit.Primary("RefreshSensors", "Atualizar sensores"); refresh.Margin = new Thickness(0, 0, 8, 8);
        var monitor = new Button { Name = "MonitorSensors", Content = "Monitorar (2 s)", Margin = new Thickness(0, 0, 8, 8) };
        var stop = new Button { Name = "StopSensors", Content = "Parar monitor", Margin = new Thickness(0, 0, 8, 8) };
        var export = new Button { Name = "ExportSensors", Content = "Exportar sensores JSON", Margin = new Thickness(0, 0, 8, 8) };
        var exportHtml = new Button { Name = "ExportSensorsHtml", Content = "Exportar sensores HTML", Margin = new Thickness(0, 0, 8, 8) };
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
        var toggleRaw = new Button { Content = "Mostrar texto bruto", FontSize = 12, Padding = new Thickness(12, 5), Background = Brushes.Transparent, Foreground = Palette.Muted };
        toggleRaw.Click += (_, _) => { raw.IsVisible = !raw.IsVisible; toggleRaw.Content = raw.IsVisible ? "Ocultar texto bruto" : "Mostrar texto bruto"; };
        void Fill() => FillTable(table, model.Snapshot);
        model.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(model.Snapshot) or null) Avalonia.Threading.Dispatcher.UIThread.Post(Fill); };
        Fill();
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                UiKit.PageHeader("Sensores", "Temperaturas, ventoinhas, tensões e energia lidas do kernel (hwmon, thermal, power_supply)"),
                new TextBlock { Text = "Leituras brutas do kernel, sem inferência de saúde. Canais não identificados preservam o nome original.\nMonitor opcional: atualiza a cada 2 segundos, sem manter histórico. Tensão pode exigir calibração da placa.", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, -8, 0, 4) },
                new WrapPanel { Children = { refresh, monitor, stop, export, exportHtml } }, status,
                UiKit.Card(new StackPanel { Children = { table, new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-12, 6, 0, 0), Children = { toggleRaw } }, raw } }, new Thickness(0, 4, 0, 0))
            }
        };
    }

    /// <summary>Groups the readings by chip/device: label on the left, value right-aligned; power supplies last. Clean rows, no heavy borders.</summary>
    private static void FillTable(StackPanel table, SensorSnapshot? snapshot)
    {
        table.Children.Clear();
        if (snapshot is null) { table.Children.Add(new TextBlock { Text = "Sem snapshot de sensores. Use \"Atualizar sensores\".", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap }); return; }
        if (snapshot.Readings.Count == 0 && snapshot.PowerSupplies is not { Count: > 0 })
        { table.Children.Add(new TextBlock { Text = snapshot.Reason ?? "Nenhum sensor exposto pelo kernel.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap }); return; }
        var groups = new List<Control>();
        foreach (var group in snapshot.Readings.GroupBy(r => (r.Chip, r.Device)))
        {
            var g = new StackPanel { Margin = new Thickness(0, 0, 0, 16), Children = { Heading(group.Key.Chip.ToUpperInvariant(), group.Key.Device) } };
            foreach (var r in group) g.Children.Add(Row(r.Label, SnapshotReport.Describe(r.Reading), r.Reading.Status == "available"));
            groups.Add(g);
        }
        if (snapshot.PowerSupplies is { Count: > 0 } supplies)
        {
            var g = new StackPanel { Margin = new Thickness(0, 0, 0, 16), Children = { Heading("FONTES DE ENERGIA", "power_supply") } };
            foreach (var p in supplies)
                g.Children.Add(Row(p.Device, $"{SnapshotReport.Describe(p.Type)} · {SnapshotReport.Describe(p.Status)} · presente: {SnapshotReport.Describe(p.Present)}", true));
            groups.Add(g);
        }
        // Two balanced columns of chip groups on wide windows, one column at the window minimum.
        table.Children.Add(HealthCardView.Columns(groups, 820, 32, table.Bounds.Width, c => ((StackPanel)c).Children.Count + 1.5));
    }

    private static Control Heading(string title, string source)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        text.Inlines!.Add(new Avalonia.Controls.Documents.Run(title) { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Faint });
        text.Inlines.Add(new Avalonia.Controls.Documents.Run("   " + source) { FontSize = 11, Foreground = Palette.Faint, FontFamily = IlmenyxTheme.Mono });
        return text;
    }

    private static Control Row(string label, string value, bool available)
    {
        var g = new Grid { ColumnDefinitions = new("3*,2*") };
        g.Children.Add(new TextBlock { Text = label, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 12, 6) });
        var v = new TextBlock { Text = value, FontSize = 13, FontFamily = IlmenyxTheme.Mono, Foreground = available ? Palette.Fg : Palette.Faint, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 6) };
        Grid.SetColumn(v, 1); g.Children.Add(v);
        return new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
    }
}
