// New Ilmenyx additions (layout follows the original RESET DRIVER page; install/update actions adapted to Linux): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class DriverPage : StackPanel
{
    private readonly LinuxDriverInventory inventory;
    private readonly Action<DriverTools.DriverManager> launchManager;
    private readonly Func<string, string?> findManager;
    private readonly PciIds pciIds = PciIds.Load();
    private readonly Func<string, Task<string?>> destination;
    private readonly CancellationToken lifetime;
    private readonly TextBlock status = new() { Name = "DrvStatus", Text = "Clique em VERIFICAR TUDO.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly StackPanel gpuList = new() { Name = "DrvGpus", Spacing = 14 };
    private readonly TextBlock board = new() { Name = "DrvBoard", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button boardPage = new() { Name = "BtnDrvBoardPage", Content = "ABRIR SUPORTE DO FABRICANTE", IsEnabled = false, Margin = new Thickness(0, 14, 0, 8) };
    private readonly StackPanel fwList = new() { Name = "DrvFirmware", Spacing = 4 };
    private readonly StackPanel list = new() { Name = "DrvList", Margin = new Thickness(0, 10, 0, 0) };
    private IReadOnlyList<GpuDriverStatus> gpus = [];
    private IReadOnlyList<DriverRow> rows = [];
    private BoardInfo boardInfo = new();
    private bool busy;
    public IReadOnlyList<DriverRow> Rows => rows;

    public DriverPage(Func<string, Task<string?>> destination, CancellationToken lifetime, LinuxDriverInventory? inventory = null, Action<DriverTools.DriverManager>? launchManager = null, Func<string, string?>? findManager = null)
    {
        this.inventory = inventory ?? new();
        this.launchManager = launchManager ?? DriverTools.LaunchDriverManager;
        this.findManager = findManager ?? BoundedProcess.FindSystem;
        this.destination = destination; this.lifetime = lifetime; Spacing = 4;
        var scan = new Button { Name = "BtnDrvScan", Content = "VERIFICAR TUDO", Background = Palette.Accent, Foreground = Palette.OnAccent, FontWeight = FontWeight.Bold, Padding = new Thickness(20, 9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        scan.Click += async (_, _) => await ScanAsync();
        var header = UiKit.PageHeader("Drivers", "Inventário do kernel e dos módulos, versão da NVIDIA (consulta online só por clique), suporte do fabricante e firmware (fwupd). Nada é instalado pelo Ilmenyx.");
        header.RowDefinitions = new("Auto,Auto");
        Grid.SetRow(scan, 1); scan.Margin = new Thickness(0, 10, 0, 0);
        header.Children.Add(scan);
        Children.Add(header); Children.Add(status);
        var gpuCard = UiKit.FeatureCard(new StackPanel { Spacing = 8, Children = { UiKit.Label("PLACA DE VÍDEO"), gpuList } });
        gpuCard.Margin = new Thickness(0, 0, 0, 14);
        boardPage.Click += async (_, _) => await OpenAsync(boardInfo.SupportUrl);
        var boardCard = Card("PLACA-MÃE / FABRICANTE", new StackPanel { Children = { board, boardPage, Note("Fabricantes de placa-mãe não têm API pública de versões. Abrimos o suporte oficial do fabricante detectado (chipset, áudio, rede, BIOS). Se a página não abrir no seu modelo, digite o modelo mostrado acima na busca do site.") } });
        var featured = new Grid { ColumnDefinitions = new("1.4*,16,*"), RowDefinitions = new("Auto,Auto") };
        featured.Children.Add(gpuCard); featured.Children.Add(boardCard);
        featured.SizeChanged += (_, _) =>
        {
            bool wide = featured.Bounds.Width >= 920;
            Grid.SetColumnSpan(gpuCard, wide ? 1 : 3);
            Grid.SetColumn(boardCard, wide ? 2 : 0); Grid.SetColumnSpan(boardCard, wide ? 1 : 3);
            Grid.SetRow(boardCard, wide ? 0 : 1);
        };
        Children.Add(featured);
        var fw = new Button { Name = "BtnDrvFwupd", Content = "PROCURAR FIRMWARE (fwupd)", Margin = new Thickness(0, 14, 0, 8) };
        fw.Click += async (_, _) => await FirmwareAsync(fw);
        Children.Add(Card("FIRMWARE (LVFS / FWUPD)", new StackPanel { Children = { fwList, fw, Note("Lista os dispositivos atualizáveis e suas versões (somente leitura). Para atualizar execute `fwupdmgr update` no terminal; o Ilmenyx não instala firmware nem pede privilégios.") } }));
        var backup = new Button { Name = "BtnDrvBackup", Content = "EXPORTAR INVENTÁRIO (JSON)", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        backup.Click += async (_, _) => await ExportAsync(backup);
        Children.Add(Card("BACKUP DO INVENTÁRIO", new StackPanel { Children = { new TextBlock { Text = "Salva a lista de dispositivos e drivers do kernel em um arquivo novo. No Linux os drivers fazem parte do kernel e dos pacotes da distribuição; não há binários para exportar.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }, backup } }));
        Children.Add(Card("DRIVERS INSTALADOS", list));
        Loaded += async (_, _) => { if (rows.Count == 0) await ScanAsync(); };
    }

    private static TextBlock Note(string text) => new() { Text = text, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private static Border Card(string title, Control content) => new()
    {
        Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(22, 18), Margin = new Thickness(0, 0, 0, 14),
        Child = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint }, content } }
    };

    public async Task ScanAsync()
    {
        if (busy) return;
        busy = true; status.Text = "Lendo dispositivos e drivers (sysfs, somente leitura)…";
        try
        {
            var (inv, g, b, distro) = await Task.Run(() => (inventory.Inventory(pciIds), inventory.Gpus(pciIds), inventory.Board(), inventory.Distro()), lifetime);
            rows = inv; gpus = g; boardInfo = b;
            Fill(distro);
            status.Text = $"Pronto às {DateTime.Now:HH:mm:ss}. {inv.Count} dispositivo(s) com driver listado.";
        }
        catch (OperationCanceledException) { status.Text = "Leitura cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Falhou: " + e.Message; }
        finally { busy = false; }
    }

    private void Fill((string Id, string IdLike) distro)
    {
        gpuList.Children.Clear();
        if (gpus.Count == 0) gpuList.Children.Add(new TextBlock { Text = "Nenhuma GPU PCI encontrada.", Foreground = Palette.Muted, Margin = new Thickness(0, 10, 0, 0) });
        foreach (var g in gpus)
        {
            if (g.Vendor == "NVIDIA" && g.Driver == "nouveau")
            {
                var manager = DriverTools.FindDriverManager(distro.Id, distro.IdLike, findManager);
                var open = new Button { Name = "BtnDrvManager", Content = "ABRIR GERENCIADOR DE DRIVERS", IsEnabled = manager is not null, HorizontalAlignment = HorizontalAlignment.Left };
                open.Click += (_, _) =>
                {
                    if (lifetime.IsCancellationRequested || manager is null) return;
                    try { launchManager(manager); status.Text = "Gerenciador de drivers aberto. Escolha o driver recomendado pela distribuição."; }
                    catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or ArgumentException or UnauthorizedAccessException) { status.Text = "Não foi possível abrir o gerenciador: " + e.Message; }
                };
                gpuList.Children.Add(Card("Driver NVIDIA recomendado", new StackPanel { Spacing = 8, Children =
                {
                    Note(GpuTelemetrySensors.NouveauReason), open,
                    Note(manager is null ? DriverInstallHints.For(distro.Id, distro.IdLike, g.Vendor) : "Abre a ferramenta da distribuição; o Ilmenyx não instala nem eleva privilégios.")
                } }));
            }
            var info = new TextBlock { Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            void Refresh() => info.Text = $"Instalado: {(g.Installed.Length > 0 ? g.Installed : "—")}  ·  Mais recente: {(g.Latest.Length > 0 ? g.Latest : "não consultado")}  ·  Driver: {g.Driver}";
            Refresh();
            var message = new TextBlock { Text = g.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            var hint = new TextBox { Name = "DrvInstallHint", Text = DriverInstallHints.For(distro.Id, distro.IdLike, g.Vendor), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            var buttons = new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            if (g.Vendor == "NVIDIA")
            {
                var latest = new Button { Name = "BtnDrvNvidiaLatest", Content = "VERIFICAR VERSÃO MAIS RECENTE (rede)" };
                ToolTip.SetTip(latest, "Consulta " + DriverTools.NvidiaLatestUrl + " (somente ao clicar).");
                latest.Click += async (_, _) =>
                {
                    latest.IsEnabled = false; status.Text = "Consultando " + DriverTools.NvidiaLatestUrl + "…";
                    try
                    {
                        string? v = await DriverTools.NvidiaLatestAsync(lifetime);
                        if (v is null) { message.Text = "A resposta da NVIDIA não tem o formato esperado."; }
                        else { g.Latest = v; Refresh(); message.Text = g.UpdateAvailable ? $"Há uma versão mais recente ({v}). Atualize pelo gerenciador de pacotes da distribuição." : "Você está na versão mais recente publicada pela NVIDIA (ou mais nova)."; }
                        status.Text = "Consulta concluída.";
                    }
                    catch (OperationCanceledException) { status.Text = "Consulta cancelada."; }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { message.Text = "Não foi possível consultar a NVIDIA: " + e.Message; status.Text = "Consulta falhou."; }
                    finally { latest.IsEnabled = true; }
                };
                buttons.Children.Add(latest);
            }
            var page = new Button { Name = "BtnDrvGpuPage", Content = "ABRIR PÁGINA OFICIAL", IsEnabled = g.OfficialPage.Length > 0 };
            page.Click += async (_, _) => await OpenAsync(g.OfficialPage);
            var copy = new Button { Name = "BtnDrvCopyHint", Content = "COPIAR COMANDO SUGERIDO" };
            copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(hint.Text ?? ""); status.Text = "Comando copiado; execute-o no terminal por conta própria."; } };
            buttons.Children.Add(page); buttons.Children.Add(copy);
            gpuList.Children.Add(new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { new TextBlock { Text = g.GpuName, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, info, message, hint, buttons } });
        }
        board.Text = $"{boardInfo.Maker} {boardInfo.Model}\nBIOS: {(boardInfo.Bios.Length > 0 ? boardInfo.Bios : "—")}" + (boardInfo.Brand.Length > 0 ? $"\nFabricante reconhecido: {boardInfo.Brand}" : "\nFabricante não reconhecido");
        boardPage.IsEnabled = boardInfo.SupportUrl.Length > 0;
        list.Children.Clear();
        foreach (var r in rows)
        {
            var left = new StackPanel { Children = { new TextBlock { Text = r.Device, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = $"{r.Class} · {r.Vendor}", Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap } } };
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0), Children = { new TextBlock { Text = r.Driver, FontFamily = new FontFamily("monospace"), HorizontalAlignment = HorizontalAlignment.Right }, new TextBlock { Text = $"{r.Version} · {r.Flags}", Foreground = Palette.Muted, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap } } };
            var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 0, 0, 8) }; Grid.SetColumn(right, 1); row.Children.Add(left); row.Children.Add(right);
            list.Children.Add(row);
        }
    }

    private async Task OpenAsync(string url)
    {
        try { await DriverTools.OpenUrlAsync(url, lifetime); status.Text = "Página aberta no navegador: " + url; }
        catch (Exception e) when (e is ArgumentException or FileNotFoundException or IOException or TimeoutException) { status.Text = "Não foi possível abrir a página (" + e.Message + "). Endereço: " + url; }
    }

    private async Task FirmwareAsync(Button button)
    {
        button.IsEnabled = false; status.Text = "Consultando o fwupd (somente leitura, 10 s)…";
        try
        {
            var items = await DriverTools.FwupdDevicesAsync(lifetime);
            fwList.Children.Clear();
            foreach (var i in items)
                fwList.Children.Add(new TextBlock { Text = $"{i.Name} · {i.Version}" + (i.Update.Length > 0 ? $"  →  atualização {i.Update} disponível" : ""), TextWrapping = TextWrapping.Wrap });
            status.Text = items.Count == 0 ? "O fwupd não listou dispositivos atualizáveis." : $"{items.Count} dispositivo(s) no fwupd.";
        }
        catch (OperationCanceledException) { status.Text = "Consulta cancelada."; }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception) { status.Text = "fwupd indisponível: " + e.Message; }
        finally { button.IsEnabled = true; }
    }

    private async Task ExportAsync(Button button)
    {
        if (rows.Count == 0) { status.Text = "Verifique os drivers antes de exportar."; return; }
        button.IsEnabled = false;
        try
        {
            var path = await destination(".json");
            if (path is null) return;
            await SnapshotReport.ExportAsync(new DriverInventoryReport("1.0", DateTimeOffset.UtcNow, "Inventário de drivers do kernel; sem binários nem números de série.", rows), path, lifetime);
            status.Text = "Inventário exportado para arquivo local.";
        }
        catch (OperationCanceledException) { status.Text = "Exportação cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { status.Text = "Não foi possível exportar. Escolha um nome novo: " + e.Message; }
        finally { button.IsEnabled = true; }
    }
}
