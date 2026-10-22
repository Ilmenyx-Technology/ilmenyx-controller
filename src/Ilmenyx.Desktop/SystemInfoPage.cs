// New Ilmenyx additions (layout follows the original System page: category list, detail card, drive rings): see LICENSING.md.
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
    private readonly TextBlock title = new() { Name = "SysInfoTitle", FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 8) };
    private readonly TextBlock sub = new() { Name = "SysStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, Button> categoryButtons = new();
    private IReadOnlyList<InfoNode> nodes = [];
    private MemoryInventory? ram;
    private string selected = "";

    public SystemInfoPage(DashboardViewModel hardware, Func<string, Task<string?>> destination, CancellationToken lifetime)
    {
        this.hardware = hardware; this.destination = destination; this.lifetime = lifetime;
        RowDefinitions = new("Auto,*");
        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 360, 0), Children = { new TextBlock { Text = "Sistema", FontSize = 26, FontWeight = FontWeight.Bold }, new TextBlock { Name = "SysSub", Text = "Hardware e software detectados", Foreground = Palette.Muted } } });
        var export = new Button { Name = "SysExportJson", Content = "Exportar JSON" };
        var exportHtml = new Button { Name = "SysExportHtml", Content = "Exportar relatório HTML" };
        var refresh = new Button { Name = "SysRefresh", Content = "Atualizar" };
        ToolTip.SetTip(exportHtml, "Salva um relatório com sistema, discos, GPU e sensores. Números de série são omitidos.");
        var buttons = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Children = { refresh, export, exportHtml } };
        header.Children.Add(buttons);
        refresh.Click += async (_, _) => { refresh.IsEnabled = false; try { await hardware.RefreshAsync(lifetime); await LoadAsync(); } finally { refresh.IsEnabled = true; } };
        export.Click += async (_, _) => await ExportAsync(false, export, exportHtml);
        exportHtml.Click += async (_, _) => await ExportAsync(true, export, exportHtml);

        var categories = new StackPanel { Name = "SysCategories", Spacing = 2 };
        var body = new Grid { ColumnDefinitions = new("220,24,*") };
        body.Children.Add(categories);
        var card = new Border
        {
            Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(26, 8, 26, 22),
            Child = new ScrollViewer { Name = "SysInfoScroll", Content = new StackPanel { Children = { title, sub, importBox, drives, details } } }
        };
        Grid.SetColumn(card, 2); body.Children.Add(card);
        Grid.SetRow(body, 1); Children.Add(header); Children.Add(body);
        BuildImportBox();
        Loaded += async (_, _) => { if (nodes.Count == 0) await LoadAsync(); };
        void MakeCategories()
        {
            categories.Children.Clear(); categoryButtons.Clear();
            foreach (var node in nodes)
            {
                var b = new Button { Name = "SysCat" + node.Header.Split(' ')[0], Content = node.Header, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 9), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Foreground = Palette.Muted };
                string header = node.Header;
                b.Click += (_, _) => Show(header);
                categoryButtons[header] = b; categories.Children.Add(b);
            }
        }
        CategoriesChanged = MakeCategories;
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
            CategoriesChanged();
            Show(selected is { Length: > 0 } && nodes.Any(n => n.Header == selected) ? selected : nodes[0].Header);
            sub.Text = $"Leitura concluída em {DateTime.Now:T}. Números de série ficam de fora.";
        }
        catch (OperationCanceledException) { sub.Text = "Leitura cancelada."; }
    }

    public void Show(string header)
    {
        var node = nodes.FirstOrDefault(n => n.Header == header); if (node is null) return;
        selected = header; title.Text = header; details.Children.Clear();
        foreach (var (key, button) in categoryButtons) { bool on = key == header; button.Background = on ? Palette.Card : Brushes.Transparent; button.Foreground = on ? Palette.Fg : Palette.Muted; button.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; }
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
                host.Children.Add(new TextBlock { Text = item.Header, FontWeight = FontWeight.SemiBold, Margin = new Thickness(depth * 16, 14, 0, 4), TextWrapping = TextWrapping.Wrap });
                Render(host, item.Children, depth + 1);
            }
            else
            {
                var row = new Grid { ColumnDefinitions = new("260,*"), Margin = new Thickness(depth * 16, 0, 0, 0) };
                row.Children.Add(new TextBlock { Text = item.Header, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 12, 6) });
                var value = new TextBlock { Text = item.Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6) }; Grid.SetColumn(value, 1); row.Children.Add(value);
                host.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = row });
            }
        }
    }

    private void FillDrives(InfoNode storage)
    {
        drives.Children.Clear();
        var cards = MountedDrives.Read(SafeMounts());
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new TextBlock { Text = "UNIDADES MONTADAS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint });
        head.Children.Add(new TextBlock { Name = "SysDrivesSummary", Text = cards.Count == 0 ? "nenhuma unidade legível" : $"{cards.Count} unidade(s)", Foreground = Palette.Muted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
        drives.Children.Add(head);
        var grid = new UniformGrid { Columns = 2 };
        foreach (var d in cards)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = d.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = d.Sizes, Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = d.Meta, Foreground = Palette.Faint, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap } } };
            var ring = new Grid { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Children = { new RingGauge(d.UsedFraction), new TextBlock { Text = d.UsedPercent, FontFamily = new FontFamily("monospace"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
            var cell = new Grid { ColumnDefinitions = new("Auto,*") }; Grid.SetColumn(text, 1); cell.Children.Add(ring); cell.Children.Add(text);
            grid.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 14), Margin = new Thickness(0, 0, 10, 10), Background = Palette.Bg, Child = cell });
        }
        drives.Children.Add(grid);
        var reread = new Button { Name = "SysRefreshDisks", Content = "Reler discos", Margin = new Thickness(0, 0, 0, 8) };
        reread.Click += (_, _) => { collector.Disks(storage); Show("Armazenamento"); };
        drives.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { new TextBlock { Text = "DISCOS FÍSICOS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center }, reread } });
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
