using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class SensorPanel : Border
{
    public SensorPanel(SensorMonitorViewModel model, Func<Task<string?>> chooseDestination, CancellationToken lifetime)
    {
        DataContext = model;
        Background = Brush.Parse("#24252E"); CornerRadius = new CornerRadius(12); Padding = new Thickness(20); Margin = new Thickness(0, 0, 12, 0);
        var refresh = new Button { Name = "RefreshSensors", Content = "Atualizar sensores", Margin = new Thickness(0, 0, 8, 8) };
        var monitor = new Button { Name = "MonitorSensors", Content = "Monitorar (2 s)", Margin = new Thickness(0, 0, 8, 8) };
        var stop = new Button { Name = "StopSensors", Content = "Parar monitor", Margin = new Thickness(0, 0, 8, 8) };
        var export = new Button { Name = "ExportSensors", Content = "Exportar sensores JSON", Margin = new Thickness(0, 0, 8, 8) };
        refresh.Bind(IsEnabledProperty, new Binding(nameof(model.CanRefresh)));
        monitor.Bind(IsEnabledProperty, new Binding(nameof(model.CanPoll)));
        stop.Bind(IsEnabledProperty, new Binding(nameof(model.IsPolling)));
        export.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        refresh.Click += async (_, _) => await model.RefreshAsync(lifetime);
        monitor.Click += (_, _) => model.StartPolling(lifetime);
        stop.Click += async (_, _) => await model.StopPollingAsync();
        export.Click += async (_, _) =>
        {
            if (!model.CanExport || model.Snapshot is not { } selected) return;
            try
            {
                var path = await chooseDestination();
                if (path is null) return;
                await SnapshotReport.ExportAsync(selected, path, lifetime);
                model.SetStatus("Snapshot de sensores exportado para arquivo local.");
            }
            catch (OperationCanceledException) { model.SetStatus("Exportação cancelada."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { model.SetStatus("Não foi possível exportar: " + e.Message); }
            finally
            {
                if (IsEffectivelyVisible && export.IsEffectivelyEnabled && !lifetime.IsCancellationRequested) export.Focus();
            }
        };
        var status = new TextBlock { Name = "SensorStatus", TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)));
        var values = new TextBlock { Name = "SensorValues", TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        values.Bind(TextBlock.TextProperty, new Binding(nameof(model.Text)));
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "SENSORES · HWMON / THERMAL / ENERGIA", FontSize = 14, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Leituras brutas do kernel, sem inferência de saúde. Canais não identificados preservam o nome original.\nMonitor opcional: atualiza a cada 2 segundos, sem manter histórico. Tensão pode exigir calibração da placa.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                new WrapPanel { Children = { refresh, monitor, stop, export } }, status,
                new ScrollViewer { MaxHeight = 260, Content = values }
            }
        };
    }
}
