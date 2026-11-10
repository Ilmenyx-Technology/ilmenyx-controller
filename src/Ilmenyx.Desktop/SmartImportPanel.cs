// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class SmartImportPanel : Border
{
    public SmartImportPanel(SmartImportViewModel model, Func<string, Task<string?>> chooseDestination, CancellationToken lifetime)
    {
        DataContext = model;
        Background = Palette.Card; BorderBrush = Palette.Line; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius); Padding = new Thickness(20, 16); Margin = new Thickness(0, 0, 0, 12);
        var deviceLabel = new TextBlock { Text = L.T("Disco declarado no relatório (ex.: nvme0n1 ou sda)"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted, Margin = new Thickness(0, 4, 0, -4) };
        var device = new TextBox { Name = "SmartImportDevice", PlaceholderText = "nvme0n1" };
        AutomationProperties.SetName(device, L.T("Disco declarado no relatório SMART"));
        AutomationProperties.SetLabeledBy(device, deviceLabel);
        var pathLabel = new TextBlock { Text = L.T("Caminho completo do relatório JSON do smartctl"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted, Margin = new Thickness(0, 4, 0, -4) };
        var path = new TextBox { Name = "SmartImportPath", PlaceholderText = "/pasta/smartctl.json" };
        AutomationProperties.SetName(path, L.T("Caminho completo do relatório SMART JSON"));
        AutomationProperties.SetLabeledBy(path, pathLabel);
        var import = UiKit.Primary("ImportSmart", L.T("Importar relatório")); import.Margin = new Thickness(0, 0, 8, 8);
        var export = new Button { Name = "ExportSmartImport", Content = L.T("Exportar SMART importado JSON"), Margin = new Thickness(0, 0, 8, 8) };
        var exportHtml = new Button { Name = "ExportSmartImportHtml", Content = L.T("Exportar SMART importado HTML"), Margin = new Thickness(0, 0, 8, 8) };
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
        var status = new TextBlock { Name = "SmartImportStatus", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)));
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var values = new TextBlock { Name = "SmartImportValues", TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = IlmenyxTheme.Mono };
        values.Bind(TextBlock.TextProperty, new Binding(nameof(model.Text)));
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = L.T("SMART · IMPORTAÇÃO OFFLINE"), TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L.T("Lê somente o arquivo JSON que você indicar (até 64 KiB, arquivo regular). Não executa smartctl nem acessa o disco. A data original da coleta não é confirmada e o relato não garante saúde atual."), TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12 },
                deviceLabel, device, pathLabel, path,
                new WrapPanel { Children = { import, export, exportHtml } }, status,
                new ScrollViewer { MaxHeight = 200, Content = values }
            }
        };
    }
}
