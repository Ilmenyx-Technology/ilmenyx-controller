using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ilmenyx.Core.Benchmarks;

namespace Ilmenyx.Desktop;

public sealed class CpuValidationPanel : Border, IDisposable
{
    private readonly CpuValidationViewModel model;
    private readonly Image preview = new() { Name = "CpuPreview", Width = 256, Height = 144, Stretch = Stretch.Fill, IsVisible = false };
    private WriteableBitmap? bitmap;
    private CpuRenderOutput? displayed;

    public CpuValidationPanel(CpuValidationViewModel model, Func<string, Task<string?>> chooseDestination, CancellationToken lifetime)
    {
        this.model = model;
        DataContext = model;
        Background = Brush.Parse("#24252E");
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(20);
        Margin = new Thickness(0, 0, 12, 0);
        var start = new Button { Name = "ValidateCpu", Content = "Validar CPU (reduzido)", Margin = new Thickness(0, 0, 8, 8) };
        var cancel = new Button { Name = "CancelCpu", Content = "Cancelar CPU", Margin = new Thickness(0, 0, 8, 8) };
        var export = new Button { Name = "ExportCpu", Content = "Exportar CPU JSON", Margin = new Thickness(0, 0, 8, 8) };
        var exportHtml = new Button { Name = "ExportCpuHtml", Content = "Exportar CPU HTML", Margin = new Thickness(0, 0, 8, 8) };
        start.Bind(IsEnabledProperty, new Binding(nameof(model.CanStart)));
        cancel.Bind(IsEnabledProperty, new Binding(nameof(model.CanCancel)));
        export.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        exportHtml.Bind(IsEnabledProperty, new Binding(nameof(model.CanExport)));
        start.Click += async (_, _) => await model.StartAsync(lifetime);
        cancel.Click += (_, _) => model.Cancel();
        export.Click += async (_, _) => await Export(false);
        exportHtml.Click += async (_, _) => await Export(true);
        async Task Export(bool html)
        {
            if (!model.CanExport) return;
            var selectedReport = model.Report;
            try
            {
                string? path = await chooseDestination(html ? ".html" : ".json");
                if (path is null) return;
                if (!ReferenceEquals(selectedReport, model.Report)) { model.SetStatus("O resultado mudou. Escolha novamente o relatório."); return; }
                if (html) await model.ExportHtmlAsync(path, lifetime);
                else await model.ExportAsync(path, lifetime);
            }
            catch (OperationCanceledException) { model.SetStatus("Exportação cancelada."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
            { model.SetStatus("Seletor de arquivo indisponível: " + e.Message); }
            finally
            {
                var button = html ? exportHtml : export;
                if (IsEffectivelyVisible && button.IsEffectivelyEnabled && !lifetime.IsCancellationRequested) button.Focus();
            }
        }
        var status = new TextBlock { Name = "CpuStatus", TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)));
        var summary = new TextBlock { Name = "CpuSummary", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        summary.Bind(TextBlock.TextProperty, new Binding(nameof(model.Summary)));
        var progress = new ProgressBar { Minimum = 0, Maximum = CpuValidationViewModel.MaximumSamples, Height = 5 };
        progress.Bind(ProgressBar.ValueProperty, new Binding(nameof(model.CompletedSamples)));
        progress.Bind(IsVisibleProperty, new Binding(nameof(model.IsRunning)));
        AutomationProperties.SetName(progress, "Amostras concluídas da validação CPU");
        AutomationProperties.SetName(preview, "Imagem final da validação CPU reduzida; contagens e hash no resumo textual");
        RenderOptions.SetBitmapInterpolationMode(preview, BitmapInterpolationMode.None);
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "VALIDAÇÃO DO RENDERIZADOR CPU", TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "32×18 pixels · 8 amostras/pixel · uma passada · sem score.\nA imagem é calculada ao clicar. Este modo não mede desempenho nem saúde do hardware.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                new WrapPanel { Orientation = Orientation.Horizontal, Children = { start, cancel, export, exportHtml } },
                progress, status, preview, summary
            }
        };
        model.PropertyChanged += ModelChanged;
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(model.Frame) || ReferenceEquals(displayed, model.Frame)) return;
        displayed = model.Frame;
        var previous = bitmap;
        bitmap = null;
        preview.Source = null;
        preview.IsVisible = displayed is not null;
        if (displayed is { } output)
        {
            var size = output.Result.Workload;
            var pixels = output.CopyPixels();
            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), pixels[i]);
            bitmap = new WriteableBitmap(new PixelSize(size.Width, size.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
                for (int y = 0; y < size.Height; y++)
                    Marshal.Copy(bytes, y * size.Width * 4, IntPtr.Add(buffer.Address, y * buffer.RowBytes), size.Width * 4);
            preview.Source = bitmap;
        }
        previous?.Dispose();
    }

    public void Dispose()
    {
        model.PropertyChanged -= ModelChanged;
        preview.Source = null;
        bitmap?.Dispose();
        bitmap = null;
    }
}
