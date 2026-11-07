// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions (layout follows the original System page: category list, detail card, drive rings; machine hero 2026-11-06): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class SystemInfoPage : Grid
{
    private readonly LinuxSystemInfoCollector collector = new();
    private readonly PciIds pciIds = PciIds.Load();
    private readonly DashboardViewModel hardware;
    private readonly Func<string, Task<string?>> destination;
    private readonly CancellationToken lifetime;
    private readonly StackPanel details = new() { Name = "SysDetails" };
    private readonly StackPanel drives = new() { Name = "SysDrives", IsVisible = false };
    private readonly StackPanel importBox = new() { Name = "SysRamImport", IsVisible = false, Spacing = 8, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBlock title = new() { Name = "SysInfoTitle", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock sub = new() { Name = "SysStatus", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock machine = new() { Name = "SysMachine", Text = "Este computador", FontSize = 26, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock machineSub = new() { Name = "SysMachineSub", FontSize = 13, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel machineTags = new() { Name = "SysTags", Margin = new Thickness(0, 12, 0, 0) };
    private readonly Dictionary<string, Button> categoryButtons = new();
    // Responsive columns: at the window minimum the content area is ~640 px, so the category list and the label column shrink.
    private readonly ColumnDefinition categoryColumn = new(220, GridUnitType.Pixel);
    private readonly List<ColumnDefinition> labelColumns = [];
    private double labelWidth = 240;
    private readonly UniformGrid driveGrid = new() { Columns = 2 };
    private IReadOnlyList<InfoNode> nodes = [];
    private MemoryInventory? ram;
    private string selected = "";

    public SystemInfoPage(DashboardViewModel hardware, Func<string, Task<string?>> destination, CancellationToken lifetime)
    {
        this.hardware = hardware; this.destination = destination; this.lifetime = lifetime;
        var export = new Button { Name = "SysExportJson", Content = "Exportar JSON" };
        var exportHtml = new Button { Name = "SysExportHtml", Content = "Exportar relatório HTML" };
        var refresh = UiKit.Primary("SysRefresh", "Atualizar");
        ToolTip.SetTip(exportHtml, "Salva um relatório com sistema, discos, GPU e sensores. Números de série são omitidos.");
        var buttons = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 0), Children = { refresh, export, exportHtml } };
        var header = UiKit.PageHeader("Sistema", "Hardware e software detectados", buttons);
        if (header.Children[0] is StackPanel { Children.Count: > 1 } headerText) headerText.Children[1].Name = "SysSub";
        refresh.Click += async (_, _) => { refresh.IsEnabled = false; try { await hardware.RefreshAsync(lifetime); await LoadAsync(); } finally { refresh.IsEnabled = true; } };
        export.Click += async (_, _) => await ExportAsync(false, export, exportHtml);
        exportHtml.Click += async (_, _) => await ExportAsync(true, export, exportHtml);

        var heroText = new StackPanel { Children = { UiKit.Label("ESTE COMPUTADOR"), machine, machineSub, machineTags, sub } };
        heroText.Children[0].Margin = new Thickness(0, 0, 0, 6);
        var heroGrid = new Grid { ColumnDefinitions = new("*,Auto"), Children = { heroText } };
        var mark = UiKit.Icon(Icons.System, 84, Palette.Fg); mark.Opacity = 0.12; mark.VerticalAlignment = VerticalAlignment.Top; mark.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(mark, 1); heroGrid.Children.Add(mark);
        var hero = UiKit.Hero(heroGrid, name: "SysHero"); hero.Height = double.NaN; hero.MinHeight = 170; FollowTheme(hero); hero.Margin = new Thickness(0, 0, 0, 18);

        var categories = new StackPanel { Name = "SysCategories", Spacing = 2 };
        var body = new Grid { ColumnDefinitions = { categoryColumn, new ColumnDefinition(18, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star) } };
        body.Children.Add(categories);
        var card = UiKit.Card(new StackPanel { Children = { title, importBox, drives, details } }, new Thickness(0));
        card.Padding = new Thickness(22, 18, 22, 18); card.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(card, 2); body.Children.Add(card);
        var page = new StackPanel { Margin = new Thickness(0, 0, 10, 16), Children = { header, hero, body } };
        page.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        Children.Add(new ScrollViewer { Name = "SysInfoScroll", Content = page });
        BuildImportBox();
        Loaded += async (_, _) => { if (nodes.Count == 0) await LoadAsync(); };
        void MakeCategories()
        {
            categories.Children.Clear(); categoryButtons.Clear();
            foreach (var node in nodes)
            {
                var icon = UiKit.Icon(CategoryIcon(node.Header), 16, Palette.Faint);
                var label = new TextBlock { Text = node.Header, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                var content = new Grid { ColumnDefinitions = new("Auto,*"), Children = { icon, label } }; Grid.SetColumn(label, 1); label.Margin = new Thickness(10, 0, 0, 0);
                var b = new Button { Name = "SysCat" + node.Header.Split(' ')[0], Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Background = Brushes.Transparent, Foreground = Palette.Muted, FontSize = 13 };
                AutomationProperties.SetName(b, node.Header);
                string header = node.Header;
                b.Click += (_, _) => Show(header);
                categoryButtons[header] = b; categories.Children.Add(b);
            }
        }
        CategoriesChanged = MakeCategories;
    }

    /// <summary>The hero fill is built once; rebuild it on a theme switch so text stays legible in light and dark.</summary>
    private static void FollowTheme(Border hero)
    {
        hero.ActualThemeVariantChanged += (_, _) =>
        {
            hero.Background = UiKit.HeroBrush();
            if (hero.Child is Grid { Children: [Border overlay, ..] }) overlay.Opacity = Palette.IsLight ? 0 : 1;
        };
    }

    private static string CategoryIcon(string header) => header.Split(' ')[0] switch
    {
        "Sistema" => Icons.System, "Placa-mãe" => Icons.Drivers, "Processador" => Icons.Cpu, "Memória" => Icons.Memory, "Placa" => Icons.Gpu, "Armazenamento" => Icons.Disks, _ => Icons.Overview,
    };

    /// <summary>Narrow content: thinner category list and label column, drive cards in one column.</summary>
    private void Fit(double width)
    {
        bool narrow = width < 820;
        categoryColumn.Width = new GridLength(narrow ? 168 : 220);
        labelWidth = narrow ? 140 : 240;
        foreach (var c in labelColumns) c.Width = new GridLength(labelWidth);
        driveGrid.Columns = narrow ? 1 : 2;
        machine.FontSize = narrow ? 22 : 26;
    }

    /// <summary>Machine summary for the hero, from the same nodes the detail card shows (no extra reads, no serial numbers).</summary>
    private void FillHero()
    {
        InfoNode? Root(string prefix) => nodes.FirstOrDefault(n => n.Header.StartsWith(prefix, StringComparison.Ordinal));
        static string? Leaf(InfoNode? root, string header)
        {
            if (root is null) return null;
            foreach (var c in root.Children)
            {
                if (!c.IsGroup && c.Header == header) return string.IsNullOrWhiteSpace(c.Value) || c.Value == "—" ? null : c.Value;
                if (c.IsGroup && Leaf(c, header) is { } v) return v;
            }
            return null;
        }
        static string? Short(string? v) => v is null ? null : v.Length <= 40 ? v : v[..38].TrimEnd() + "…";
        var board = Root("Placa-mãe"); var equipment = board?.Children.FirstOrDefault(c => c.Header.StartsWith("Sistema", StringComparison.Ordinal));
        string? product = Leaf(equipment, "Produto")?.Split('_')[0], maker = Leaf(equipment, "Fabricante");
        machine.Text = product is not null ? (maker is not null && !product.Contains(maker.Split(' ')[0], StringComparison.OrdinalIgnoreCase) ? maker.Split(' ')[0] + " " + product : product) : Leaf(board, "Modelo") ?? "Este computador";
        var os = Root("Sistema operacional");
        machineSub.Text = string.Join("  ·  ", new[] { Leaf(os, "Nome"), Leaf(os, "Kernel") is { } k ? "Linux " + k : null, Leaf(os, "Arquitetura") }.Where(t => t is not null));
        var cpu = Root("Processador"); var gpu = Root("Placa de vídeo"); var disks = Root("Armazenamento");
        var tags = new[]
        {
            Short(cpu?.Children.FirstOrDefault(c => c.IsGroup)?.Header), Leaf(cpu, "Threads (lógicos)") is { } th ? th + " threads" : null,
            Leaf(Root("Memória"), "Total visível ao sistema") is { } mem ? mem + " de RAM" : null,
            Short(gpu?.Children.FirstOrDefault(c => c.IsGroup)?.Header),
            disks?.Children.Count(c => c.IsGroup) is > 0 and var n ? $"{n} disco(s)" : null,
        };
        machineTags.Children.Clear();
        foreach (var t in tags) if (t is not null) machineTags.Children.Add(UiKit.Tag(t));
    }

    private Action CategoriesChanged = () => { };

    public IReadOnlyList<InfoNode> Nodes => nodes;
    public string Selected => selected;

    public async Task LoadAsync()
    {
        sub.Text = "Lendo /proc, /sys e /etc (somente leitura)…";
        try
        {
            var gpus = hardware.Snapshot?.Gpus; var inventory = ram;
            nodes = await Task.Run(() => collector.Collect(inventory, gpus, pciIds), lifetime);
            CategoriesChanged(); FillHero();
            Show(selected is { Length: > 0 } && nodes.Any(n => n.Header == selected) ? selected : nodes[0].Header);
            sub.Text = $"Leitura concluída em {DateTime.Now:T}. Números de série ficam de fora.";
        }
        catch (OperationCanceledException) { sub.Text = "Leitura cancelada."; }
    }

    public void Show(string header)
    {
        var node = nodes.FirstOrDefault(n => n.Header == header); if (node is null) return;
        selected = header; title.Text = header; details.Children.Clear(); labelColumns.Clear();
        foreach (var (key, button) in categoryButtons) { bool on = key == header; button.Background = on ? Palette.Card : Brushes.Transparent; button.Foreground = on ? Palette.Fg : Palette.Muted; button.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; if (button.Content is Grid { Children: [Avalonia.Controls.Shapes.Path icon, ..] }) icon.Fill = on ? Palette.AccentText : Palette.Faint; }
        drives.IsVisible = header == "Armazenamento"; importBox.IsVisible = header == "Memória RAM";
        if (drives.IsVisible) FillDrives(node);
        Render(details, node.Children, 0);
    }

    private void Render(StackPanel host, IEnumerable<InfoNode> items, int depth)
    {
        foreach (var item in items)
        {
            if (item.IsGroup)
            {
                host.Children.Add(new TextBlock { Text = item.Header, FontWeight = FontWeight.SemiBold, FontSize = 13.5, Margin = new Thickness(depth * 14, 16, 0, 6), TextWrapping = TextWrapping.Wrap });
                Render(host, item.Children, depth + 1);
            }
            else
            {
                var labelColumn = new ColumnDefinition(labelWidth, GridUnitType.Pixel); labelColumns.Add(labelColumn);
                var row = new Grid { ColumnDefinitions = { labelColumn, new ColumnDefinition(1, GridUnitType.Star) }, Margin = new Thickness(depth * 14, 0, 0, 0) };
                row.Children.Add(new TextBlock { Text = item.Header, Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 12, 6) });
                var value = new TextBlock { Text = item.Value, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6) }; Grid.SetColumn(value, 1); row.Children.Add(value);
                host.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = row });
            }
        }
    }

    private void FillDrives(InfoNode storage)
    {
        drives.Children.Clear();
        var cards = MountedDrives.Read(SafeMounts());
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(UiKit.Label("UNIDADES MONTADAS"));
        head.Children.Add(new TextBlock { Name = "SysDrivesSummary", Text = cards.Count == 0 ? "nenhuma unidade legível" : $"{cards.Count} unidade(s)", Foreground = Palette.Muted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
        drives.Children.Add(head);
        var grid = driveGrid; grid.Children.Clear();
        foreach (var d in cards)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = d.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = d.Sizes, Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = d.Meta, Foreground = Palette.Faint, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap } } };
            var ring = new Grid { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Children = { new RingGauge(d.UsedFraction), new TextBlock { Text = d.UsedPercent, FontFamily = IlmenyxTheme.Mono, FontSize = 14, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
            var cell = new Grid { ColumnDefinitions = new("Auto,*") }; Grid.SetColumn(text, 1); cell.Children.Add(ring); cell.Children.Add(text);
            grid.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(16, 14), Margin = new Thickness(0, 0, 10, 10), Background = Palette.Card, Child = cell });
        }
        drives.Children.Add(grid);
        var reread = new Button { Name = "SysRefreshDisks", Content = "Reler discos", FontSize = 12, Padding = new Thickness(12, 5), Margin = new Thickness(0, 6, 0, 8) };
        reread.Click += (_, _) => { collector.Disks(storage); Show("Armazenamento"); };
        drives.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { new TextBlock { Text = "DISCOS FÍSICOS", FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center }, reread } });
    }

    private static string SafeMounts()
    {
        try { return File.ReadAllText("/proc/self/mounts"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    private void BuildImportBox()
    {
        var path = new TextBox { Name = "SysDmiPath", PlaceholderText = "/pasta/dmidecode-memoria.txt" };
        var label = new TextBlock { Text = "Importar `sudo dmidecode -t memory > arquivo.txt` (caminho completo; o Ilmenyx não executa dmidecode)", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
        AutomationProperties.SetName(path, "Caminho do arquivo com a saída do dmidecode"); AutomationProperties.SetLabeledBy(path, label);
        var import = new Button { Name = "SysImportDmi", Content = "Importar módulos de RAM" };
        var status = new TextBlock { Name = "SysImportStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        import.Click += async (_, _) =>
        {
            import.IsEnabled = false;
            try
            {
                string text = await SafeFileReader.ReadTextAsync(path.Text ?? "", DmiDecodeParser.MaxCharacters, lifetime);
                var inventory = DmiDecodeParser.Parse(text);
                if (inventory.Modules.Count == 0) { status.Text = "Nenhum módulo de memória encontrado no arquivo; inventário anterior preservado."; return; }
                ram = inventory; status.Text = $"{inventory.Modules.Count} módulo(s) importado(s); números de série ignorados.";
                await LoadAsync();
            }
            catch (OperationCanceledException) { status.Text = "Importação cancelada."; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException) { status.Text = "Importação recusada: " + e.Message; }
            finally { import.IsEnabled = true; if (IsEffectivelyVisible && !lifetime.IsCancellationRequested) import.Focus(); }
        };
        importBox.Children.Add(label); importBox.Children.Add(path); importBox.Children.Add(import); importBox.Children.Add(status);
    }

    private async Task ExportAsync(bool html, Button export, Button exportHtml)
    {
        if (hardware.Snapshot is not { } snapshot) { sub.Text = "Atualize os dados antes de exportar."; return; }
        export.IsEnabled = false; exportHtml.IsEnabled = false;
        try
        {
            string extension = html ? ".html" : ".json";
            var path = await destination(extension);
            if (path is null) return;
            if (html) await Task.Run(() => HardwareHtmlReport.ExportAsync(snapshot, path, lifetime), lifetime);
            else await SnapshotReport.ExportAsync(snapshot, path, lifetime);
            sub.Text = "Snapshot exportado para arquivo local.";
        }
        catch (OperationCanceledException) { sub.Text = "Exportação cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { sub.Text = "Não foi possível exportar. Escolha um nome novo: " + e.Message; }
        finally { export.IsEnabled = true; exportHtml.IsEnabled = true; if (IsEffectivelyVisible && !lifetime.IsCancellationRequested) (html ? exportHtml : export).Focus(); }
    }
}
