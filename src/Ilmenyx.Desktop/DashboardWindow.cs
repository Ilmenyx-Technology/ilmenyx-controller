// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;
namespace Ilmenyx.Desktop;

public sealed class DashboardWindow : Window
{
    private readonly DashboardViewModel vm = new(LinuxPlatform.CreateHardwareProvider());
    private readonly CpuValidationViewModel cpu = new(LinuxPlatform.CreateHardwareProvider(), new CpuBenchmarkRunner());
    private readonly SensorMonitorViewModel sensors = new(LinuxPlatform.CreateSensorProvider());
    private readonly MemoryValidationViewModel memory = new(LinuxPlatform.CreateHardwareProvider(), new MemoryValidationRunner());
    private readonly GameSimValidationViewModel simulation = new(LinuxPlatform.CreateHardwareProvider(), new GameSimValidationRunner());
    private readonly SmartImportViewModel smartImport = new(SmartctlReportImport.ReadFileAsync);
    private readonly CancellationTokenSource stop = new();
    public DashboardWindow()
    {
        Icon = AppIcon.Window; Title = ProductIdentity.Name; Width = 1060; Height = 780; MinWidth = 640; MinHeight = 480;
        Background = Palette.Bg; DataContext = vm;
        var refresh = new Button { Name = "RefreshHardware", Content = L.T("Atualizar") };
        var export = new Button { Name = "ExportHardware", Content = L.T("Exportar hardware JSON") };
        var exportHtml = new Button { Name = "ExportHardwareHtml", Content = L.T("Exportar hardware HTML") };
        var about = new Button { Name = "About", Content = L.T("Licenças e créditos") };
        var requirements = new Button { Name = "ShowRequirements", Content = L.T("Requisitos") };
        requirements.Click += async (_, _) => { await new RequirementsDialog().ShowDialog(this); if (IsVisible) requirements.Focus(); };
        about.Click += async (_, _) => { await new AboutDialog().ShowDialog(this); if (IsVisible) about.Focus(); };
        refresh.Click += async (_, _) => { refresh.IsEnabled = false; try { await vm.RefreshAsync(stop.Token); } finally { refresh.IsEnabled = true; } };
        export.Click += async (_, _) => await ExportHardwareAsync(false);
        exportHtml.Click += async (_, _) => await ExportHardwareAsync(true);
        async Task ExportHardwareAsync(bool html)
        {
            if (vm.Snapshot is not { } snapshot) { vm.SetStatus(L.T("Atualize os dados antes de exportar.")); return; }
            export.IsEnabled = false; exportHtml.IsEnabled = false;
            try
            {
                string extension = html ? ".html" : ".json";
                var path = await JsonSaveDialog.ChooseAsync(this, $"ilmenyx-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension);
                if (path is null) return;
                if (html) await Task.Run(() => HardwareHtmlReport.ExportAsync(snapshot, path, stop.Token), stop.Token);
                else await SnapshotReport.ExportAsync(snapshot, path, stop.Token);
                vm.SetStatus(L.T("Snapshot exportado para arquivo local."));
            }
            catch (OperationCanceledException) { vm.SetStatus(L.T("Exportação cancelada.")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { vm.SetStatus(L.T("Não foi possível exportar. Escolha um nome novo: ") + e.Message); }
            finally
            {
                export.IsEnabled = true; exportHtml.IsEnabled = true;
                if (IsVisible && !stop.IsCancellationRequested) (html ? exportHtml : export).Focus();
            }
        }
        var cards = new UniformGrid { Columns = 2 };
        var stack = new StackPanel { Margin = new Thickness(32), Spacing = 18 };
        stack.Children.Add(new TextBlock { Name = "ProductName", Text = ProductIdentity.Name, FontSize = 32, FontWeight = FontWeight.Bold });
        stack.Children.Add(new TextBlock { Name = "MaintainerSignature", Text = ProductIdentity.Signature });
        stack.Children.Add(new TextBlock { Text = L.T("Diagnóstico de hardware · Linux") + " · " + typeof(DashboardWindow).Assembly.GetName().Version?.ToString(3), Foreground = Palette.Muted });
        stack.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8, Children = { refresh, export, exportHtml, requirements, about } });
        foreach (var (label, property) in new[] { (L.T("PROCESSADOR"), nameof(vm.Cpu)), (L.T("MEMÓRIA"), nameof(vm.Memory)), (L.T("SISTEMA"), nameof(vm.System)), (L.T("DISPOSITIVOS DE BLOCO"), nameof(vm.Disks)) })
        {
            var value = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 17 };
            value.Bind(TextBlock.TextProperty, new Binding(property));
            cards.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 12), Background = Palette.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12 }, value } } });
        }
        stack.Children.Add(cards);
        var gpuSummary = new TextBlock { Name = "GpuSummary", TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        string? gpuNotice = ValidationNotices.For(DrmGpuInfoProvider.Vendors());
        gpuSummary.Bind(TextBlock.TextProperty, new Binding(nameof(vm.Gpus)));
        stack.Children.Add(new Border
        {
            Margin = new Thickness(0, 0, 12, 0),
            Background = Palette.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    gpuSummary,
                    new Border
                    {
                        IsVisible = gpuNotice is not null, HorizontalAlignment = HorizontalAlignment.Left,
                        Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius),
                        Padding = new Thickness(10, 6),
                        Child = new TextBlock
                        {
                            Name = "GpuValidationNotice",
                            Text = gpuNotice ?? "",
                            Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap, FontSize = 13
                        }
                    }
                }
            }
        });
        stack.Children.Add(new TextBlock { Text = L.T("Fontes: /proc/cpuinfo, /proc/meminfo, /etc/os-release, /sys/block.\nSensores Linux e validação CPU reduzida disponíveis abaixo. GPU: inventário DRM/PCI; SMART: consulta somente CLI por opt-in; importação offline de arquivo abaixo.\nCapacidade de dispositivo não representa espaço livre. Dados não indicam saúde."), TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted });
        var status = new TextBlock { Name = "HardwareStatus", TextWrapping = TextWrapping.Wrap }; status.Bind(TextBlock.TextProperty, new Binding(nameof(vm.Status))); stack.Children.Add(status);
        stack.Children.Add(new SensorPanel(sensors,
            extension => JsonSaveDialog.ChooseAsync(this, $"ilmenyx-sensors-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension), stop.Token));
        stack.Children.Add(new SmartImportPanel(smartImport,
            extension => JsonSaveDialog.ChooseAsync(this, $"ilmenyx-smart-import-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension), stop.Token));
        var cpuPanel = new CpuValidationPanel(cpu,
            extension => JsonSaveDialog.ChooseAsync(this, $"ilmenyx-cpu-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension), stop.Token);
        stack.Children.Add(cpuPanel);
        stack.Children.Add(new MemoryValidationPanel(memory,
            extension => JsonSaveDialog.ChooseAsync(this, $"ilmenyx-memory-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension), stop.Token));
        stack.Children.Add(new GameSimValidationPanel(simulation,
            extension => JsonSaveDialog.ChooseAsync(this, $"ilmenyx-sim-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension), stop.Token));
        Content = new ScrollViewer { Name = "DashboardScroll", Content = stack };
        Opened += async (_, _) =>
        {
            if (OperatingSystem.IsLinux()) { await vm.RefreshAsync(stop.Token); await sensors.RefreshAsync(stop.Token); }
            else vm.SetStatus(L.T("Backend disponível apenas no Linux."));
        };
        Closed += (_, _) => { stop.Cancel(); sensors.Dispose(); smartImport.Dispose(); cpu.Dispose(); memory.Dispose(); simulation.Dispose(); cpuPanel.Dispose(); };
    }
}
