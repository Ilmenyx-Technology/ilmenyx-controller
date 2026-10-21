using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

public sealed class GameSimValidationPanel : Border
{
    public GameSimValidationPanel(GameSimValidationViewModel model, Func<string, Task<string?>> chooseDestination, CancellationToken lifetime)
    {
        DataContext = model; Background = Brush.Parse("#24252E"); CornerRadius = new CornerRadius(12);
        Padding = new Thickness(20); Margin = new Thickness(0, 0, 12, 0);
        var start = new Button { Name = "ValidateSim", Content = "Validar simulação (reduzida)", Margin = new Thickness(0, 0, 8, 8) };
        var cancel = new Button { Name = "CancelSim", Content = "Cancelar simulação", Margin = new Thickness(0, 0, 8, 8) };
        var export = new Button { Name = "ExportSim", Content = "Exportar simulação JSON", Margin = new Thickness(0, 0, 8, 8) };
        var exportHtml = new Button { Name = "ExportSimHtml", Content = "Exportar simulação HTML", Margin = new Thickness(0, 0, 8, 8) };
        start.Bind(IsEnabledProperty, new Binding(nameof(model.CanStart))); cancel.Bind(IsEnabledProperty, new Binding(nameof(model.CanCancel))); export.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        exportHtml.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        start.Click += async (_, _) => await model.StartAsync(lifetime);
        cancel.Click += (_, _) => model.Cancel();
        export.Click += async (_, _) => await Export(false);
        exportHtml.Click += async (_, _) => await Export(true);
        async Task Export(bool html)
        {
            if (!model.CanExport) return;
            var selected = model.Report;
            try
            {
                var path = await chooseDestination(html ? ".html" : ".json"); if (path is null) return;
                if (!ReferenceEquals(selected, model.Report)) { model.SetStatus("O resultado mudou. Selecione novamente o relatório."); return; }
                if (html) await model.ExportHtmlAsync(path, lifetime);
                else await model.ExportAsync(path, lifetime);
            }
            catch (OperationCanceledException) { model.SetStatus("Exportação cancelada."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { model.SetStatus("Não foi possível selecionar destino: " + e.Message); }
        }
        var status = new TextBlock { Name = "SimStatus", TextWrapping = TextWrapping.Wrap }; status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)));
        var summary = new TextBlock { Name = "SimSummary", TextWrapping = TextWrapping.Wrap, FontSize = 12 }; summary.Bind(TextBlock.TextProperty, new Binding(nameof(model.Summary)));
        var preview = new GameSimPreview { Name = "SimPreview", Width = 220, Height = 220 };
        preview.Bind(GameSimPreview.StateProperty, new Binding(nameof(model.State)));
        preview.Bind(IsVisibleProperty, new Binding(nameof(model.HasState)));
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
        {
            new TextBlock { Text = "VALIDAÇÃO REDUZIDA · GAMESIM", FontSize = 14, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "256 entidades · 16 passos fixos · 1 worker · sem aquecimento por tempo.\nSimulação sintética de física/IA. Não mede FPS de jogos, desempenho da GPU ou score.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap },
            new WrapPanel { Children = { start, cancel, export, exportHtml } }, preview, status, summary
        }
        };
    }
}
