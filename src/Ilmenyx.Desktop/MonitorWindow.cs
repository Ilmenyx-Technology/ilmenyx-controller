// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>Main window: live system monitor (processes left, charts right) with the diagnostics dashboard one click away.</summary>
public sealed class MonitorWindow : Window
{
    private static readonly IBrush Panel = Brush.Parse("#2A2531"), Muted = Brush.Parse("#B9B2C4"), Accent = Brush.Parse("#D9A8FF"), Separator = Brush.Parse("#3D3646");
    private readonly UsageViewModel usage = new(new LinuxUsageProvider());
    private readonly CancellationTokenSource stop = new();
    private DashboardWindow? diagnostics;

    public MonitorWindow()
    {
        Title = ProductIdentity.Name + " — Monitor"; Width = 1180; Height = 780; MinWidth = 720; MinHeight = 480;
        Background = Brush.Parse("#17131C");
        var pause = new Button { Name = "ToggleMonitor", Content = "Pausar" };
        var details = new Button { Name = "OpenDiagnostics", Content = "Diagnósticos de hardware" };
        var about = new Button { Name = "MonitorAbout", Content = "Licenças e créditos" };
        pause.Click += async (_, _) =>
        {
            if (usage.IsRunning) await usage.StopAsync(); else usage.Start(stop.Token);
            pause.Content = usage.IsRunning ? "Pausar" : "Retomar";
            if (IsVisible && !stop.IsCancellationRequested) pause.Focus();
        };
        details.Click += (_, _) =>
        {
            if (diagnostics is { IsVisible: true }) { diagnostics.Activate(); return; }
            diagnostics = new DashboardWindow(); diagnostics.Show(this);
        };
        about.Click += async (_, _) => { await new AboutDialog().ShowDialog(this); if (IsVisible) about.Focus(); };

        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(24, 16, 24, 8) };
        header.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Name = "ProductName", Text = ProductIdentity.Name, FontSize = 26, FontWeight = FontWeight.Bold },
                new TextBlock { Name = "MaintainerSignature", Text = ProductIdentity.Signature, Foreground = Muted, TextWrapping = TextWrapping.Wrap }
            }
        });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { pause, details, about } };
        Grid.SetColumn(actions, 1); header.Children.Add(actions);

        var columns = new Grid { ColumnDefinitions = new("1.15*,*"), Margin = new Thickness(24, 0, 24, 16) };
        var left = ProcessPanel();
        var right = new ScrollViewer { Name = "MonitorCardsScroll", Margin = new Thickness(12, 0, 0, 0), Content = Cards() };
        Grid.SetColumn(right, 1);
        columns.Children.Add(left); columns.Children.Add(right);

        var status = new TextBlock { Name = "MonitorStatus", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 0, 24, 12) };
        var root = new Grid { RowDefinitions = new("Auto,*,Auto") };
        Grid.SetRow(columns, 1); Grid.SetRow(status, 2);
        root.Children.Add(header); root.Children.Add(columns); root.Children.Add(status);
        Content = root;
        usage.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(usage.Status)) status.Text = usage.Status; };
        status.Text = usage.Status;
        Opened += (_, _) => { if (OperatingSystem.IsLinux()) usage.Start(stop.Token); else status.Text = "Monitor disponível apenas no Linux."; };
        Closed += async (_, _) => { stop.Cancel(); await usage.StopAsync(); usage.Dispose(); };
    }

    private TextBlock Text(string name, Func<string> read, double size = 14, IBrush? brush = null, bool bold = false)
    {
        var block = new TextBlock { Name = name, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, Text = read() };
        if (brush is not null) block.Foreground = brush;
        usage.Updated += () => block.Text = read();
        return block;
    }

    private Border ProcessPanel()
    {
        var rows = new StackPanel { Name = "ProcessRows" };
        var cpuHeader = new Button { Name = "SortCpu", Content = "CPU ▼", Padding = new Thickness(4, 2), Background = Brushes.Transparent };
        var memHeader = new Button { Name = "SortMemory", Content = "Memória", Padding = new Thickness(4, 2), Background = Brushes.Transparent };
        void Sort() { cpuHeader.Content = usage.SortByMemory ? "CPU" : "CPU ▼"; memHeader.Content = usage.SortByMemory ? "Memória ▼" : "Memória"; }
        cpuHeader.Click += (_, _) => { if (usage.SortByMemory) { usage.ToggleSort(); Sort(); } };
        memHeader.Click += (_, _) => { if (!usage.SortByMemory) { usage.ToggleSort(); Sort(); } };
        var table = new Grid { ColumnDefinitions = new("*,80,110"), Margin = new Thickness(0, 12, 0, 4) };
        table.Children.Add(new TextBlock { Text = "Nome", Foreground = Muted, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) });
        Grid.SetColumn(cpuHeader, 1); Grid.SetColumn(memHeader, 2); table.Children.Add(cpuHeader); table.Children.Add(memHeader);
        usage.Updated += () =>
        {
            rows.Children.Clear();
            foreach (var row in usage.Rows)
            {
                var line = new Grid { ColumnDefinitions = new("*,80,110"), Margin = new Thickness(0, 0, 0, 0) };
                var name = new TextBlock { Text = row.Name, Margin = new Thickness(8, 7), TextTrimming = TextTrimming.CharacterEllipsis };
                var cpu = new TextBlock { Text = row.Cpu, Margin = new Thickness(4, 7), HorizontalAlignment = HorizontalAlignment.Right };
                var mem = new TextBlock { Text = row.Memory, Margin = new Thickness(4, 7), HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(cpu, 1); Grid.SetColumn(mem, 2);
                line.Children.Add(name); line.Children.Add(cpu); line.Children.Add(mem);
                rows.Children.Add(new Border { BorderBrush = Separator, BorderThickness = new Thickness(0, 1, 0, 0), Child = line });
            }
        };
        var scroll = new ScrollViewer { Name = "ProcessScroll", Content = rows };
        var layout = new Grid { RowDefinitions = new("Auto,Auto,Auto,*") };
        var title = new TextBlock { Text = "Processos", FontSize = 20, FontWeight = FontWeight.Bold };
        var summary = Text("ProcessSummary", () => usage.ProcessSummary, 12, Muted);
        Grid.SetRow(summary, 1); Grid.SetRow(table, 2); Grid.SetRow(scroll, 3);
        layout.Children.Add(title); layout.Children.Add(summary); layout.Children.Add(table); layout.Children.Add(scroll);
        return new Border { Name = "ProcessPanel", Background = Panel, CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = layout };
    }

    private StackPanel Cards()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Card("CPU", "CpuChart", () => usage.CpuSeries, 100, [Text("CpuLine", () => usage.CpuLine, 14), Text("CpuModel", () => usage.CpuModel, 12, Muted)]));
        stack.Children.Add(Card("Memória", "MemoryChart", () => usage.MemorySeries, 100, [Text("MemoryLine", () => usage.MemoryLine, 14), Text("MemorySub", () => usage.MemorySub, 12, Muted)]));
        stack.Children.Add(Card("Disco", "DiskChart", () => usage.DiskSeries, null, [Text("DiskLine", () => usage.DiskLine, 14), new TextBlock { Text = "Soma dos dispositivos inteiros (sd*, nvme*, vd*, mmcblk*)", FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap }]));
        stack.Children.Add(Card("Rede", "NetChart", () => usage.NetSeries, null, [Text("NetLine", () => usage.NetLine, 14), Text("NetSub", () => usage.NetSub, 12, Muted)]));
        var gpuText = new TextBlock { Name = "MonitorGpuSummary", Text = "Lendo inventário DRM…", TextWrapping = TextWrapping.Wrap };
        var notice = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brush.Parse("#3A3020"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6),
            Child = new TextBlock { Name = "GpuValidationNotice", Text = ValidationNotices.AmdNvidia, Foreground = Brush.Parse("#F3CE85"), TextWrapping = TextWrapping.Wrap, FontSize = 13 }
        };
        stack.Children.Add(new Border
        {
            Background = Panel, CornerRadius = new CornerRadius(12), Padding = new Thickness(16),
            Child = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "GPU", FontSize = 20, FontWeight = FontWeight.Bold }, gpuText, new TextBlock { Text = "Contadores de uso e VRAM não são lidos neste monitor.", FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap }, notice } }
        });
        Opened += async (_, _) =>
        {
            if (!OperatingSystem.IsLinux()) return;
            try
            {
                var snapshot = await Task.Run(() => LinuxPlatform.CreateHardwareProvider().CollectAsync(stop.Token), stop.Token);
                if (!stop.IsCancellationRequested) gpuText.Text = snapshot.Gpus is { } g ? GpuPresentation.Text(g) : "Inventário DRM indisponível.";
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { gpuText.Text = "Inventário DRM indisponível: " + e.Message; }
        };
        return stack;
    }

    private Border Card(string title, string chartName, Func<IReadOnlyList<double>> series, double? ceiling, Control[] lines)
    {
        var chart = new UsageChart { Name = chartName, Width = 260, Height = 120 };
        AutomationProperties.SetName(chart, "Gráfico de " + title + " (últimos 60 s)");
        usage.Updated += () => chart.SetValues(series(), ceiling);
        var text = new StackPanel { Spacing = 4, Margin = new Thickness(14, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Accent });
        foreach (var line in lines) text.Children.Add(line);
        var layout = new Grid { ColumnDefinitions = new("Auto,*") };
        Grid.SetColumn(text, 1); layout.Children.Add(chart); layout.Children.Add(text);
        return new Border { Background = Panel, CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = layout };
    }
}
