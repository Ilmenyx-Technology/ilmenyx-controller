// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Disks page: header, refresh, verdict cards, footnote; health and space per disk,
// one-time SMART permission, partitions map and safe actions 2026-11-11): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// Disks page: each disk with its health (SMART when allowed), how full it is and, for pendrives and cards, eject and format; a Partitions
/// tab with the map of every disk, mount/unmount and a shortcut to a full partition editor for the system disk. Nothing changes without
/// a confirmation, and the disk holding the running system is never unmounted, formatted or ejected.
/// </summary>
public sealed class DiskHealthPage : StackPanel
{
    private readonly LinuxDiskHealthReader reader = new();
    private readonly CancellationToken lifetime;
    private readonly Func<Window?> owner;
    private readonly Action<string>? open;
    private readonly StackPanel cards = new() { Name = "DiskCards", Spacing = 12 };
    private readonly StackPanel partitions = new() { Name = "DiskPartitions", Spacing = 12, IsVisible = false };
    private readonly StackPanel healthTab = new() { Spacing = 12 };
    private readonly TextBlock status = new() { Name = "DiskHealthStatus", Text = L.T("Lendo os discos…"), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock actionResult = new() { Name = "DiskActionResult", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Border banner = new() { Name = "DiskSmartBanner", IsVisible = false, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(18, 14) };
    private readonly Dictionary<string, SmartDiagnostic> smart = new(StringComparer.Ordinal);
    private readonly Button healthChip, partsChip;
    private readonly Button revoke = new() { Name = "BtnSmartRevoke", FontSize = 12, Padding = new Thickness(12, 6), IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
    private IReadOnlyList<BlockDevice> devices = [];
    private bool busy, triedSmart;

    public IReadOnlyList<HealthCard> Cards { get; private set; } = [];

    /// <param name="open">Opens another page of the shell (Liberar espaço); null hides the shortcut.</param>
    public DiskHealthPage(Func<Window?> owner, CancellationToken lifetime, Action<string>? open = null)
    {
        this.owner = owner; this.lifetime = lifetime; this.open = open;
        Spacing = 12;
        var refresh = UiKit.Primary("BtnDiskRefresh", L.T("Atualizar"));
        refresh.Click += async (_, _) => await RefreshAsync();
        Children.Add(UiKit.PageHeader(L.T("Discos"), L.T("Cada disco do PC, quanto espaço tem e se está saudável."), refresh));
        healthChip = Chip("DiskTabHealth", L.T("Saúde e espaço")); partsChip = Chip("DiskTabParts", L.T("Partições"));
        healthChip.Click += (_, _) => Tab(false); partsChip.Click += (_, _) => Tab(true);
        Children.Add(new WrapPanel { ItemSpacing = 6, Children = { healthChip, partsChip } });
        Children.Add(status); Children.Add(actionResult);
        healthTab.Children.Add(banner); healthTab.Children.Add(cards);
        healthTab.Children.Add(new TextBlock
        {
            Text = L.T("A saúde vem do S.M.A.R.T. do próprio disco (lido com a sua permissão, sem mudar nada) e da temperatura que o kernel expõe. Pendrives e alguns HDs externos não informam saúde."),
            Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8),
        });
        revoke.Content = L.T("Remover permissão de ler a saúde");
        revoke.Click += async (_, _) => await RevokeAsync();
        healthTab.Children.Add(revoke);
        Children.Add(healthTab); Children.Add(partitions);
        Tab(false);
        Loaded += async (_, _) => { if (Cards.Count == 0) await RefreshAsync(); };
    }

    private static Button Chip(string name, string text) => new() { Name = name, Content = text, FontSize = 12.5, Padding = new Thickness(14, 6), CornerRadius = new CornerRadius(999) };

    private void Tab(bool parts)
    {
        UiKit.ChipStyle(healthChip, !parts); UiKit.ChipStyle(partsChip, parts);
        healthTab.IsVisible = !parts; partitions.IsVisible = parts;
    }

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; status.Text = L.T("Lendo os discos…");
        try
        {
            // Once the permission exists, every refresh reads the health of each whole disk (a few hundred ms, only on this page).
            if (SmartAccess.Installed && !triedSmart) { triedSmart = true; await ReadAllSmartAsync(); }
            var snapshot = smart.ToDictionary(kv => kv.Key, kv => kv.Value);
            var built = await Task.Run(() =>
            {
                string mounts = ""; try { mounts = File.ReadAllText("/proc/self/mounts"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                return reader.Read(mounts, snapshot).Select(DiskHealthEvaluator.Evaluate).ToList();
            }, lifetime);
            try { devices = await BlockDevices.ReadAsync(lifetime); }
            catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or System.Text.Json.JsonException) { devices = []; status.Text = L.T("Não foi possível listar as partições: ") + e.Message; }
            Cards = built; Rebuild();
            int bad = built.Count(c => c.Verdict is HealthVerdict.Warning or HealthVerdict.Critical);
            status.Text = built.Count == 0 ? L.T("Nenhum disco encontrado.")
                : bad == 0 ? L.T($"{built.Count} disco(s) · nenhum problema encontrado · lido às {DateTime.Now:HH:mm:ss}")
                : L.T($"{built.Count} disco(s) · {bad} precisa(m) de atenção · lido às {DateTime.Now:HH:mm:ss}");
        }
        catch (OperationCanceledException) { status.Text = L.T("Leitura cancelada."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = L.T("Não foi possível ler os discos: ") + e.Message; }
        finally { busy = false; }
    }

    private async Task ReadAllSmartAsync()
    {
        foreach (var name in SmartDisks())
            try
            {
                var d = await new SmartctlDiagnostic(new HelperSmartctlQuery()).CollectAsync(name, lifetime);
                if (d.Status == "available") smart[name] = d;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or ArgumentException or FileNotFoundException or System.ComponentModel.Win32Exception) { }
    }

    private static IEnumerable<string> SmartDisks()
    {
        try { return Directory.EnumerateDirectories("/sys/block").Select(Path.GetFileName).OfType<string>().Where(SmartctlQuery.ValidDeviceName).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private void Rebuild()
    {
        revoke.IsVisible = SmartAccess.Installed;
        BuildBanner();
        cards.Children.Clear();
        if (Cards.Count == 0) cards.Children.Add(new TextBlock { Text = L.T("Nenhum disco encontrado."), Foreground = Palette.Muted });
        foreach (var c in Cards) cards.Children.Add(DiskCard(c, devices.FirstOrDefault(d => d.Name == c.Device)));
        BuildPartitions();
    }

    /// <summary>Health that could not be read gets a fix in plain words, never a sudo command in the title.</summary>
    private void BuildBanner()
    {
        bool missing = Cards.Any(c => SmartctlQuery.ValidDeviceName(c.Device) && !smart.ContainsKey(c.Device));
        banner.IsVisible = false;
        if (!missing) return;
        var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Palette.Hover, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "i", FontWeight = FontWeight.Bold, Foreground = Palette.Warn, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var title = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var text = new TextBlock { FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        if (!SmartAccess.SmartctlPresent)
        {
            var (id, like) = new LinuxDriverInventory(etcRoot: HostSpawn.InFlatpak ? "/run/host/etc" : "/etc").Distro();
            string cmd = SmartAccess.InstallHint(id, like);
            title.Text = L.T("Para ler a saúde dos discos, falta um programa do sistema");
            text.Text = L.T("O smartmontools (smartctl) é quem conversa com o disco. Instale pelo Terminal e clique em Atualizar:");
            var box = new TextBox { Name = "DiskSmartInstallCmd", Text = cmd, IsReadOnly = true, FontFamily = IlmenyxTheme.Mono, FontSize = 12 };
            AutomationProperties.SetName(box, L.T("Comando para instalar o smartmontools"));
            var copy = new Button { Name = "BtnCopySmartInstall", Content = L.T("Copiar comando"), Padding = new Thickness(12, 7) };
            copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(cmd); Result(L.T("Comando copiado. Cole no Terminal; ele pede a sua senha."), Palette.Muted); };
            actions.Children.Add(box); actions.Children.Add(copy);
        }
        else if (!SmartAccess.Installed)
        {
            title.Text = L.T("A saúde dos discos ainda não foi lida");
            text.Text = L.T("O Linux só deixa ler a saúde (desgaste, erros) com permissão de administrador. Você libera uma vez com a sua senha e o Ilmenyx passa a mostrar sempre, só lendo, sem mudar nada no disco.");
            var grant = UiKit.Primary("BtnSmartGrant", L.T("Liberar leitura (pede senha)"));
            grant.Click += async (_, _) => await GrantAsync(grant);
            actions.Children.Add(grant);
        }
        else
        {
            title.Text = L.T("A saúde de um disco não respondeu");
            text.Text = L.T("A permissão já existe, mas o disco não informou a saúde agora (pode estar em repouso, ou é um modelo que não informa). Clique em Atualizar para tentar de novo.");
            var retry = new Button { Name = "BtnSmartRetry", Content = L.T("Tentar de novo"), Padding = new Thickness(14, 7) };
            retry.Click += async (_, _) => { triedSmart = false; await RefreshAsync(); };
            actions.Children.Add(retry);
        }
        var grid = new Grid { ColumnDefinitions = new("Auto,*") };
        var body = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Children = { title, text, actions } };
        Grid.SetColumn(body, 1); grid.Children.Add(icon); grid.Children.Add(body);
        banner.Child = grid; banner.Background = Palette.WarnBg; banner.IsVisible = true;
    }

    private async Task GrantAsync(Button grant)
    {
        if (owner() is not { } window) return;
        if (!await ConfirmDialog.AskAsync(window, L.T("Liberar a leitura da saúde dos discos?"),
            L.T("O sistema vai pedir a sua senha uma vez. O Ilmenyx instala um pequeno programa que só lê a saúde (S.M.A.R.T.) de um disco inteiro e uma regra que deixa você usá-lo sem senha. Nada no disco é alterado.\n\nPara desfazer: \"Remover permissão\" no fim desta página."),
            L.T("Liberar"), L.T("Cancelar"))) return;
        grant.IsEnabled = false; Result(L.T("Aguardando a senha…"), Palette.Muted);
        var (ok, message) = await PrivilegedFix.RunAsync(SmartAccess.InstallScript, lifetime);
        grant.IsEnabled = true;
        if (!ok) { Result(message, Palette.Warn); return; }
        Result(L.T("Permissão criada. Lendo a saúde dos discos…"), Palette.Success);
        triedSmart = false; await RefreshAsync();
        Result(smart.Count > 0 ? L.T("Pronto: a saúde dos discos aparece abaixo.") : L.T("Permissão criada, mas nenhum disco informou a saúde."), smart.Count > 0 ? Palette.Success : Palette.Warn);
    }

    private async Task RevokeAsync()
    {
        if (owner() is not { } window) return;
        if (!await ConfirmDialog.AskAsync(window, L.T("Remover a permissão?"), L.T("O programa de leitura e a regra são apagados (o sistema pede a senha). A saúde dos discos deixa de aparecer até você liberar de novo."), L.T("Remover"), L.T("Cancelar"))) return;
        var (ok, message) = await PrivilegedFix.RunAsync(SmartAccess.UndoScript, lifetime);
        if (!ok) { Result(message, Palette.Warn); return; }
        smart.Clear(); triedSmart = false;
        Result(L.T("Permissão removida."), Palette.Success);
        await RefreshAsync();
    }

    private void Result(string text, IBrush brush) { actionResult.Text = text; actionResult.Foreground = brush; actionResult.IsVisible = true; }

    private static string Size(long bytes) => bytes >= 1L << 40 ? (bytes / (double)(1L << 40)).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " TB"
        : bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " GB"
        : (bytes / (double)(1L << 20)).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " MB";

    /// <summary>"SSD do sistema", "HD", "Pendrive"…: what the disk is, in words, from the transport, the spinning flag and the mounts.</summary>
    private static string Kind(BlockDevice? d, HealthCard c)
    {
        if (d is null) return c.Title;
        if (d.Name.StartsWith("mmcblk", StringComparison.Ordinal) || d.Transport == "mmc") return L.T("Cartão de memória");
        // Many USB sticks report themselves as rotational: the transport says more than the flag.
        if (d.Transport == "usb") return L.T("Pendrive ou disco USB");
        string kind = d.Rotational ? L.T("HD") : L.T("SSD");
        return d.IsSystem ? kind + L.T(" do sistema") : kind + L.T(" de dados");
    }

    private Control DiskCard(HealthCard c, BlockDevice? d)
    {
        var icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = Palette.Hover, VerticalAlignment = VerticalAlignment.Top,
            Child = UiKit.Icon(d?.IsExternal == true ? Icons.Ports : Icons.Disks, 18, d?.IsExternal == true ? Palette.DataNetwork : Palette.DataDisk) };
        ((Avalonia.Controls.Shapes.Path)icon.Child).HorizontalAlignment = HorizontalAlignment.Center; ((Avalonia.Controls.Shapes.Path)icon.Child).VerticalAlignment = VerticalAlignment.Center;
        var titleLine = new WrapPanel { ItemSpacing = 8, Children = { new TextBlock { Text = Kind(d, c), FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap } } };
        string meta = string.Join(" · ", new[] { d?.Model, d?.Transport?.ToUpperInvariant(), d is { Size: > 0 } ? Size(d.Size) : null, "/dev/" + c.Device }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var body = new StackPanel { Spacing = 4, Children = { titleLine, new TextBlock { Text = meta, FontSize = 12, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap } } };
        // Space: the mounted file systems of the disk, each counted once (btrfs subvolumes share one).
        var mounted = d?.Partitions.Append(d).Where(p => p.FsUsed is not null && p.FsAvail is not null && p.Mounts.Count > 0).GroupBy(p => p.Path).Select(g => g.First()).ToList() ?? [];
        if (mounted.Count > 0)
        {
            long used = mounted.Sum(p => p.FsUsed!.Value), avail = mounted.Sum(p => p.FsAvail!.Value);
            double fraction = used + avail > 0 ? (double)used / (used + avail) : 0;
            var color = fraction >= 0.9 ? Palette.Error : fraction >= 0.75 ? Palette.Warn : Palette.Success;
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = fraction, Height = 8, MinHeight = 8, Foreground = color, Margin = new Thickness(0, 8, 0, 0) };
            AutomationProperties.SetName(bar, L.T("Espaço usado"));
            body.Children.Add(bar);
            body.Children.Add(new TextBlock { Name = "DiskSpace_" + c.Device, Text = L.T($"{Size(used)} usados · {Size(avail)} livres · {fraction * 100:0}% cheio"), FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
            if (fraction >= 0.9) body.Children.Add(new TextBlock { Text = L.T("Quase cheio: o sistema fica lento e atualizações podem falhar. Libere espaço apagando o que não usa mais."), FontSize = 12, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap });
            if (fraction >= 0.75 && open is { } go && d is { IsExternal: false })
            {
                var free = new Button { Name = "BtnDiskFreeSpace_" + c.Device, Content = L.T("Liberar espaço"), FontSize = 12, Padding = new Thickness(12, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                free.Click += (_, _) => go("cleanup");
                body.Children.Add(free);
            }
        }
        else if (d?.IsExternal == true && d.Partitions.Any()) body.Children.Add(new TextBlock { Text = L.T("Não montado: abra-o no gerenciador de arquivos ou monte na aba Partições."), FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        var tag = UiKit.Tag(c.VerdictText, HealthCardView.VerdictBrush(c.Verdict));
        var side = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { tag } };
        if (d is { IsExternal: true, IsSystem: false })
        {
            var eject = new Button { Name = "BtnEject_" + c.Device, Content = L.T("Ejetar"), Padding = new Thickness(14, 6), HorizontalAlignment = HorizontalAlignment.Right };
            var format = new Button { Name = "BtnFormat_" + c.Device, Content = L.T("Formatar…"), Padding = new Thickness(14, 6), Foreground = Palette.Error, HorizontalAlignment = HorizontalAlignment.Right };
            eject.Click += async (_, _) => await EjectAsync(d, eject);
            format.Click += async (_, _) => await FormatAsync(d, null);
            side.Children.Add(eject); side.Children.Add(format);
        }
        // The full report (SMART metrics, temperature, reasons) stays one click away, with the read-only SMART query button.
        var query = new Button { Name = "SmartQuery_" + c.Device, Content = L.T("Ler a saúde agora"), FontSize = 12, Padding = new Thickness(12, 6), Margin = new Thickness(0, 12, 0, 0) };
        query.Click += async (_, _) => await QuerySmartAsync(c.Device, query);
        var details = new Border { Child = HealthCardView.Build(c, query), IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
        var toggle = new Button { Name = "DiskDetails_" + c.Device, Content = L.T("Ver detalhes"), FontSize = 12, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Click += (_, _) => { details.IsVisible = !details.IsVisible; toggle.Content = details.IsVisible ? L.T("Esconder detalhes") : L.T("Ver detalhes"); };
        body.Children.Add(toggle);
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        body.Margin = new Thickness(14, 0, 12, 0);
        Grid.SetColumn(body, 1); Grid.SetColumn(side, 2);
        grid.Children.Add(icon); grid.Children.Add(body); grid.Children.Add(side);
        var card = UiKit.Card(new StackPanel { Children = { grid, details } }, new Thickness(0)); card.Padding = new Thickness(18, 14);
        return card;
    }

    /// <summary>One explicit, bounded read-only query: through the installed helper when allowed, otherwise as the user (fails closed).</summary>
    private async Task QuerySmartAsync(string device, Button button)
    {
        button.IsEnabled = false; status.Text = L.T($"Lendo a saúde de {device}…");
        try
        {
            ISmartctlQuery query = SmartAccess.Installed ? new HelperSmartctlQuery() : new SmartctlQuery();
            var d = await new SmartctlDiagnostic(query).CollectAsync(device, lifetime);
            if (d.Status == "available") { smart[device] = d; status.Text = L.T($"Saúde de {device} lida."); await RefreshAsync(); }
            else status.Text = L.T($"A saúde de {device} não foi lida: ") + (SmartAccess.Installed ? d.Reason : L.T("falta a permissão. Use \"Liberar leitura\" no aviso acima."));
        }
        catch (OperationCanceledException) { status.Text = L.T("Leitura cancelada."); }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception)
        { status.Text = L.T($"A saúde de {device} não foi lida: ") + e.Message; }
        finally { button.IsEnabled = true; BuildBanner(); }
    }

    private async Task EjectAsync(BlockDevice disk, Button button)
    {
        if (owner() is not { } window) return;
        if (!await ConfirmDialog.AskAsync(window, L.T("Ejetar ") + (disk.Model ?? disk.Name) + "?", L.T("Os arquivos abertos dele são salvos e ele é desligado. Depois é só tirar da porta."), L.T("Ejetar"), L.T("Cancelar"))) return;
        button.IsEnabled = false; Result(L.T("Ejetando…"), Palette.Muted);
        var error = await BlockDevices.EjectAsync(disk, lifetime);
        button.IsEnabled = true;
        Result(error ?? L.T("Pronto: já pode tirar o dispositivo."), error is null ? Palette.Success : Palette.Warn);
        await RefreshAsync();
    }

    private async Task FormatAsync(BlockDevice disk, BlockDevice? part)
    {
        if (owner() is not { } window) return;
        var target = part ?? disk.Partitions.FirstOrDefault() ?? disk;
        var dialog = new FormatDialog(disk, target);
        if (await dialog.ShowDialog<bool?>(window) != true) return;
        Result(L.T("Formatando… não tire o dispositivo."), Palette.Muted);
        var error = await BlockDevices.FormatAsync(dialog.Target, disk, dialog.FsType, dialog.Label, lifetime);
        Result(error ?? L.T("Pronto: formatado como ") + dialog.FsName + ".", error is null ? Palette.Success : Palette.Warn);
        await RefreshAsync();
    }

    // ---- Partitions tab ----

    private static IBrush FsBrush(BlockDevice p) => p.FsType switch
    {
        "ntfs" or "exfat" or "vfat" when p.PartType?.Contains("EFI", StringComparison.OrdinalIgnoreCase) != true => p.FsType == "ntfs" ? Palette.DataGpu : Palette.DataNetwork,
        "vfat" => Palette.DataCpu,
        "swap" => Palette.DataTemperature,
        null => Palette.Hover,
        _ => Palette.DataDisk,
    };

    /// <summary>"Linux (btrfs)", "Windows (NTFS)", "Inicialização (EFI)"…</summary>
    private static string FsName(BlockDevice p) => p.PartType?.Contains("EFI", StringComparison.OrdinalIgnoreCase) == true ? L.T("Inicialização (EFI)")
        : p.FsType switch
        {
            "ntfs" => L.T("Windows (NTFS)"), "exfat" => "exFAT", "vfat" => "FAT32", "swap" => L.T("Memória virtual (swap)"),
            "ext4" or "ext3" or "ext2" or "btrfs" or "xfs" or "f2fs" or "jfs" => "Linux (" + p.FsType + ")",
            "crypto_LUKS" => L.T("Criptografado (LUKS)"), null => p.PartType is { } t && t.Contains("reserved", StringComparison.OrdinalIgnoreCase) ? L.T("Reservada (Windows)") : L.T("Sem sistema de arquivos"),
            _ => p.FsType,
        };

    private void BuildPartitions()
    {
        partitions.Children.Clear();
        if (devices.Count == 0) { partitions.Children.Add(new TextBlock { Text = L.T("Nenhum disco listado pelo lsblk."), Foreground = Palette.Muted }); return; }
        foreach (var d in devices) partitions.Children.Add(PartitionCard(d));
        var editor = BlockDevices.PartitionEditor();
        var note = new TextBlock { Text = L.T("Para criar, apagar ou mudar o tamanho de partições do disco do sistema, use um editor de partições dedicado. Faça backup antes."), FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        var row = new Grid { ColumnDefinitions = new("*,Auto") };
        row.Children.Add(note);
        if (editor is { } e)
        {
            var open = new Button { Name = "BtnOpenPartitionEditor", Content = L.T("Abrir ") + e.Name, Padding = new Thickness(14, 7), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            open.Click += (_, _) => { if (BlockDevices.OpenPartitionEditor() is { } err) Result(err, Palette.Warn); };
            Grid.SetColumn(open, 1); row.Children.Add(open);
        }
        else note.Text += " " + L.T("Nenhum está instalado: o KDE Partition Manager e o GParted estão no gerenciador de programas da sua distro.");
        partitions.Children.Add(UiKit.Card(row, new Thickness(0)));
    }

    private Control PartitionCard(BlockDevice d)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = (d.Model ?? d.Name) + " · " + Size(d.Size) + (d.IsSystem ? L.T(" · disco do sistema") : ""), FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var parts = d.Partitions.ToList();
        // Map: one block per partition, proportional to its size (at least a sliver so tiny ones still show); free space at the end.
        var map = new Grid { Height = 28, ClipToBounds = true };
        long sum = parts.Sum(p => p.Size), free = Math.Max(0, d.Size - sum);
        var blocks = parts.Select(p => (Size: p.Size, Brush: FsBrush(p), Text: FsName(p))).ToList();
        if (free > d.Size / 100) blocks.Add((free, Palette.Hover, L.T("Livre")));
        if (blocks.Count == 0) blocks.Add((d.Size, FsBrush(d), FsName(d)));
        foreach (var b in blocks)
        {
            double weight = Math.Max(b.Size / (double)Math.Max(1, d.Size), 0.02);
            map.ColumnDefinitions.Add(new ColumnDefinition(weight, GridUnitType.Star));
            var cell = new Border
            {
                Background = new SolidColorBrush(((ISolidColorBrush)b.Brush).Color, 0.45), BorderBrush = Palette.Bg, BorderThickness = new Thickness(0, 0, 2, 0),
                Child = new TextBlock { Text = b.Text + " · " + Size(b.Size), FontSize = 11, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            ToolTip.SetTip(cell, b.Text + " · " + Size(b.Size));
            Grid.SetColumn(cell, map.ColumnDefinitions.Count - 1); map.Children.Add(cell);
        }
        body.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = map, BorderBrush = Palette.Line, BorderThickness = new Thickness(1) });
        foreach (var p in parts) body.Children.Add(PartitionRow(d, p));
        var card = UiKit.Card(body, new Thickness(0)); card.Padding = new Thickness(18, 14); card.Name = "PartCard_" + d.Name;
        return card;
    }

    private Control PartitionRow(BlockDevice disk, BlockDevice p)
    {
        var mounts = p.Mounts.OrderBy(m => m.Length).ThenBy(m => m, StringComparer.Ordinal).ToList(); // "/" and "/home" before subvolumes
        string where = mounts.Count > 0 ? L.T("montada em ") + string.Join(", ", mounts.Take(2)) + (mounts.Count > 2 ? " +" + (mounts.Count - 2) : "") : L.T("não montada");
        string used = p.FsUsed is { } u && p.FsAvail is { } a ? " · " + L.T($"{Size(u)} usados, {Size(a)} livres") : "";
        var info = new StackPanel { Children = {
            new TextBlock { Text = FsName(p) + (p.Label is { } l ? " · " + l : "") + " · " + Size(p.Size), FontSize = 13, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = p.Path + " · " + where + used, FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap } } };
        var actions = new WrapPanel { ItemSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        bool mountable = p.FsType is not null and not "swap" and not "crypto_LUKS" && !p.IsSystem;
        if (mountable)
        {
            bool mounted = p.Mounts.Count > 0;
            var b = new Button { Name = (mounted ? "BtnUnmount_" : "BtnMount_") + p.Name, Content = mounted ? L.T("Desmontar") : L.T("Montar"), FontSize = 12, Padding = new Thickness(12, 5) };
            b.Click += async (_, _) =>
            {
                b.IsEnabled = false; Result(mounted ? L.T("Desmontando…") : L.T("Montando…"), Palette.Muted);
                var error = mounted ? await BlockDevices.UnmountAsync(p, lifetime) : await BlockDevices.MountAsync(p, lifetime);
                Result(error ?? (mounted ? L.T("Desmontada.") : L.T("Montada.")), error is null ? Palette.Success : Palette.Warn);
                await RefreshAsync();
            };
            actions.Children.Add(b);
        }
        if (disk.IsExternal && !disk.IsSystem && !p.IsSystem)
        {
            var f = new Button { Name = "BtnFormatPart_" + p.Name, Content = L.T("Formatar…"), FontSize = 12, Padding = new Thickness(12, 5), Foreground = Palette.Error };
            f.Click += async (_, _) => await FormatAsync(disk, p);
            actions.Children.Add(f);
        }
        if (p.IsSystem) actions.Children.Add(UiKit.Tag(L.T("sistema"), Palette.Muted));
        var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(actions, 1); row.Children.Add(info); row.Children.Add(actions);
        return new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = row };
    }
}

/// <summary>Format a partition of a pendrive or card: file system, name, and a box that must be ticked before anything is erased.</summary>
public sealed class FormatDialog : Window
{
    public BlockDevice Target { get; private set; }
    public string FsType { get; private set; } = "exfat";
    public string FsName { get; private set; } = "exFAT";
    public string Label { get; private set; } = "";

    public FormatDialog(BlockDevice disk, BlockDevice target)
    {
        Target = target;
        Icon = AppIcon.Window; Title = L.T("Formatar"); Width = 540; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Panel; Foreground = Palette.Fg;
        var parts = disk.Partitions.ToList();
        var partBox = new ComboBox { Name = "FormatPartition", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = parts.Select(p => $"{p.Path} · {p.FsType ?? "—"} · {p.Label ?? ""}").ToList(), SelectedIndex = Math.Max(0, parts.FindIndex(p => p.Path == target.Path)), IsVisible = parts.Count > 1 };
        AutomationProperties.SetName(partBox, L.T("Partição"));
        var typeBox = new ComboBox { Name = "FormatType", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = BlockDevices.FormatTypes.Select(t => t.Name).ToList(), SelectedIndex = 0 };
        AutomationProperties.SetName(typeBox, L.T("Sistema de arquivos"));
        var note = new TextBlock { Text = BlockDevices.FormatTypes[0].Note, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        typeBox.SelectionChanged += (_, _) => { if (typeBox.SelectedIndex >= 0) note.Text = BlockDevices.FormatTypes[typeBox.SelectedIndex].Note; };
        var label = new TextBox { Name = "FormatLabel", Text = "PENDRIVE", MaxLength = 11, PlaceholderText = L.T("Nome (até 11 letras)") };
        AutomationProperties.SetName(label, L.T("Nome do dispositivo"));
        var sure = new CheckBox { Name = "FormatConfirm", Content = L.T("Entendo que tudo nesta partição será apagado para sempre.") };
        var ok = new Button { Name = "FormatOk", Content = L.T("Formatar"), Padding = new Thickness(20, 8), IsEnabled = false, Foreground = Palette.Error };
        var cancel = new Button { Name = "FormatCancel", Content = L.T("Cancelar"), IsCancel = true, Padding = new Thickness(20, 8) };
        sure.IsCheckedChanged += (_, _) => ok.IsEnabled = sure.IsChecked == true;
        ok.Click += (_, _) =>
        {
            if (parts.Count > 0 && partBox.SelectedIndex >= 0) Target = parts[partBox.SelectedIndex];
            var t = BlockDevices.FormatTypes[Math.Max(0, typeBox.SelectedIndex)]; FsType = t.Type; FsName = t.Name; Label = (label.Text ?? "").Trim();
            Close(true);
        };
        cancel.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Background = IlmenyxTheme.Ambient(), Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = L.T("Formatar ") + (disk.Model ?? disk.Name), FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = L.T("Tudo o que estiver na partição escolhida será apagado. Copie antes o que quiser guardar."), Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap },
                partBox, UiKit.Label(L.T("SISTEMA DE ARQUIVOS")), typeBox, note, UiKit.Label(L.T("NOME")), label, sure,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
        Opened += (_, _) => cancel.Focus();
    }
}
