// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original RESET MAKER pages: connected boards with diagnosis, chip drivers): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class MakerPortsPage : StackPanel
{
    private readonly LinuxSerialPorts ports = new();
    private readonly CancellationToken lifetime;
    private readonly StackPanel list = new() { Name = "MakerPortList" };
    private readonly TextBlock status = new() { Name = "MakerPortStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Text = L.T("Placas e conversores seriais conectados agora, com diagnóstico") };
    public IReadOnlyList<SerialRow> Rows { get; private set; } = [];

    public MakerPortsPage(CancellationToken lifetime)
    {
        this.lifetime = lifetime; Spacing = 4;
        var scan = new Button { Name = "BtnMakerPortScan", Content = L.T("Atualizar"), Background = Palette.Accent, Foreground = Palette.OnAccent, FontWeight = FontWeight.Bold, Padding = new Thickness(20, 9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        scan.Click += async (_, _) => await ScanAsync();
        var header = UiKit.PageHeader(L.T("Maker · Portas"), trailing: scan);
        Children.Add(header);
        Children.Add(status); Children.Add(list);
        Children.Add(new TextBlock { Text = L.T("O Ilmenyx lê só o sysfs e as permissões do nó em /dev: não abre a porta (isso pode reiniciar placas como Arduino/ESP), não altera grupos, serviços nem regras udev. Os comandos mostrados são sugestões para você executar."), Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        Loaded += async (_, _) => await ScanAsync();
    }

    public async Task ScanAsync()
    {
        status.Text = L.T("Procurando portas seriais USB…");
        try
        {
            Rows = await ports.ReadAsync(lifetime); list.Children.Clear();
            if (Rows.Count == 0) { list.Children.Add(new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(22, 18), Child = new TextBlock { Name = "MakerPortEmpty", Text = L.T("Nenhuma placa serial USB (ttyUSB*/ttyACM*) conectada. Conecte a placa e clique em Atualizar."), Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } }); }
            foreach (var r in Rows) list.Children.Add(Card(r));
            int bad = Rows.Count(r => !r.Diagnosis.Ok);
            status.Text = Rows.Count == 0 ? L.T("Nenhum dispositivo serial") : bad == 0 ? L.T($"{Rows.Count} dispositivo(s) · tudo funcionando · lido às {DateTime.Now:HH:mm:ss}") : L.T($"{Rows.Count} dispositivo(s) · {bad} com problema · lido às {DateTime.Now:HH:mm:ss}");
        }
        catch (OperationCanceledException) { status.Text = L.T("Leitura cancelada."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Falhou: " + e.Message; }
    }

    private static Control Card(SerialRow r)
    {
        string title = r.Port.Product.Length > 0 ? r.Port.Product : r.Chip?.Name ?? L.T("Dispositivo serial");
        var badge = new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(12, 4), Background = r.Diagnosis.Ok ? Palette.Card : Palette.WarnBg, Child = new TextBlock { Name = "MakerState_" + System.IO.Path.GetFileName(r.Port.Node), Text = r.Diagnosis.Ok ? L.T("FUNCIONANDO") : L.T("COM PROBLEMA"), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = r.Diagnosis.Ok ? Palette.Success : Palette.Warn } };
        var top = new Grid(); top.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 150, 0), Children = { new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = L.T($"{r.Port.Node} · {r.Chip?.Name ?? r.Port.Maker} · VID {r.Port.Vid} PID {r.Port.Pid}"), Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap } } });
        top.Children.Add(badge);
        var body = new StackPanel { Children = { top, new TextBlock { Text = "Driver: " + (r.Port.BoundDriver.Length > 0 ? r.Port.BoundDriver : L.T("sem driver")), Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) }, new TextBlock { Text = r.Diagnosis.Verdict, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 2), TextWrapping = TextWrapping.Wrap }, new TextBox { Text = r.Diagnosis.Advice, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) } } };
        return new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(22, 18), Margin = new Thickness(0, 0, 0, 14), Child = body };
    }
}

public sealed class MakerDriversPage : StackPanel
{
    private readonly LinuxSerialPorts ports = new();
    private readonly CancellationToken lifetime;
    private readonly StackPanel chips = new() { Name = "MakerChipList", Spacing = 14 };
    private readonly StackPanel env = new() { Name = "MakerEnv", Spacing = 6 };
    private readonly TextBlock status = new() { Name = "MakerDrvStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Text = L.T("Módulos do kernel que atendem CH340, CP210x, FTDI, Prolific e placas CDC ACM") };
    public MakerDriversPage(CancellationToken lifetime)
    {
        this.lifetime = lifetime; Spacing = 4;
        var scan = new Button { Name = "BtnMakerDrvScan", Content = L.T("Verificar"), Background = Palette.Accent, Foreground = Palette.OnAccent, FontWeight = FontWeight.Bold, Padding = new Thickness(20, 9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        scan.Click += (_, _) => Scan();
        var header = UiKit.PageHeader(L.T("Maker · Drivers seriais"), trailing: scan);
        Children.Add(header);
        Children.Add(status);
        Children.Add(Card(L.T("AMBIENTE (O QUE ATRAPALHA PLACAS SERIAIS)"), env));
        Children.Add(Card(L.T("FAMÍLIAS DE CHIP"), chips));
        Children.Add(new TextBlock { Text = L.T("No Linux esses drivers já vêm no kernel (não há .exe para instalar nem pacotes de driver para remover como no Windows). O Ilmenyx só informa e abre a página oficial; instalar módulos ou mudar permissões é com você."), Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        Loaded += (_, _) => Scan();
    }
    private static Border Card(string title, Control content) => new() { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(22, 18), Margin = new Thickness(0, 0, 0, 14), Child = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint }, content } } };

    public void Scan()
    {
        var e = ports.Environment();
        env.Children.Clear();
        env.Children.Add(new TextBlock { Name = "MakerBrltty", Text = e.BrlttyRunning ? L.T("brltty: EM EXECUÇÃO — pode capturar adaptadores CH340/CH341. Se você não usa braile: sudo systemctl disable --now brltty") : L.T("brltty: não está em execução"), TextWrapping = TextWrapping.Wrap });
        env.Children.Add(new TextBlock { Name = "MakerModemManager", Text = e.ModemManagerRunning ? L.T("ModemManager: em execução — pode sondar portas durante uploads de Arduino/ESP (regra udev ID_MM_DEVICE_IGNORE=1 resolve).") : L.T("ModemManager: não está em execução"), TextWrapping = TextWrapping.Wrap });
        env.Children.Add(new TextBlock { Text = L.T("Grupo da porta serial: dialout (Debian/Ubuntu/Fedora) ou uucp (Arch/CachyOS). Para acessar portas: sudo usermod -aG <grupo> $USER e novo login."), Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        chips.Children.Clear();
        foreach (var chip in SerialDiagnosis.Chips)
        {
            string module = chip.Modules[0]; var (available, loaded) = ports.ModuleState(module);
            string state = loaded ? L.T("CARREGADO") : available ? L.T("DISPONÍVEL (não carregado)") : L.T("AUSENTE NO KERNEL");
            var page = new Button { Name = "BtnMakerChipPage_" + module, Content = L.T("Abrir página oficial"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            page.Click += async (_, _) => { try { await DriverTools.OpenUrlAsync(chip.OfficialUrl, lifetime); status.Text = L.T("Página aberta: ") + chip.OfficialUrl; } catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or IOException or TimeoutException) { status.Text = L.T("Não foi possível abrir (") + ex.Message + "): " + chip.OfficialUrl; } };
            var head = new StackPanel { Spacing = 4 }; head.Children.Add(new TextBlock { Text = chip.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }); head.Children.Add(new TextBlock { Name = "MakerChipState_" + module, Text = state, FontSize = 11, FontWeight = FontWeight.Medium, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
            chips.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 4), Child = new StackPanel { Children = { head, new TextBlock { Text = L.T($"VID {string.Join("/", chip.Vids)} · módulo {module}"), Foreground = Palette.Muted, FontSize = 12 }, page } } });
        }
        status.Text = L.T($"Verificado às {DateTime.Now:HH:mm:ss}.");
    }
}
