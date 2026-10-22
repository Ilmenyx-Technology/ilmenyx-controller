// New Ilmenyx additions: see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed partial class ShellWindow
{
    private void BuildPages()
    {
        pages["dash"] = ("Visão geral", Overview, null);
        pages["system"] = ("Sistema", SystemPage, null);
        pages["sensors"] = ("Sensores", () => Scroll("SensorsScroll", new SensorPanel(sensors, Destination("sensors"), stop.Token)), null);
        pages["gpu"] = ("GPU", GpuPage, null);
        pages["disks"] = ("Discos", DisksPage, null);
        pages["energy"] = ("Energia", EnergyPage, null);
        pages["net"] = ("Rede", NetPage, null);
        pages["procs"] = ("Processos", () => new ProcessTable(usage), null);
        pages["validate"] = ("Validações reduzidas", ValidatePage, null);
        pages["driver"] = ("Drivers", () => Scroll("DriverScroll", new DriverPage(Destination("drivers"), stop.Token) { Margin = new Thickness(0, 4, 8, 12) }), null);
        pages["tools"] = ("Requisitos e sobre", ToolsPage, null);
    }

    private static Border CardBox(Control child, Thickness? padding = null) => new()
    {
        Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = padding ?? new Thickness(22, 18), Child = child
    };
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Wrapped(string name, string text, double size = 14, IBrush? brush = null)
    {
        var t = new TextBlock { Name = name, Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (brush is not null) t.Foreground = brush;
        return t;
    }
    private static ScrollViewer Scroll(string name, Control content) => new() { Name = name, Content = content };
    private static Button Primary(string name, string text) => new() { Name = name, Content = text, Background = Palette.Accent, Foreground = Brushes.Black, FontWeight = FontWeight.Bold, Padding = new Thickness(20, 12), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(12) };

    private static void ChipStyle(Button b, bool on) { b.Background = on ? Palette.Fg : Brushes.Transparent; b.Foreground = on ? Brushes.Black : Palette.Muted; }
    private static StackPanel Chips<T>(string name, (string Label, T Value)[] options, T initial, Action<T> chosen)
    {
        var panel = new StackPanel { Name = name, Orientation = Orientation.Horizontal, Spacing = 6 };
        var buttons = new List<Button>();
        foreach (var (label, value) in options)
        {
            var b = new Button { Content = label, FontSize = 11, Padding = new Thickness(14, 5), CornerRadius = new CornerRadius(16), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
            ChipStyle(b, EqualityComparer<T>.Default.Equals(value, initial)); buttons.Add(b);
            var chosenValue = value;
            b.Click += (_, _) => { foreach (var o in buttons) ChipStyle(o, ReferenceEquals(o, b)); chosen(chosenValue); };
            panel.Children.Add(b);
        }
        return panel;
    }

    private Control Overview()
    {
        int range = 300; string metric = "load";
        var chart = new UsageChart { Name = "OverviewChart", Height = 210, HorizontalAlignment = HorizontalAlignment.Stretch };
        var chartLabel = Label("CPU · CARGA");
        var hero = new TextBlock { Name = "OverviewValue", Text = "—", FontSize = 44, FontWeight = FontWeight.Bold, FontFamily = new FontFamily("monospace") };
        void Refresh()
        {
            bool temp = metric == "temp";
            chart.SetValues(temp ? usage.TempSeries : usage.CpuSeries, temp ? null : 100, range);
            chartLabel.Text = temp ? "CPU · TEMPERATURA" : "CPU · CARGA";
            hero.Text = temp ? (usage.Last?.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—") : UsageViewModel.Pct(usage.Last?.CpuPercent);
        }
        usage.Updated += Refresh;
        var ranges = Chips("DashRange", [("1 MIN", 60), ("5 MIN", 300), ("15 MIN", 900)], 300, v => { range = v; Refresh(); });
        var metrics = Chips("DashMetric", [("Temperatura", "temp"), ("Carga", "load")], "load", v => { metric = v; Refresh(); });
        var power = new Button { Name = "DashMetricPower", Content = "Consumo", FontSize = 11, Padding = new Thickness(14, 5), CornerRadius = new CornerRadius(16), IsEnabled = false };
        ToolTip.SetTip(power, "Indisponível: consumo da CPU (RAPL) ainda não é lido no Linux.");
        metrics.Children.Add(power);
        var chips = new Grid { Margin = new Thickness(0, 16, 0, 8) };
        metrics.HorizontalAlignment = HorizontalAlignment.Right; chips.Children.Add(ranges); chips.Children.Add(metrics);
        var gpuTag = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Children = { Label("GPU"), new TextBlock { Text = "—", FontSize = 22, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right } } };
        var top = new Grid { Children = { new StackPanel { Children = { chartLabel, hero } }, gpuTag } };
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Spacing = 6, Children = { new Border { Width = 14, Height = 2, Background = Palette.Accent, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "CPU (GPU: sem contadores de uso neste monitor)", FontSize = 11, Foreground = Palette.Muted } } };
        var chartCard = CardBox(new StackPanel { Children = { top, chips, chart, legend } }, new Thickness(24, 20));

        // Right card: reduced validations (no score), like the benchmark card of the original.
        var open = Primary("OpenValidations", "ABRIR VALIDAÇÕES");
        open.Click += (_, _) => Select("validate");
        var notice = new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = Brush.Parse("#3A3020"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Margin = new Thickness(0, 10, 0, 0), Child = new TextBlock { Name = "OverviewGpuNotice", Text = ValidationNotices.AmdNvidia, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap, FontSize = 12 } };
        var memoryRow = Wrapped("OverviewMemory", "—", 13, Palette.Muted); var procRow = Wrapped("OverviewProcesses", "—", 13, Palette.Muted); var cpuName = Wrapped("OverviewCpuName", "—", 12, Palette.Muted);
        usage.Updated += () => { memoryRow.Text = usage.MemoryLine; procRow.Text = usage.ProcessSummary; cpuName.Text = usage.CpuModel; };
        var inner = new Border { Background = Palette.Bg, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10), Margin = new Thickness(0, 14, 0, 0), Child = new StackPanel { Spacing = 4, Children = { Label("CPU"), cpuName } } };
        var side = CardBox(new StackPanel
        {
            Children =
            {
                Label("DESEMPENHO · VALIDAÇÕES REDUZIDAS"), new TextBlock { Text = "Sem score", FontSize = 30, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) },
                Wrapped("OverviewScoreNote", "CPU, memória e GameSim validam corretude em carga reduzida. Score Linux/Windows não é comparável.", 11, Palette.Faint),
                new Border { Height = 1, Background = Palette.Line, Margin = new Thickness(0, 14) }, Label("MEMÓRIA"), memoryRow, new Border { Height = 8 }, Label("PROCESSOS"), procRow, inner, notice,
                new Border { Height = 14 }, open
            }
        }, new Thickness(22, 20));
        var row1 = new Grid { ColumnDefinitions = new("*,16,330"), Margin = new Thickness(0, 6, 0, 16) };
        Grid.SetColumn(side, 2); row1.Children.Add(chartCard); row1.Children.Add(side);

        // Component table: COMPONENTE | TEMPERATURA | CARGA | CLOCK | CONSUMO / USO | VER
        var rows = new StackPanel { Name = "DashComponents" };
        var head = new Grid { ColumnDefinitions = new("2.2*,*,*,*,1.6*,56"), Margin = new Thickness(0, 4, 0, 10) };
        string[] titles = ["COMPONENTE", "TEMPERATURA", "CARGA", "CLOCK", "CONSUMO / USO", "VER"];
        for (int i = 0; i < titles.Length; i++) { var l = Label(titles[i]); Grid.SetColumn(l, i); if (i == 5) l.HorizontalAlignment = HorizontalAlignment.Right; head.Children.Add(l); }
        void Components()
        {
            rows.Children.Clear();
            var s = usage.Last;
            string mhz = s?.CpuMhz is { } m ? m.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "—";
            string tmp = s?.CpuTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "—";
            AddRow("CPU", s?.CpuModel ?? "", tmp, UsageViewModel.Pct(s?.CpuPercent), mhz, "—", "system", "GoCpu");
            AddRow("GPU", "Inventário DRM/PCI", "—", "—", "—", "sem contadores", "gpu", "GoGpu");
            AddRow("Memória", usage.MemorySub, "—", s is { MemoryTotal: > 0, MemoryUsed: { } u } ? UsageViewModel.Pct(100.0 * u / s.MemoryTotal.Value) : "—", "—", usage.MemoryLine, "system", "GoMemory");
            AddRow("Discos", "Dispositivos inteiros", "—", "—", "—", usage.DiskLine, "disks", "GoDisk");
            AddRow("Rede", usage.NetSub, "—", "—", "—", usage.NetLine, "net", "GoNet");
        }
        void AddRow(string name, string model, string temp, string load, string clock, string use, string target, string goName)
        {
            var g = new Grid { ColumnDefinitions = new("2.2*,*,*,*,1.6*,56") };
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = name, FontSize = 13 }, new TextBlock { Text = model, FontSize = 11, Foreground = Palette.Faint, TextTrimming = TextTrimming.CharacterEllipsis } } };
            g.Children.Add(title);
            void Cell(int col, string text, bool muted) { var t = new TextBlock { Text = text, FontFamily = new FontFamily("monospace"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; if (muted) t.Foreground = Palette.Muted; Grid.SetColumn(t, col); g.Children.Add(t); }
            Cell(1, temp, false); Cell(2, load, false); Cell(3, clock, true); Cell(4, use, true);
            var go = new Button { Name = goName, Content = "›", Width = 30, Height = 30, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent };
            AutomationPropertiesName(go, "Abrir " + name);
            go.Click += (_, _) => Select(target);
            Grid.SetColumn(go, 5); g.Children.Add(go);
            rows.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 9), Child = g });
        }
        usage.Updated += Components; Components();
        var table = CardBox(new StackPanel { Children = { head, rows } }, new Thickness(22, 14, 22, 8));
        return Scroll("OverviewScroll", new StackPanel { Margin = new Thickness(0, 0, 8, 12), Children = { row1, table } });
    }

    private static void AutomationPropertiesName(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);

    private Control SystemPage() => new SystemInfoPage(hardware, Destination("hardware"), stop.Token) { Margin = new Thickness(0, 4, 0, 0) };

    private Control GpuPage() => new GpuInfoPage(stop.Token) { Margin = new Thickness(0, 4, 0, 0) };

    private Control DisksPage() => Scroll("DisksScroll", new DiskHealthPage(smartImport, Destination("smart-import"), stop.Token) { Margin = new Thickness(0, 4, 8, 12) });

    private Control EnergyPage() => Scroll("EnergyScroll", new Ilmenyx.Desktop.EnergyPage(sensors, stop.Token) { Margin = new Thickness(0, 4, 8, 12) });

    private Control NetPage()
    {
        var chart = new UsageChart { Name = "NetChart", Height = 210 };
        var line = Wrapped("NetLine", usage.NetLine, 16); var sub = Wrapped("NetSub", usage.NetSub, 12, Palette.Muted);
        usage.Updated += () => { chart.SetValues(usage.NetSeries, null, 300); line.Text = usage.NetLine; sub.Text = usage.NetSub; };
        return Scroll("NetScroll", new StackPanel { Spacing = 12, Children = { CardBox(new StackPanel { Spacing = 10, Children = { Label("REDE · ÚLTIMOS 5 MIN (SOMA DAS INTERFACES ATIVAS)"), chart, line, sub }, }) } });
    }

    private Control ValidatePage()
    {
        cpuPanel = new CpuValidationPanel(cpu, Destination("cpu"), stop.Token);
        return Scroll("ValidateScroll", new StackPanel
        {
            Spacing = 12,
            Children = { cpuPanel, new MemoryValidationPanel(memory, Destination("memory"), stop.Token), new GameSimValidationPanel(simulation, Destination("sim"), stop.Token) }
        });
    }

    private Control ToolsPage()
    {
        var requirements = new Button { Name = "ShowRequirements", Content = "Requisitos" };
        var about = new Button { Name = "About", Content = "Licenças e créditos" };
        var classicButton = new Button { Name = "OpenClassic", Content = "Painel clássico de diagnósticos" };
        requirements.Click += async (_, _) => { await new RequirementsDialog().ShowDialog(this); if (IsVisible) requirements.Focus(); };
        about.Click += async (_, _) => { await new AboutDialog().ShowDialog(this); if (IsVisible) about.Focus(); };
        classicButton.Click += (_, _) => OpenClassicDiagnostics();
        return Scroll("ToolsScroll", new StackPanel
        {
            Spacing = 12,
            Children = { CardBox(new StackPanel { Spacing = 12, Children = { Label("FERRAMENTAS"), new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Children = { requirements, about, classicButton } },
                Wrapped("ToolsNote", ProductIdentity.Attribution, 12, Palette.Faint) } }) }
        });
    }
}
