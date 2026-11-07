// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
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
    private readonly Image preview = new() { Name = "CpuPreview", Width = 256, Height = 144, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Fill, IsVisible = false };
    private readonly Border placeholder = new() { Width = 256, Height = 144, HorizontalAlignment = HorizontalAlignment.Left, Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius) };
    private WriteableBitmap? bitmap;
    private CpuRenderOutput? displayed;

    public CpuValidationPanel(CpuValidationViewModel model, Func<string, Task<string?>> chooseDestination, CancellationToken lifetime)
    {
        this.model = model;
        DataContext = model;
        ValidationLook.Surface(this);
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
        ValidationLook.Actions(start, cancel, export, exportHtml);
        var status = new TextBlock { Name = "CpuStatus", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)));
        var summary = new TextBlock { Name = "CpuSummary", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted };
        summary.Bind(TextBlock.TextProperty, new Binding(nameof(model.Summary)));
        var progress = new ProgressBar { Minimum = 0, Maximum = CpuValidationViewModel.MaximumSamples, Height = 5, MinHeight = 5 };
        progress.Bind(ProgressBar.ValueProperty, new Binding(nameof(model.CompletedSamples)));
        progress.Bind(IsVisibleProperty, new Binding(nameof(model.IsRunning)));
        AutomationProperties.SetName(progress, "Amostras concluídas da validação CPU");
        AutomationProperties.SetName(preview, "Imagem final da validação CPU reduzida; contagens e hash no resumo textual");
        RenderOptions.SetBitmapInterpolationMode(preview, BitmapInterpolationMode.None);
        placeholder.Child = new TextBlock { Text = "A imagem final aparece aqui depois da validação.", FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) };
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                ValidationLook.Title(Icons.Cpu, "VALIDAÇÃO DO RENDERIZADOR CPU"),
                ValidationLook.Description("32×18 pixels · 8 amostras/pixel · uma passada · sem score.\nA imagem é calculada ao clicar. Este modo não mede desempenho nem saúde do hardware."),
                new WrapPanel { Orientation = Orientation.Horizontal, Children = { start, cancel, export, exportHtml } },
                progress, status, preview, placeholder, summary
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
        placeholder.IsVisible = displayed is null;
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

/// <summary>
/// Shared look of the reduced validation panels (cinematic ambient dark): translucent card surface, icon title, compact secondary text,
/// the start action in the brand accent and the other actions as quiet buttons. Appearance only; names, texts and order are untouched.
/// </summary>
internal static class ValidationLook
{
    public static void Surface(Border panel)
    {
        panel.Background = Palette.Card; panel.BorderBrush = Palette.Line; panel.BorderThickness = new Thickness(1);
        panel.CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius); panel.Padding = new Thickness(20, 16); panel.Margin = new Thickness(0, 0, 12, 0);
    }

    public static Control Title(string icon, string text)
    {
        var glyph = UiKit.Icon(icon, 16, Palette.AccentText); glyph.VerticalAlignment = VerticalAlignment.Top; glyph.Margin = new Thickness(0, 1, 8, 0);
        var title = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(title, 1);
        return new Grid { ColumnDefinitions = new("Auto,*"), Children = { glyph, title } };
    }

    public static TextBlock Description(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted };

    public static void Actions(Button start, params Button[] others)
    {
        foreach (var b in others.Prepend(start)) { b.FontSize = 12; b.Padding = new Thickness(13, 6); b.CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius); }
        start.Background = Palette.Accent; start.Foreground = Palette.OnAccent; start.FontWeight = FontWeight.SemiBold;
        foreach (var b in others) { b.Background = Palette.Card; b.Foreground = Palette.Muted; }
    }
}
