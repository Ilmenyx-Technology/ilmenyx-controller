// New Ilmenyx additions (layout follows the original Disks page: header, refresh, verdict cards, footnote): see LICENSING.md.
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
    private readonly TextBlock status = new() { Name = "DiskHealthStatus", Text = "Saúde, desgaste e temperatura de cada disco", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, SmartDiagnostic> smart = new(StringComparer.Ordinal);
    private bool busy;

    public IReadOnlyList<HealthCard> Cards { get; private set; } = [];

    public DiskHealthPage(SmartImportViewModel smartImport, Func<string, Task<string?>> destination, CancellationToken lifetime)
    {
        this.smartImport = smartImport; this.lifetime = lifetime; Spacing = 4;
        var refresh = new Button { Name = "BtnDiskRefresh", Content = "ATUALIZAR", Padding = new Thickness(18, 9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        refresh.Click += async (_, _) => await RefreshAsync();
        var header = new Grid { Margin = new Thickness(0, 4, 0, 18) };
        header.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 160, 0), Children = { new TextBlock { Text = "Discos", FontSize = 32, FontWeight = FontWeight.Bold }, status } });
        header.Children.Add(refresh);
        Children.Add(header); Children.Add(cards);
        Children.Add(new TextBlock
        {
            Text = "Os dados vêm do sysfs, do sensor de temperatura do próprio disco (quando o kernel expõe) e do S.M.A.R.T. consultado por você ou importado do smartctl. Pendrives e alguns HDs externos não informam saúde. O veredito é uma estimativa: faça backup de qualquer disco com aviso, não só dos críticos.",
            Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 18)
        });
        Children.Add(new SmartImportPanel(smartImport, destination, lifetime));
        smartImport.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(smartImport.Report) && smartImport.Report is { } report)
            { smart[report.Diagnostic.Device] = report.Diagnostic; await RefreshAsync(); }
        };
        Loaded += async (_, _) => { if (Cards.Count == 0) await RefreshAsync(); };
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
        catch (OperationCanceledException) { status.Text = "Leitura cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Falhou: " + e.Message; }
        finally { busy = false; }
    }

    private void Rebuild()
    {
        cards.Children.Clear();
        foreach (var card in Cards) cards.Children.Add(CardView(card));
    }

    private Control CardView(HealthCard c)
    {
        var query = new Button { Name = "SmartQuery_" + c.Device, Content = "Consultar SMART (smartctl, somente leitura)", Margin = new Thickness(0, 10, 0, 0) };
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
