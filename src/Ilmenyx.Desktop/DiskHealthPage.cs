// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Disks page: header, refresh, verdict cards, footnote; overall verdict hero 2026-11-06): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class DiskHealthPage : StackPanel
{
    private readonly LinuxDiskHealthReader reader = new();
    private readonly SmartImportViewModel smartImport;
    private readonly CancellationToken lifetime;
    private readonly StackPanel cards = new() { Name = "DiskCards" };
    private readonly TextBlock status = new() { Name = "DiskHealthStatus", Text = "Saúde, desgaste e temperatura de cada disco", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock overall = new() { Name = "DiskOverall", Text = "Lendo os discos…", FontSize = 24, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel overallTags = new() { Name = "DiskOverallTags", Margin = new Thickness(0, 12, 0, 0) };
    private readonly Avalonia.Controls.Shapes.Path overallIcon = UiKit.Icon(Icons.Disks, 64, Palette.Faint);
    private readonly Dictionary<string, SmartDiagnostic> smart = new(StringComparer.Ordinal);
    private bool busy;

    public IReadOnlyList<HealthCard> Cards { get; private set; } = [];

    public DiskHealthPage(SmartImportViewModel smartImport, Func<string, Task<string?>> destination, CancellationToken lifetime)
    {
        this.smartImport = smartImport; this.lifetime = lifetime;
        var refresh = UiKit.Primary("BtnDiskRefresh", "Atualizar");
        refresh.Click += async (_, _) => await RefreshAsync();
        Children.Add(UiKit.PageHeader("Discos", "Saúde, desgaste e temperatura de cada disco", refresh));
        // Dominant element: the overall verdict, coloured by the worst disk.
        var heroText = new StackPanel { Children = { UiKit.Label("SAÚDE DOS DISCOS"), overall, overallTags, status } };
        heroText.Children[0].Margin = new Thickness(0, 0, 0, 6);
        var heroGrid = new Grid { ColumnDefinitions = new("*,Auto"), Children = { heroText } };
        overallIcon.VerticalAlignment = VerticalAlignment.Top; overallIcon.Margin = new Thickness(16, 0, 0, 0); overallIcon.Opacity = 0.85;
        Grid.SetColumn(overallIcon, 1); heroGrid.Children.Add(overallIcon);
        var hero = UiKit.Hero(heroGrid, name: "DiskHero"); hero.Height = double.NaN; hero.MinHeight = 150; FollowTheme(hero);
        Children.Add(hero);
        Children.Add(UiKit.SectionHeader("Discos detectados"));
        Children.Add(cards);
        Children.Add(new TextBlock
        {
            Text = "Os dados vêm do sysfs, do sensor de temperatura do próprio disco (quando o kernel expõe) e do S.M.A.R.T. consultado por você ou importado do smartctl. Pendrives e alguns HDs externos não informam saúde. O veredito é uma estimativa: faça backup de qualquer disco com aviso, não só dos críticos.",
            Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 18)
        });
        Children.Add(new SmartImportPanel(smartImport, destination, lifetime));
        smartImport.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(smartImport.Report) && smartImport.Report is { } report)
            { smart[report.Diagnostic.Device] = report.Diagnostic; await RefreshAsync(); }
        };
        Loaded += async (_, _) => { if (Cards.Count == 0) await RefreshAsync(); };
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

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; status.Text = "Lendo os discos…";
        try
        {
            var snapshot = smart.ToDictionary(kv => kv.Key, kv => kv.Value);
            var built = await Task.Run(() =>
            {
                string mounts = ""; try { mounts = File.ReadAllText("/proc/self/mounts"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                return reader.Read(mounts, snapshot).Select(DiskHealthEvaluator.Evaluate).ToList();
            }, lifetime);
            Cards = built; Rebuild();
            int bad = built.Count(c => c.Verdict is HealthVerdict.Warning or HealthVerdict.Critical);
            status.Text = built.Count == 0 ? "Nenhum disco encontrado."
                : bad == 0 ? $"{built.Count} disco(s) · nenhum problema encontrado · lido às {DateTime.Now:HH:mm:ss}"
                : $"{built.Count} disco(s) · {bad} precisa(m) de atenção · lido às {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) { status.Text = "Leitura cancelada."; if (Cards.Count == 0) overall.Text = "Leitura não concluída"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Falhou: " + e.Message; if (Cards.Count == 0) overall.Text = "Leitura não concluída"; }
        finally { busy = false; }
    }

    private void Rebuild()
    {
        cards.Children.Clear();
        cards.Children.Add(HealthCardView.Columns(Cards.Select(CardView).ToList()));
        int good = Cards.Count(c => c.Verdict == HealthVerdict.Good), warn = Cards.Count(c => c.Verdict == HealthVerdict.Warning), bad = Cards.Count(c => c.Verdict == HealthVerdict.Critical), unknown = Cards.Count - good - warn - bad;
        var worst = bad > 0 ? HealthVerdict.Critical : warn > 0 ? HealthVerdict.Warning : good > 0 ? HealthVerdict.Good : HealthVerdict.Unknown;
        overall.Text = Cards.Count == 0 ? "Nenhum disco encontrado" : bad + warn == 0 ? (good > 0 ? "Nenhum problema encontrado" : "Sem dados de saúde") : $"{bad + warn} disco(s) precisa(m) de atenção";
        overallIcon.Fill = HealthCardView.VerdictBrush(worst);
        overallTags.Children.Clear();
        foreach (var (n, text, verdict) in new[] { (good, "saudável(is)", HealthVerdict.Good), (warn, "com atenção", HealthVerdict.Warning), (bad, "crítico(s)", HealthVerdict.Critical), (unknown, "sem dados", HealthVerdict.Unknown) })
            if (n > 0) overallTags.Children.Add(UiKit.Tag($"{n} {text}", HealthCardView.VerdictBrush(verdict)));
    }

    private Control CardView(HealthCard c)
    {
        var query = new Button { Name = "SmartQuery_" + c.Device, Content = "Consultar SMART (smartctl, somente leitura)", FontSize = 12, Padding = new Thickness(12, 6), Margin = new Thickness(0, 12, 0, 0) };
        query.Click += async (_, _) => await QuerySmartAsync(c.Device, query);
        return HealthCardView.Build(c, query);
    }

    /// <summary>One explicit, bounded read-only query. Fails closed without privilege; never runs sudo.</summary>
    private async Task QuerySmartAsync(string device, Button button)
    {
        button.IsEnabled = false; status.Text = $"Consultando SMART de {device} (3 s, sem sudo)…";
        try
        {
            var d = await new SmartctlDiagnostic(new SmartctlQuery()).CollectAsync(device, lifetime);
            if (d.Status == "available") { smart[device] = d; status.Text = $"SMART de {device} lido."; await RefreshAsync(); }
            else status.Text = $"SMART de {device} indisponível: {d.Reason} Use `sudo smartctl -a -j /dev/{device} > arquivo.json` e importe o arquivo abaixo.";
        }
        catch (OperationCanceledException) { status.Text = "Consulta cancelada."; }
        catch (ArgumentException e) { status.Text = "Consulta recusada: " + e.Message; }
        finally { button.IsEnabled = true; }
    }
}
