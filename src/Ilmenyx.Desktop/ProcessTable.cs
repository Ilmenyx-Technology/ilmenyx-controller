// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>Top processes by CPU or memory from the live monitor. Local view only: names are never exported.</summary>
public sealed class ProcessTable : Border
{
    public ProcessTable(UsageViewModel usage)
    {
        var rows = new StackPanel { Name = "ProcessRows" };
        var cpuHeader = SortButton("SortCpu", "CPU ▼"); var memHeader = SortButton("SortMemory", L.T("Memória"));
        void Sort()
        {
            cpuHeader.Content = usage.SortByMemory ? "CPU" : "CPU ▼"; memHeader.Content = usage.SortByMemory ? L.T("Memória ▼") : L.T("Memória");
            cpuHeader.Foreground = usage.SortByMemory ? Palette.Faint : Palette.AccentText; memHeader.Foreground = usage.SortByMemory ? Palette.AccentText : Palette.Faint;
        }
        cpuHeader.Click += (_, _) => { if (usage.SortByMemory) { usage.ToggleSort(); Sort(); } };
        memHeader.Click += (_, _) => { if (!usage.SortByMemory) { usage.ToggleSort(); Sort(); } };
        var table = new Grid { ColumnDefinitions = new("*,90,120"), Margin = new Thickness(0, 0, 0, 4) };
        var nameHead = UiKit.Label(L.T("NOME")); nameHead.VerticalAlignment = VerticalAlignment.Center; nameHead.Margin = new Thickness(4, 0);
        table.Children.Add(nameHead);
        Grid.SetColumn(cpuHeader, 1); Grid.SetColumn(memHeader, 2); table.Children.Add(cpuHeader); table.Children.Add(memHeader);
        // Rows are reused and only their texts change; off screen nothing is rebuilt (the monitor ticks every second on every page).
        var lines = new List<(TextBlock Name, TextBlock Cpu, TextBlock Mem)>();
        bool stale = false;
        void Fill()
        {
            var data = usage.Rows;
            while (lines.Count < data.Count)
            {
                var line = new Grid { ColumnDefinitions = new("*,90,120") };
                var name = new TextBlock { FontSize = 13, Margin = new Thickness(4, 8), TextTrimming = TextTrimming.CharacterEllipsis };
                var cpu = new TextBlock { FontSize = 12.5, FontFamily = IlmenyxTheme.Mono, Margin = new Thickness(4, 8, 10, 8), HorizontalAlignment = HorizontalAlignment.Right };
                var mem = new TextBlock { FontSize = 12.5, FontFamily = IlmenyxTheme.Mono, Margin = new Thickness(4, 8, 10, 8), HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(cpu, 1); Grid.SetColumn(mem, 2);
                line.Children.Add(name); line.Children.Add(cpu); line.Children.Add(mem);
                rows.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = line });
                lines.Add((name, cpu, mem));
            }
            while (lines.Count > data.Count) { rows.Children.RemoveAt(rows.Children.Count - 1); lines.RemoveAt(lines.Count - 1); }
            for (int i = 0; i < data.Count; i++)
            {
                var (name, cpu, mem) = lines[i];
                name.Text = data[i].Name; cpu.Text = data[i].Cpu; mem.Text = data[i].Memory;
                cpu.Foreground = usage.SortByMemory ? Palette.Muted : Palette.Fg; mem.Foreground = usage.SortByMemory ? Palette.Fg : Palette.Muted;
            }
        }
        var summary = new TextBlock { Name = "ProcessSummary", FontSize = 12, Foreground = Palette.Faint, Text = usage.ProcessSummary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 10) };
        void Update()
        {
            if (TopLevel.GetTopLevel(this) is null || !IsEffectivelyVisible) { stale = true; return; }
            Fill(); summary.Text = usage.ProcessSummary;
        }
        usage.Updated += Update; Fill();
        AttachedToVisualTree += (_, _) => { if (stale) { stale = false; Fill(); summary.Text = usage.ProcessSummary; } };
        Sort();
        // Table surface: one subtle card, header in small caps labels, rows separated by hairlines only.
        var tableLayout = new Grid { RowDefinitions = new("Auto,*") };
        var scroll = new ScrollViewer { Name = "ProcessScroll", Content = rows }; Grid.SetRow(scroll, 1);
        tableLayout.Children.Add(table); tableLayout.Children.Add(scroll);
        var surface = UiKit.Card(tableLayout, new Thickness(0)); surface.Padding = new Thickness(16, 10, 8, 8);
        var layout = new Grid { RowDefinitions = new("Auto,Auto,*") };
        var header = UiKit.PageHeader(L.T("Processos"), L.T("Os que mais usam CPU e memória agora, do monitor ao vivo. Só nesta tela: os nomes nunca são exportados."));
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
