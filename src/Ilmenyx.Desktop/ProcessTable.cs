// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

/// <summary>Top processes by CPU or memory from the live monitor. Local view only: names are never exported.</summary>
public sealed class ProcessTable : Border
{
    public ProcessTable(UsageViewModel usage)
    {
        var rows = new StackPanel { Name = "ProcessRows" };
        var cpuHeader = new Button { Name = "SortCpu", Content = "CPU ▼", Padding = new Thickness(4, 2), Background = Brushes.Transparent };
        var memHeader = new Button { Name = "SortMemory", Content = "Memória", Padding = new Thickness(4, 2), Background = Brushes.Transparent };
        void Sort() { cpuHeader.Content = usage.SortByMemory ? "CPU" : "CPU ▼"; memHeader.Content = usage.SortByMemory ? "Memória ▼" : "Memória"; }
        cpuHeader.Click += (_, _) => { if (usage.SortByMemory) { usage.ToggleSort(); Sort(); } };
        memHeader.Click += (_, _) => { if (!usage.SortByMemory) { usage.ToggleSort(); Sort(); } };
        var table = new Grid { ColumnDefinitions = new("*,80,110"), Margin = new Thickness(0, 12, 0, 4) };
        table.Children.Add(new TextBlock { Text = "NOME", Foreground = Palette.Faint, FontSize = 11, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) });
        Grid.SetColumn(cpuHeader, 1); Grid.SetColumn(memHeader, 2); table.Children.Add(cpuHeader); table.Children.Add(memHeader);
        void Fill()
        {
            rows.Children.Clear();
            foreach (var row in usage.Rows)
            {
                var line = new Grid { ColumnDefinitions = new("*,80,110") };
                var name = new TextBlock { Text = row.Name, Margin = new Thickness(8, 7), TextTrimming = TextTrimming.CharacterEllipsis };
                var cpu = new TextBlock { Text = row.Cpu, Margin = new Thickness(4, 7), HorizontalAlignment = HorizontalAlignment.Right, Foreground = Palette.Muted };
                var mem = new TextBlock { Text = row.Memory, Margin = new Thickness(4, 7), HorizontalAlignment = HorizontalAlignment.Right, Foreground = Palette.Muted };
                Grid.SetColumn(cpu, 1); Grid.SetColumn(mem, 2);
                line.Children.Add(name); line.Children.Add(cpu); line.Children.Add(mem);
                rows.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = line });
            }
        }
        usage.Updated += Fill; Fill();
        var summary = new TextBlock { Name = "ProcessSummary", FontSize = 12, Foreground = Palette.Faint, Text = usage.ProcessSummary };
        usage.Updated += () => summary.Text = usage.ProcessSummary;
        var layout = new Grid { RowDefinitions = new("Auto,Auto,Auto,*") };
        Grid.SetRow(summary, 1); Grid.SetRow(table, 2);
        var scroll = new ScrollViewer { Name = "ProcessScroll", Content = rows }; Grid.SetRow(scroll, 3);
        layout.Children.Add(new TextBlock { Text = "PROCESSOS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint });
        layout.Children.Add(summary); layout.Children.Add(table); layout.Children.Add(scroll);
        Name = "ProcessPanel"; Background = Palette.Card; BorderBrush = Palette.Line; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius); Padding = new Thickness(20); Child = layout;
    }
}
