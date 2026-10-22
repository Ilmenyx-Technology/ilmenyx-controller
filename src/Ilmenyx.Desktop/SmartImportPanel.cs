// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

public sealed class SmartImportPanel : Border
{
    public SmartImportPanel(SmartImportViewModel model, Func<string, Task<string?>> chooseDestination, CancellationToken lifetime)
    {
        DataContext = model;
        Background = Brush.Parse("#2A2531"); CornerRadius = new CornerRadius(12); Padding = new Thickness(20); Margin = new Thickness(0, 0, 12, 0);
        var deviceLabel = new TextBlock { Text = "Disco declarado no relatório (ex.: nvme0n1 ou sda)", TextWrapping = TextWrapping.Wrap };
        var device = new TextBox { Name = "SmartImportDevice", PlaceholderText = "nvme0n1" };
        AutomationProperties.SetName(device, "Disco declarado no relatório SMART");
        AutomationProperties.SetLabeledBy(device, deviceLabel);
        var pathLabel = new TextBlock { Text = "Caminho completo do relatório JSON do smartctl", TextWrapping = TextWrapping.Wrap };
        var path = new TextBox { Name = "SmartImportPath", PlaceholderText = "/pasta/smartctl.json" };
        AutomationProperties.SetName(path, "Caminho completo do relatório SMART JSON");
        AutomationProperties.SetLabeledBy(path, pathLabel);
        var import = new Button { Name = "ImportSmart", Content = "Importar relatório", Margin = new Thickness(0, 0, 8, 8) };
        var export = new Button { Name = "ExportSmartImport", Content = "Exportar SMART importado JSON", Margin = new Thickness(0, 0, 8, 8) };
        var exportHtml = new Button { Name = "ExportSmartImportHtml", Content = "Exportar SMART importado HTML", Margin = new Thickness(0, 0, 8, 8) };
        import.Bind(IsEnabledProperty, new Binding(nameof(model.CanImport)));
        export.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        exportHtml.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        import.Click += async (_, _) =>
        {
            try { await model.ImportAsync(device.Text ?? "", path.Text ?? "", lifetime); }
            finally { if (IsEffectivelyVisible && import.IsEffectivelyEnabled && !lifetime.IsCancellationRequested) import.Focus(); }
        };
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
        var status = new TextBlock { Name = "SmartImportStatus", TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)));
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var values = new TextBlock { Name = "SmartImportValues", TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        values.Bind(TextBlock.TextProperty, new Binding(nameof(model.Text)));
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "SMART · IMPORTAÇÃO OFFLINE", TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Lê somente o arquivo JSON que você indicar (até 64 KiB, arquivo regular). Não executa smartctl nem acessa o disco. A data original da coleta não é confirmada e o relato não garante saúde atual.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                deviceLabel, device, pathLabel, path,
                new WrapPanel { Children = { import, export, exportHtml } }, status,
                new ScrollViewer { MaxHeight = 200, Content = values }
            }
        };
    }
}
