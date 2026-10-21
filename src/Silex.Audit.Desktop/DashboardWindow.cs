using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Silex.Audit.Core;
using Silex.Audit.Core.Benchmarks;
using Silex.Audit.Platform.Linux;
namespace Silex.Audit.Desktop;

public sealed class DashboardWindow : Window
{
    private readonly DashboardViewModel vm = new(LinuxPlatform.CreateHardwareProvider());
    private readonly CpuValidationViewModel cpu = new(LinuxPlatform.CreateHardwareProvider(), new CpuBenchmarkRunner());
    private readonly SensorMonitorViewModel sensors = new(LinuxPlatform.CreateSensorProvider());
    private readonly MemoryValidationViewModel memory = new(LinuxPlatform.CreateHardwareProvider(), new MemoryValidationRunner());
    private readonly GameSimValidationViewModel simulation = new(LinuxPlatform.CreateHardwareProvider(), new GameSimValidationRunner());
    private readonly CancellationTokenSource stop = new();
    public DashboardWindow()
    {
        Title = "Silex Audit"; Width = 1060; Height = 780; MinWidth = 640; MinHeight = 480;
        Background = Brush.Parse("#111315"); DataContext = vm;
        var refresh = new Button { Name = "RefreshHardware", Content = "Atualizar" };
        var export = new Button { Name = "ExportHardware", Content = "Exportar hardware JSON" };
        var exportHtml = new Button { Name = "ExportHardwareHtml", Content = "Exportar hardware HTML" };
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
                var path = await JsonSaveDialog.ChooseAsync(this, $"silex-{DateTime.Now:yyyyMMdd-HHmmss}{extension}", extension);
                if (path is null) return;
                if (html) await Task.Run(() => HardwareHtmlReport.ExportAsync(snapshot, path, stop.Token), stop.Token);
                else await SnapshotReport.ExportAsync(snapshot, path, stop.Token);
                vm.SetStatus("Snapshot exportado para arquivo local.");
            }
            catch (OperationCanceledException) { vm.SetStatus("Exportação cancelada."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { vm.SetStatus("Não foi possível exportar. Escolha um nome novo: " + e.Message); }
            finally { export.IsEnabled = true; exportHtml.IsEnabled = true; }
        }
        var cards = new UniformGrid { Columns = 2 };
        var stack = new StackPanel { Margin = new Thickness(32), Spacing = 18 };
        stack.Children.Add(new TextBlock { Text = "SILEX AUDIT", FontSize = 32, FontWeight = FontWeight.Bold });
        stack.Children.Add(new TextBlock { Text = "Diagnóstico de hardware · Linux · 0.9.0", Foreground = Brushes.LightGray });
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { refresh, export, exportHtml } });
        foreach (var (label, property) in new[] { ("PROCESSADOR", nameof(vm.Cpu)), ("MEMÓRIA", nameof(vm.Memory)), ("SISTEMA", nameof(vm.System)), ("DISPOSITIVOS DE BLOCO", nameof(vm.Disks)) })
        {
            var value = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 17 };
            value.Bind(TextBlock.TextProperty, new Binding(property));
            cards.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 12), Background = Brush.Parse("#24252E"), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 12 }, value } } });
        }
        stack.Children.Add(cards);
        var gpuSummary = new TextBlock { Name = "GpuSummary", TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        gpuSummary.Bind(TextBlock.TextProperty, new Binding(nameof(vm.Gpus)));
        stack.Children.Add(new Border
        {
            Margin = new Thickness(0, 0, 12, 0),
            Background = Brush.Parse("#24252E"),
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
        stack.Children.Add(new TextBlock { Text = "Fontes: /proc/cpuinfo, /proc/meminfo, /etc/os-release, /sys/block.\nSensores Linux e validação CPU reduzida disponíveis abaixo. GPU: inventário DRM/PCI; SMART: somente CLI por opt-in.\nCapacidade de dispositivo não representa espaço livre. Dados não indicam saúde.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        var status = new TextBlock { Name = "HardwareStatus", TextWrapping = TextWrapping.Wrap }; status.Bind(TextBlock.TextProperty, new Binding(nameof(vm.Status))); stack.Children.Add(status);
        stack.Children.Add(new SensorPanel(sensors,
            () => JsonSaveDialog.ChooseAsync(this, $"silex-sensors-{DateTime.Now:yyyyMMdd-HHmmss}.json"), stop.Token));
        var cpuPanel = new CpuValidationPanel(cpu,
            () => JsonSaveDialog.ChooseAsync(this, $"silex-cpu-{DateTime.Now:yyyyMMdd-HHmmss}.json"), stop.Token);
        stack.Children.Add(cpuPanel);
        stack.Children.Add(new MemoryValidationPanel(memory,
            () => JsonSaveDialog.ChooseAsync(this, $"silex-memory-{DateTime.Now:yyyyMMdd-HHmmss}.json"), stop.Token));
        stack.Children.Add(new GameSimValidationPanel(simulation,
            () => JsonSaveDialog.ChooseAsync(this, $"silex-sim-{DateTime.Now:yyyyMMdd-HHmmss}.json"), stop.Token));
        Content = new ScrollViewer { Name = "DashboardScroll", Content = stack };
        Opened += async (_, _) =>
        {
            if (OperatingSystem.IsLinux()) { await vm.RefreshAsync(stop.Token); await sensors.RefreshAsync(stop.Token); }
            else vm.SetStatus("Backend disponível apenas no Linux.");
        };
        Closed += (_, _) => { stop.Cancel(); sensors.Dispose(); cpu.Dispose(); memory.Dispose(); simulation.Dispose(); cpuPanel.Dispose(); };
    }
}
