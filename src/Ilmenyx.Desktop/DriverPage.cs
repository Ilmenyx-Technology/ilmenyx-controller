// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
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
    private readonly Action? openNvidiaTab;
    private readonly TextBlock status = new() { Name = "DrvStatus", Text = "Clique em VERIFICAR TUDO.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock headline = new() { Name = "DrvHeadline", Text = "Ainda não verificado.", FontSize = 17, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel summaryLines = new() { Name = "DrvSummaryLines", Spacing = 3, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Grid categories = new() { Name = "DrvCategories", ColumnDefinitions = new("*,14,*") };
    private readonly StackPanel colA = new() { Name = "DrvColumnA" }, colB = new() { Name = "DrvColumnB" };
    private readonly TextBlock board = new() { Name = "DrvBoard", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button boardPage = new() { Name = "BtnDrvBoardPage", Content = "ABRIR SUPORTE DO FABRICANTE", IsEnabled = false, Margin = new Thickness(0, 14, 0, 8) };
    private readonly StackPanel fwList = new() { Name = "DrvFirmware", Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button fwCopy = new() { Name = "BtnDrvFwCopy", Content = "COPIAR COMANDO DE ATUALIZAÇÃO", IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel list = new() { Name = "DrvList", Margin = new Thickness(0, 10, 0, 0) };
    private IReadOnlyList<GpuDriverStatus> gpus = [];
    private IReadOnlyList<DriverRow> rows = [];
    private IReadOnlyList<DriverCategoryView> cards = [];
    private BoardInfo boardInfo = new();
    private bool busy;
    public IReadOnlyList<DriverRow> Rows => rows;
    public IReadOnlyList<DriverCategoryView> Cards => cards;

    public DriverPage(Func<string, Task<string?>> destination, CancellationToken lifetime, LinuxDriverInventory? inventory = null, Action<DriverTools.DriverManager>? launchManager = null, Func<string, string?>? findManager = null, Action? openNvidiaTab = null)
    {
        this.openNvidiaTab = openNvidiaTab;
        this.inventory = inventory ?? new();
        this.launchManager = launchManager ?? DriverTools.LaunchDriverManager;
        this.findManager = findManager ?? BoundedProcess.FindSystem;
        this.destination = destination; this.lifetime = lifetime; Spacing = 4;
        var scan = new Button { Name = "BtnDrvScan", Content = "VERIFICAR TUDO", Background = Palette.Accent, Foreground = Palette.OnAccent, FontWeight = FontWeight.Bold, Padding = new Thickness(20, 9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        scan.Click += async (_, _) => await ScanAsync();
        var header = UiKit.PageHeader("Drivers", "Driver é o programa que faz o Linux conversar com cada peça. Aqui você vê, por tipo de peça, se está tudo funcionando e o que fazer quando não está. Nada é instalado sem você pedir.");
        header.RowDefinitions = new("Auto,Auto");
        Grid.SetRow(scan, 1); scan.Margin = new Thickness(0, 10, 0, 0);
        header.Children.Add(scan);
        Children.Add(header); Children.Add(status);

        var summary = UiKit.FeatureCard(new StackPanel { Children =
        {
            UiKit.Label("RESUMO"), headline, summaryLines,
            new TextBlock { Text = "Livre = código aberto, já vem com o Linux e se atualiza com o sistema. Proprietário = feito pelo fabricante, código fechado (como o driver oficial da NVIDIA).", Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) },
        } }, "DrvSummary");
        summary.Margin = new Thickness(0, 0, 0, 14);
        Children.Add(summary);
        Grid.SetColumn(colB, 2); categories.Children.Add(colA); categories.Children.Add(colB);
        categories.SizeChanged += (_, e) => { if (e.WidthChanged && (e.NewSize.Width >= 860) != colB.IsVisible) Arrange2(e.NewSize.Width); };
        Children.Add(categories);

        var fw = new Button { Name = "BtnDrvFwupd", Content = "PROCURAR ATUALIZAÇÕES DE FIRMWARE", Margin = new Thickness(0, 12, 0, 8) };
        fw.Click += async (_, _) => await FirmwareAsync(fw);
        fwCopy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync("fwupdmgr update"); status.Text = "Comando copiado: cole no terminal e confirme por conta própria."; } };
        Children.Add(Card("FIRMWARE DAS PEÇAS E DA BIOS", new StackPanel { Children = { Note("Firmware é o programa que fica gravado dentro da peça (SSD, BIOS, touchpad…). O serviço fwupd do Linux sabe quais peças têm atualização oficial (LVFS)."), fwList, new WrapPanel { ItemSpacing = 10, Children = { fw, fwCopy } }, Note("Somente leitura. Para atualizar, rode `fwupdmgr update` no terminal; o Ilmenyx não instala firmware nem pede senha.") } }));
        boardPage.Click += async (_, _) => await OpenAsync(boardInfo.SupportUrl);
        Children.Add(Card("PLACA-MÃE E FABRICANTE", new StackPanel { Children = { board, boardPage, Note("Fabricantes não têm um serviço público de versões. Abrimos o suporte oficial do fabricante detectado (chipset, áudio, rede, BIOS); se a página não abrir no seu modelo, busque o modelo mostrado acima no site.") } }));
        var backup = new Button { Name = "BtnDrvBackup", Content = "EXPORTAR INVENTÁRIO (JSON)", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        backup.Click += async (_, _) => await ExportAsync(backup);
        Children.Add(Card("BACKUP DO INVENTÁRIO", new StackPanel { Children = { new TextBlock { Text = "Salva a lista de peças e drivers em um arquivo novo (útil para mandar a um técnico). No Linux os drivers fazem parte do kernel e dos pacotes da distribuição; não há instaladores para exportar.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }, backup } }));
        var full = new Expander { Name = "DrvListExpander", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Header = new TextBlock { Text = "Lista técnica completa (todos os dispositivos e módulos do kernel)", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, Content = list };
        Children.Add(Card("PARA TÉCNICOS", full));
        Loaded += async (_, _) => { if (rows.Count == 0) await ScanAsync(); };
    }

    private static TextBlock Note(string text) => new() { Text = text, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private static Border Card(string title, Control content) => new()
    {
        Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(22, 18), Margin = new Thickness(0, 0, 0, 14),
        Child = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint }, content } }
    };

    private static IBrush HealthBrush(DriverHealth h) => h switch { DriverHealth.Attention => Palette.Warn, DriverHealth.Ok => Palette.Success, _ => Palette.Muted };
    private static Border Badge(string text, IBrush brush, string? name = null) => new()
    {
        Name = name, BorderBrush = brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(9, 2), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right,
        Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = brush },
    };

    /// <summary>Two independent columns of category cards on wide windows (each card keeps its own height), one column otherwise.</summary>
    private void Arrange2(double width)
    {
        bool two = width >= 860;
        var cards_ = colA.Children.Concat(colB.Children).OrderBy(c => c.Tag is int i ? i : 0).ToList();
        colA.Children.Clear(); colB.Children.Clear();
        int perColumn = two ? (cards_.Count + 1) / 2 : cards_.Count;
        // Column-major so the order reads top-down: Vídeo, Rede, Bluetooth | Áudio, Impressoras, Outros.
        for (int i = 0; i < cards_.Count; i++) (i < perColumn ? colA : colB).Children.Add(cards_[i]);
        Grid.SetColumnSpan(colA, two ? 1 : 3); // the 3 column definitions stay: colB must keep a valid column index
        colB.IsVisible = two;
    }

    public async Task ScanAsync()
    {
        if (busy) return;
        busy = true; status.Text = "Lendo dispositivos e drivers (sysfs, somente leitura)…";
        try
        {
            var (inv, g, b, distro) = await Task.Run(() => (inventory.Inventory(pciIds), inventory.Gpus(pciIds), inventory.Board(), inventory.Distro()), lifetime);
            rows = inv; gpus = g; boardInfo = b;
            Fill(distro);
            status.Text = $"Pronto às {DateTime.Now:HH:mm:ss}. {inv.Count} dispositivo(s) lido(s).";
        }
        catch (OperationCanceledException) { status.Text = "Leitura cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Falhou: " + e.Message; }
        finally { busy = false; }
    }

    private static string IconOf(DriverCategory c) => c switch
    {
        DriverCategory.Video => Icons.Gpu, DriverCategory.Network => Icons.Network, DriverCategory.Bluetooth => Icons.Ports,
        DriverCategory.Audio => Icons.Bell, DriverCategory.Printing => Icons.Report, _ => Icons.System,
    };

    private void Fill((string Id, string IdLike) distro)
    {
        cards = DriverOverview.Build(rows, gpus);
        var sum = DriverOverview.Summarize(cards);
        headline.Text = sum.Headline; headline.Foreground = sum.Attention > 0 ? Palette.Warn : Palette.Success;
        summaryLines.Children.Clear();
        foreach (var line in sum.Lines) summaryLines.Children.Add(new TextBlock { Text = "• " + line, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = line.EndsWith(": ok.", StringComparison.Ordinal) ? Palette.Muted : Palette.Fg });
        var manager = DriverTools.FindDriverManager(distro.Id, distro.IdLike, findManager);
        colA.Children.Clear(); colB.Children.Clear();
        int order = 0;
        foreach (var c in cards) { var card = CategoryCard(c, distro, manager); card.Tag = order++; colA.Children.Add(card); }
        Arrange2(categories.Bounds.Width > 0 ? categories.Bounds.Width : Bounds.Width);

        board.Text = $"{boardInfo.Maker} {boardInfo.Model}\nBIOS: {(boardInfo.Bios.Length > 0 ? boardInfo.Bios : "—")}" + (boardInfo.Brand.Length > 0 ? $"\nFabricante reconhecido: {boardInfo.Brand}" : "\nFabricante não reconhecido");
        boardPage.IsEnabled = boardInfo.SupportUrl.Length > 0;
        list.Children.Clear();
        foreach (var r in rows)
        {
            var left = new StackPanel { Children = { new TextBlock { Text = r.Device, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = $"{r.Class} · {r.Vendor}" + (r.Id.Length > 0 ? $" · {r.Id}" : ""), Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap } } };
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0), Children = { new TextBlock { Text = r.Driver, FontFamily = new FontFamily("monospace"), HorizontalAlignment = HorizontalAlignment.Right }, new TextBlock { Text = $"{r.Version} · {r.Flags}", Foreground = Palette.Muted, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap } } };
            var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 0, 0, 8) }; Grid.SetColumn(right, 1); row.Children.Add(left); row.Children.Add(right);
            list.Children.Add(row);
        }
    }

    private Control CategoryCard(DriverCategoryView c, (string Id, string IdLike) distro, DriverTools.DriverManager? manager)
    {
        var body = new StackPanel { Spacing = 0 };
        string state = c.Devices.Count == 0 ? "NADA ENCONTRADO" : c.Health switch { DriverHealth.Attention => "ATENÇÃO", DriverHealth.Ok => "OK", _ => "OK" };
        var title = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        var icon = UiKit.Icon(IconOf(c.Category), 18, Palette.Muted); icon.Margin = new Thickness(0, 0, 10, 0);
        var name = new TextBlock { Text = c.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var badge = Badge(state, c.Devices.Count == 0 ? Palette.Muted : HealthBrush(c.Health == DriverHealth.Info ? DriverHealth.Ok : c.Health), "DrvCatState_" + c.Category);
        Grid.SetColumn(name, 1); Grid.SetColumn(badge, 2); title.Children.Add(icon); title.Children.Add(name); title.Children.Add(badge);
        body.Children.Add(title);
        body.Children.Add(new TextBlock { Text = c.Description, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) });
        if (c.Devices.Count == 0) body.Children.Add(new TextBlock { Text = c.Empty, Foreground = Palette.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        if (c.Category == DriverCategory.Other && c.Devices.Count > 0)
        {
            // Many small devices: plain tally first, the list folded (attention items stay visible).
            int ok = c.Devices.Count(d => d.Health == DriverHealth.Ok), idle = c.Devices.Count(d => d.Health == DriverHealth.Info);
            var kinds = string.Join(", ", c.Devices.GroupBy(d => d.Kind).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ({g.Count()})"));
            body.Children.Add(new TextBlock { Name = "DrvOtherTally", Text = $"{ok} com driver funcionando" + (idle > 0 ? $" · {idle} sem driver em uso (normal para peças internas)" : "") + ".", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2) });
            body.Children.Add(new TextBlock { Text = kinds, FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
            foreach (var d in c.Devices.Where(d => d.Health == DriverHealth.Attention)) body.Children.Add(DeviceRow(d, distro, manager));
            var more = new StackPanel();
            foreach (var d in c.Devices.Where(d => d.Health != DriverHealth.Attention)) more.Children.Add(DeviceRow(d, distro, manager));
            body.Children.Add(new Expander { Name = "DrvOtherExpander", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0), Header = new TextBlock { Text = $"Mostrar os {more.Children.Count} dispositivos", FontSize = 12.5 }, Content = more });
        }
        else foreach (var d in c.Devices) body.Children.Add(DeviceRow(d, distro, manager));
        var card = UiKit.Card(body, new Thickness(0, 0, 0, 14)); card.Name = "DrvCat_" + c.Category; card.Padding = new Thickness(20, 16);
        if (c.Health == DriverHealth.Attention) card.BorderBrush = Palette.Warn;
        return card;
    }

    private Control DeviceRow(DriverDevice d, (string Id, string IdLike) distro, DriverTools.DriverManager? manager)
    {
        var r = d.Row;
        var head = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        string device = d.Gpu is { } g0 ? NvidiaGenerations.MarketingName(g0.GpuName) : r.Device;
        head.Children.Add(new TextBlock { Text = device, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) });
        var badge = Badge(d.Badge, HealthBrush(d.Health)); Grid.SetColumn(badge, 1); head.Children.Add(badge);
        var panel = new StackPanel { Spacing = 2, Children = { head } };
        panel.Children.Add(new TextBlock { Text = $"{d.Kind} · Driver em uso: {(DriverOverview.HasDriver(r) ? r.Driver : "nenhum")} · {d.License}", FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = d.Plain, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        // ONE recommended action, when there is something to do.
        Button? action = d.Action switch
        {
            DriverAction.OpenNvidiaTab => NvidiaTabButton("BtnDrvNvidiaTab", d.Health == DriverHealth.Attention),
            DriverAction.OpenDriverManager when manager is not null => ManagerButton("BtnDrvFixManager", manager, primary: true),
            _ => null,
        };
        if (action is not null) { action.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(action); }
        else if (d.Action == DriverAction.OpenDriverManager)
            panel.Children.Add(Note("Procure o driver ou o firmware desta peça no gerenciador de pacotes da sua distribuição (pacotes \"firmware\" / \"linux-firmware\")."));

        var details = new StackPanel { Spacing = 4 };
        details.Children.Add(new SelectableTextBlock { Text = $"Módulo do kernel: {r.Driver}\nVersão: {r.Version}\nTipo: {r.Flags}\nBarramento: {(r.Bus.Length > 0 ? r.Bus : "—")} · ID: {(r.Id.Length > 0 ? r.Id : "—")} · Classe: {(r.ClassCode.Length > 0 ? r.ClassCode : r.Class)}\nFabricante: {r.Vendor}", FontSize = 11.5, Foreground = Palette.Muted, FontFamily = new FontFamily("monospace"), TextWrapping = TextWrapping.Wrap });
        if (d.Gpu is { } g) details.Children.Add(GpuTools(g, distro, manager));
        panel.Children.Add(new Expander { Name = "DrvDetails", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 0), Header = new TextBlock { Text = "Detalhes técnicos", FontSize = 12, Foreground = Palette.Muted }, Content = details });
        return panel;
    }

    private Button ManagerButton(string name, DriverTools.DriverManager? manager, bool primary)
    {
        var open = primary ? UiKit.Primary(name, "ABRIR GERENCIADOR DE DRIVERS") : new Button { Name = name, Content = "ABRIR GERENCIADOR DE DRIVERS DA DISTRIBUIÇÃO" };
        open.IsEnabled = manager is not null; open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Click += (_, _) =>
        {
            if (lifetime.IsCancellationRequested || manager is null) return;
            try { launchManager(manager); status.Text = "Gerenciador de drivers aberto. Escolha o driver recomendado pela distribuição."; }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or ArgumentException or UnauthorizedAccessException) { status.Text = "Não foi possível abrir o gerenciador: " + e.Message; }
        };
        return open;
    }

    /// <summary>The previous GPU card, now under "Detalhes técnicos": versions, online check (click only), official page, suggested command, distribution tool.</summary>
    private Control GpuTools(GpuDriverStatus g, (string Id, string IdLike) distro, DriverTools.DriverManager? manager)
    {
        var info = new TextBlock { Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        string branchText = g.SupportedBranch is { } sb ? $" do ramo {sb}" : "";
        void Refresh() => info.Text = $"Instalado: {(g.Installed.Length > 0 ? g.Installed : "—")}  ·  Mais recente{branchText}: {(g.Latest.Length > 0 ? g.Latest : "não consultado")}  ·  Driver: {g.Driver}";
        Refresh();
        var message = new TextBlock { Text = g.Message, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        var hint = new TextBox { Name = "DrvInstallHint", Text = DriverInstallHints.For(distro.Id, distro.IdLike, g.Vendor), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var buttons = new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        if (g.Vendor == "NVIDIA")
        {
            var latest = new Button { Name = "BtnDrvNvidiaLatest", Content = "VERIFICAR VERSÃO MAIS RECENTE (internet)", IsEnabled = g.SupportedBranch is not null };
            ToolTip.SetTip(latest, "Consulta " + DriverTools.NvidiaReleasesUrl + " (somente ao clicar) e compara só com o ramo que suporta esta placa.");
            latest.Click += async (_, _) =>
            {
                if (g.SupportedBranch is not { } branch) return;
                latest.IsEnabled = false; status.Text = "Consultando " + DriverTools.NvidiaReleasesUrl + "…";
                try
                {
                    string? v = await DriverTools.NvidiaBranchLatestAsync(branch, lifetime);
                    var gen = NvidiaGenerations.Classify(g.DeviceId).Generation;
                    string why = NvidiaGenerations.Support(gen).Legacy ? $" Ramo {branch} = último que suporta {NvidiaGenerations.Name(gen)}; não instale 590 ou mais novo." : "";
                    if (v is null) { message.Text = $"A listagem da NVIDIA não tem versões do ramo {branch}."; }
                    else { g.Latest = v; Refresh(); message.Text = (g.UpdateAvailable ? $"Há uma versão mais recente do ramo {branch} ({v}). Atualize pela aba Driver NVIDIA (quando a distribuição já publicar o pacote)." : $"Você está na versão mais recente do ramo {branch} publicada pela NVIDIA (ou mais nova).") + why; }
                    status.Text = "Consulta concluída.";
                }
                catch (OperationCanceledException) { status.Text = "Consulta cancelada."; }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { message.Text = "Não foi possível consultar a NVIDIA: " + e.Message; status.Text = "Consulta falhou."; }
                finally { latest.IsEnabled = true; }
            };
            buttons.Children.Add(latest);
            if (g.Driver == "nouveau") buttons.Children.Add(ManagerButton("BtnDrvManager", manager, primary: false));
        }
        var page = new Button { Name = "BtnDrvGpuPage", Content = "ABRIR PÁGINA OFICIAL", IsEnabled = g.OfficialPage.Length > 0 };
        page.Click += async (_, _) => await OpenAsync(g.OfficialPage);
        var copy = new Button { Name = "BtnDrvCopyHint", Content = "COPIAR COMANDO SUGERIDO" };
        copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(hint.Text ?? ""); status.Text = "Comando copiado; execute-o no terminal por conta própria."; } };
        buttons.Children.Add(page); buttons.Children.Add(copy);
        var tools = new StackPanel { Children = { info, message, hint, buttons } };
        if (g.Vendor == "NVIDIA" && g.Driver == "nouveau")
            tools.Children.Add(Note(manager is null ? DriverInstallHints.For(distro.Id, distro.IdLike, g.Vendor) : "A aba Driver NVIDIA instala o driver certo para esta placa (DKMS para o kernel em uso, com simulação antes). O gerenciador da distribuição é a alternativa."));
        return tools;
    }

    private Button NvidiaTabButton(string name, bool primary = true)
    {
        var b = primary ? UiKit.Primary(name, "ABRIR A ABA DRIVER NVIDIA") : new Button { Name = name, Content = "ABRIR A ABA DRIVER NVIDIA" };
        b.HorizontalAlignment = HorizontalAlignment.Left; b.IsEnabled = openNvidiaTab is not null;
        b.Click += (_, _) => openNvidiaTab?.Invoke();
        return b;
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
                fwList.Children.Add(new TextBlock { Text = $"{i.Name} · versão {i.Version}" + (i.Update.Length > 0 ? $"  →  atualização {i.Update} disponível" : "  ·  em dia"), TextWrapping = TextWrapping.Wrap, Foreground = i.Update.Length > 0 ? Palette.Warn : Palette.Fg });
            int updates = items.Count(i => i.Update.Length > 0);
            fwCopy.IsVisible = updates > 0;
            status.Text = items.Count == 0 ? "O fwupd não listou dispositivos atualizáveis." : updates > 0 ? $"{items.Count} dispositivo(s) no fwupd; {updates} com atualização de firmware." : $"{items.Count} dispositivo(s) no fwupd, todos em dia.";
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
            await SnapshotReport.ExportAsync(new DriverInventoryReport("1.1", DateTimeOffset.UtcNow, "Inventário de drivers do kernel; sem binários nem números de série.", rows), path, lifetime);
            status.Text = "Inventário exportado para arquivo local.";
        }
        catch (OperationCanceledException) { status.Text = "Exportação cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { status.Text = "Não foi possível exportar. Escolha um nome novo: " + e.Message; }
        finally { button.IsEnabled = true; }
    }
}
