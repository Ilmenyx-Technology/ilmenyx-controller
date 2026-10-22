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
        Title = ProductIdentity.Name; Width = 1060; Height = 780; MinWidth = 640; MinHeight = 480;
        Background = Brush.Parse("#17131C"); DataContext = vm;
        var refresh = new Button { Name = "RefreshHardware", Content = "Atualizar" };
        var export = new Button { Name = "ExportHardware", Content = "Exportar hardware JSON" };
        var exportHtml = new Button { Name = "ExportHardwareHtml", Content = "Exportar hardware HTML" };
        var about = new Button { Name = "About", Content = "Licenças e créditos" };
        var requirements = new Button { Name = "ShowRequirements", Content = "Requisitos" };
        requirements.Click += async (_, _) => { await new RequirementsDialog().ShowDialog(this); if (IsVisible) requirements.Focus(); };
        about.Click += async (_, _) => { await new AboutDialog().ShowDialog(this); if (IsVisible) about.Focus(); };
        refresh.Click += async (_, _) => { refresh.IsEnabled = false; try { await vm.RefreshAsync(stop.Token); } finally { refresh.IsEnabled = true; } };
        export.Click += async (_, _) => await ExportHardwareAsync(false);
        exportHtml.Click += async (_, _) => await ExportHardwareAsync(true);
        async Task ExportHardwareAsync(bool html)
        {
            if (vm.Snapshot is not { } snapshot) { vm.SetStatus("Atualize os dados antes de exportar."); return; }
            export.IsEnabled = false; exportHtml.IsEnabled = false;
            try
            {
                string extension = html ? ".html" : ".json";
                var path = await JsonSaveDialog.ChooseAsync(this, $"ilmenyx-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension);
                if (path is null) return;
                if (html) await Task.Run(() => HardwareHtmlReport.ExportAsync(snapshot, path, stop.Token), stop.Token);
                else await SnapshotReport.ExportAsync(snapshot, path, stop.Token);
                vm.SetStatus("Snapshot exportado para arquivo local.");
            }
            catch (OperationCanceledException) { vm.SetStatus("Exportação cancelada."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { vm.SetStatus("Não foi possível exportar. Escolha um nome novo: " + e.Message); }
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
        stack.Children.Add(new TextBlock { Text = "Diagnóstico de hardware · Linux · 0.26.0", Foreground = Brushes.LightGray });
        stack.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8, Children = { refresh, export, exportHtml, requirements, about } });
        foreach (var (label, property) in new[] { ("PROCESSADOR", nameof(vm.Cpu)), ("MEMÓRIA", nameof(vm.Memory)), ("SISTEMA", nameof(vm.System)), ("DISPOSITIVOS DE BLOCO", nameof(vm.Disks)) })
        {
            var value = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 17 };
            value.Bind(TextBlock.TextProperty, new Binding(property));
            cards.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 12), Background = Brush.Parse("#2A2531"), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 12 }, value } } });
        }
        stack.Children.Add(cards);
        var gpuSummary = new TextBlock { Name = "GpuSummary", TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        gpuSummary.Bind(TextBlock.TextProperty, new Binding(nameof(vm.Gpus)));
        stack.Children.Add(new Border
        {
            Margin = new Thickness(0, 0, 12, 0),
            Background = Brush.Parse("#2A2531"),
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
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Background = Brush.Parse("#3A3020"), CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 6),
                        Child = new TextBlock
                        {
                            Name = "GpuValidationNotice",
                            Text = ValidationNotices.AmdNvidia,
                            Foreground = Brush.Parse("#F3CE85"), TextWrapping = TextWrapping.Wrap, FontSize = 13
                        }
                    }
                }
            }
        });
        stack.Children.Add(new TextBlock { Text = "Fontes: /proc/cpuinfo, /proc/meminfo, /etc/os-release, /sys/block.\nSensores Linux e validação CPU reduzida disponíveis abaixo. GPU: inventário DRM/PCI; SMART: consulta somente CLI por opt-in; importação offline de arquivo abaixo.\nCapacidade de dispositivo não representa espaço livre. Dados não indicam saúde.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
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
            else vm.SetStatus("Backend disponível apenas no Linux.");
        };
        Closed += (_, _) => { stop.Cancel(); sensors.Dispose(); smartImport.Dispose(); cpu.Dispose(); memory.Dispose(); simulation.Dispose(); cpuPanel.Dispose(); };
    }
}
