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
        var cpuHeader = SortButton("SortCpu", "CPU ▼"); var memHeader = SortButton("SortMemory", "Memória");
        void Sort()
        {
            cpuHeader.Content = usage.SortByMemory ? "CPU" : "CPU ▼"; memHeader.Content = usage.SortByMemory ? "Memória ▼" : "Memória";
            cpuHeader.Foreground = usage.SortByMemory ? Palette.Faint : Palette.AccentText; memHeader.Foreground = usage.SortByMemory ? Palette.AccentText : Palette.Faint;
        }
        cpuHeader.Click += (_, _) => { if (usage.SortByMemory) { usage.ToggleSort(); Sort(); } };
        memHeader.Click += (_, _) => { if (!usage.SortByMemory) { usage.ToggleSort(); Sort(); } };
        var table = new Grid { ColumnDefinitions = new("*,90,120"), Margin = new Thickness(0, 0, 0, 4) };
        var nameHead = UiKit.Label("NOME"); nameHead.VerticalAlignment = VerticalAlignment.Center; nameHead.Margin = new Thickness(4, 0);
        table.Children.Add(nameHead);
        Grid.SetColumn(cpuHeader, 1); Grid.SetColumn(memHeader, 2); table.Children.Add(cpuHeader); table.Children.Add(memHeader);
        void Fill()
        {
            rows.Children.Clear();
            foreach (var row in usage.Rows)
            {
                var line = new Grid { ColumnDefinitions = new("*,90,120") };
                var name = new TextBlock { Text = row.Name, FontSize = 13, Margin = new Thickness(4, 8), TextTrimming = TextTrimming.CharacterEllipsis };
                var cpu = new TextBlock { Text = row.Cpu, FontSize = 12.5, FontFamily = IlmenyxTheme.Mono, Margin = new Thickness(4, 8, 10, 8), HorizontalAlignment = HorizontalAlignment.Right, Foreground = usage.SortByMemory ? Palette.Muted : Palette.Fg };
                var mem = new TextBlock { Text = row.Memory, FontSize = 12.5, FontFamily = IlmenyxTheme.Mono, Margin = new Thickness(4, 8, 10, 8), HorizontalAlignment = HorizontalAlignment.Right, Foreground = usage.SortByMemory ? Palette.Fg : Palette.Muted };
                Grid.SetColumn(cpu, 1); Grid.SetColumn(mem, 2);
                line.Children.Add(name); line.Children.Add(cpu); line.Children.Add(mem);
                rows.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = line });
            }
        }
        usage.Updated += Fill; Fill();
        var summary = new TextBlock { Name = "ProcessSummary", FontSize = 12, Foreground = Palette.Faint, Text = usage.ProcessSummary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 10) };
        usage.Updated += () => summary.Text = usage.ProcessSummary;
        Sort();
        // Table surface: one subtle card, header in small caps labels, rows separated by hairlines only.
        var tableLayout = new Grid { RowDefinitions = new("Auto,*") };
        var scroll = new ScrollViewer { Name = "ProcessScroll", Content = rows }; Grid.SetRow(scroll, 1);
        tableLayout.Children.Add(table); tableLayout.Children.Add(scroll);
        var surface = UiKit.Card(tableLayout, new Thickness(0)); surface.Padding = new Thickness(16, 10, 8, 8);
        var layout = new Grid { RowDefinitions = new("Auto,Auto,*") };
        var header = UiKit.PageHeader("Processos", "Os que mais usam CPU e memória agora, do monitor ao vivo. Só nesta tela: os nomes nunca são exportados.");
        Grid.SetRow(summary, 1); Grid.SetRow(surface, 2);
        layout.Children.Add(header); layout.Children.Add(summary); layout.Children.Add(surface);
        Name = "ProcessPanel"; Background = Brushes.Transparent; Padding = new Thickness(0, 4, 10, 12); Child = layout;
    }

    private static Button SortButton(string name, string text) => new()
    {
        Name = name, Content = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Padding = new Thickness(6, 3), Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0),
    };
}
