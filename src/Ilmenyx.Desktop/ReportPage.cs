// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions (the original exports one HTML/JSON report of everything the app shows): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>General report export. Collection is read-only and local; the file is new (never overwritten) and written atomically.</summary>
public sealed class ReportPage : StackPanel
{
    private readonly Func<CancellationToken, Task<ReportData>> gather;
    private readonly Func<string, Task<string?>> destination;
    private readonly CancellationToken lifetime;
    private readonly Button html = UiKit.Primary("BtnReportHtml", "EXPORTAR HTML", 22, 11), json = new() { Name = "BtnReportJson", Content = "EXPORTAR JSON", Padding = new Thickness(22, 11) };
    private readonly TextBlock status = new() { Name = "ReportStatus", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private bool busy;
    public ReportData? LastData { get; private set; }
    public string StatusText => status.Text ?? "";

    public ReportPage(Func<CancellationToken, Task<ReportData>> gather, Func<string, Task<string?>> destination, CancellationToken lifetime)
    {
        this.gather = gather; this.destination = destination; this.lifetime = lifetime; Spacing = 4;
        Children.Add(UiKit.PageHeader("Relatório", "Um único arquivo com tudo o que o Ilmenyx lê da máquina"));
        AutomationProperties.SetName(html, "Exportar relatório em HTML"); AutomationProperties.SetName(json, "Exportar relatório em JSON");
        html.Click += async (_, _) => await ExportAsync(true); json.Click += async (_, _) => await ExportAsync(false);
        Children.Add(UiKit.FeatureCard(new StackPanel { Spacing = 10, Children = {
            UiKit.Label("O QUE ENTRA"),
            new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Sistema (CPU, memória, placa-mãe, SO, rede), placas de vídeo, saúde dos discos, bateria e energia, sensores no momento da exportação, o último teste de rede desta sessão e o histórico local de benchmarks e verificações." },
            UiKit.Label("O QUE FICA DE FORA"),
            new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Números de série, nome do computador e do usuário, endereços MAC e IP e caminhos da pasta pessoal (minimização de identificadores óbvios, não anonimização: revise antes de compartilhar). Nenhum SMART é consultado, nenhum benchmark roda e nenhum teste de rede é feito ao exportar; SMART aparece só se você importou um relatório na página Discos." },
            new TextBlock { Text = "A leitura é local e somente leitura. O arquivo é novo (nunca sobrescreve) e o HTML não tem scripts nem recursos remotos.", FontSize = 12, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap },
            new WrapPanel { Children = { html, json } }, status } }));
    }

    public async Task ExportAsync(bool asHtml)
    {
        if (busy) return;
        busy = true; html.IsEnabled = json.IsEnabled = false;
        try
        {
            string? path = await destination(asHtml ? ".html" : ".json");
            if (path is null) { status.Text = "Exportação cancelada."; return; }
            status.Text = "Lendo as fontes locais (somente leitura)…";
            var data = await gather(lifetime); LastData = data;
            await GeneralReport.ExportAsync(data, path, asHtml, lifetime);
            status.Text = $"Relatório salvo em {path} · {data.Disks.Count} disco(s), {data.Gpus.Count} GPU(s), {data.Sensors.Count} sensor(es), {data.Benchmarks.Count} teste(s) no histórico.";
        }
        catch (OperationCanceledException) { status.Text = "Exportação cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { status.Text = "Não foi possível exportar. Escolha um nome novo: " + e.Message; }
        finally { busy = false; html.IsEnabled = json.IsEnabled = true; }
    }
}
