using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

public sealed class MemoryValidationPanel : Border
{
    public MemoryValidationPanel(MemoryValidationViewModel model, Func<string, Task<string?>> chooseDestination, CancellationToken lifetime)
    {
        DataContext = model; Background = Brush.Parse("#24252E"); CornerRadius = new CornerRadius(12);
        Padding = new Thickness(20); Margin = new Thickness(0, 0, 12, 0);
        var start = new Button { Name = "ValidateMemory", Content = "Validar memória (reduzido)", Margin = new Thickness(0, 0, 8, 8) };
        var cancel = new Button { Name = "CancelMemory", Content = "Cancelar memória", Margin = new Thickness(0, 0, 8, 8) };
        var export = new Button { Name = "ExportMemory", Content = "Exportar memória JSON", Margin = new Thickness(0, 0, 8, 8) };
        var exportHtml = new Button { Name = "ExportMemoryHtml", Content = "Exportar memória HTML", Margin = new Thickness(0, 0, 8, 8) };
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
        var status = new TextBlock { Name = "MemoryStatus", TextWrapping = TextWrapping.Wrap }; status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)));
        var summary = new TextBlock { Name = "MemorySummary", TextWrapping = TextWrapping.Wrap, FontSize = 12 }; summary.Bind(TextBlock.TextProperty, new Binding(nameof(model.Summary)));
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
        {
            new TextBlock { Text = "VALIDAÇÃO DE ALGORITMOS DE MEMÓRIA", FontSize = 14, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Cadeia de ponteiros, soma e cópia · buffers de 256 KiB · uma passada · 1 worker.\nNão mede latência, GB/s, estabilidade ou saúde da RAM. Não executa GameSim ou testes de disco.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap },
            new WrapPanel { Children = { start, cancel, export, exportHtml } }, status, summary
        }
        };
    }
}
