// New Ilmenyx additions (layout follows the original Games page: search, options, data source with API key, 4-column game cards): see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class GamesFpsPage : StackPanel
{
    private readonly HowManyFpsClient api;
    private readonly IlmenyxSettings settings;
    private readonly string settingsPath;
    private readonly Func<Window?> owner;
    private readonly Func<(string Cpu, string Gpu)> hardware;
    private readonly CancellationToken lifetime;
    private readonly List<GameFpsRow> rows = GameCatalog.Games.Select(g => new GameFpsRow(g)).ToList();
    private readonly WrapPanel cards = new() { Name = "GameCards" };
    private readonly TextBlock sub = new() { Name = "GamesSub", Foreground = Palette.Muted };
    private readonly TextBox search = new() { Name = "GameSearch", Width = 260, PlaceholderText = "Buscar jogo…" };
    private readonly TextBlock sourceTitle = new() { Name = "GameSourceTitle", FontWeight = FontWeight.SemiBold };
    private readonly TextBlock sourceText = new() { Name = "GameSourceText", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBox keyBox = new() { Name = "ApiKeyBox", PasswordChar = '•', Width = 240, PlaceholderText = "Chave da API HowManyFPS" };
    private readonly Button saveKey = new() { Name = "BtnApiKey", Content = "SALVAR CHAVE", Padding = new Thickness(16, 8) };
    private readonly Button query = new() { Name = "BtnFpsQuery", Content = "CONSULTAR API", Padding = new Thickness(16, 8), IsEnabled = false };
    private string res = "1920x1080", preset = "Alto", upscaling = "Native";
    private double gpuIdx = 50, cpuIdx = 80;
    public IReadOnlyList<GameFpsRow> Rows => rows;
    public double GpuIndex => gpuIdx; public double CpuIndex => cpuIdx;

    public GamesFpsPage(HowManyFpsClient api, IlmenyxSettings settings, string settingsPath, Func<Window?> owner, Func<(string Cpu, string Gpu)> hardware, CancellationToken lifetime)
    {
        this.api = api; this.settings = settings; this.settingsPath = settingsPath; this.owner = owner; this.hardware = hardware; this.lifetime = lifetime; Spacing = 4;
        var header = new Grid { Margin = new Thickness(0, 4, 0, 20) };
        header.Children.Add(new StackPanel { Children = { new TextBlock { Text = "Estimativa de FPS", FontSize = 26, FontWeight = FontWeight.Bold }, sub } });
        search.HorizontalAlignment = HorizontalAlignment.Right; search.VerticalAlignment = VerticalAlignment.Center; search.TextChanged += (_, _) => Filter(); header.Children.Add(search);
        Children.Add(header);
        var resChips = new UiKit.ChipGroup<string>("GameResPanel", [("1080p", "1920x1080"), ("1440p", "2560x1440"), ("4K", "3840x2160")], res, v => { res = v; RefreshLocal(); });
        var presetChips = new UiKit.ChipGroup<string>("GamePresetPanel", [("Baixo", "Baixo"), ("Médio", "Médio"), ("Alto", "Alto"), ("Ultra", "Ultra")], preset, v => { preset = v; RefreshLocal(); });
        var upChips = new UiKit.ChipGroup<string>("GameUpscalePanel", [("Nativo", "Native"), ("Qualidade", "Quality"), ("Desempenho", "Performance")], upscaling, v => { upscaling = v; RefreshLocal(); });
        Control Opt(string label, Control chips) => new StackPanel { Margin = new Thickness(0, 0, 32, 0), Children = { UiKit.Label(label), new Border { Height = 4 }, chips } };
        Children.Add(UiKit.Card(new WrapPanel { Children = { Opt("RESOLUÇÃO", resChips.Panel), Opt("GRÁFICOS", presetChips.Panel), Opt("UPSCALING (DLSS / FSR)", upChips.Panel) } }));
        AutomationProperties.SetName(keyBox, "Chave da API HowManyFPS");
        saveKey.Click += (_, _) => SaveKey(); query.Click += async (_, _) => await QueryAsync();
        var src = new Grid { ColumnDefinitions = new("*,Auto") };
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Children = { keyBox, saveKey, query } };
        Grid.SetColumn(keyRow, 1); src.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { sourceTitle, sourceText } }); src.Children.Add(keyRow);
        Children.Add(UiKit.Card(src));
        Children.Add(new ScrollViewer { Name = "GameCardsScroll", Content = cards, MaxHeight = 900 });
        api.ApiKey = settings.HowManyFpsKey; keyBox.Text = api.Configured ? settings.HowManyFpsKey : ""; Loaded += (_, _) => Init();
    }

    private void Init()
    {
        var (cpu, gpu) = hardware();
        gpuIdx = GameCatalog.GpuPerf(gpu) ?? GameCatalog.GpuPerf(GameCatalog.ShortGpuName(gpu)) ?? 50; cpuIdx = GameCatalog.CpuPerf(GameCatalog.ShortCpuName(cpu)) ?? GameCatalog.CpuPerf(cpu) ?? 80;
        sub.Text = $"FPS esperado no seu PC · {(gpu.Length > 0 ? GameCatalog.ShortGpuName(gpu) : "GPU desconhecida")} · {(cpu.Length > 0 ? GameCatalog.ShortCpuName(cpu) : "CPU desconhecida")}";
        RefreshLocal();
    }

    public void RefreshLocal()
    {
        GameFpsRow.RefreshLocal(rows, gpuIdx, cpuIdx, res, preset, upscaling);
        if (api.Configured)
        {
            sourceTitle.Text = "Fonte: estimativa local + HowManyFPS sob demanda"; sourceText.Text = "A estimativa local usa o desempenho relativo da sua GPU e CPU. Com a chave, CONSULTAR API busca FPS médio e 1% low medidos em sessões reais (respostas ficam 7 dias em cache); a API usa as configurações gráficas dela.";
            saveKey.Content = "ATUALIZAR CHAVE"; query.IsEnabled = true;
        }
        else
        {
            sourceTitle.Text = "Fonte: estimativa local (aproximada)"; sourceText.Text = "Calculada a partir do desempenho relativo da sua GPU e CPU. Para FPS medido em jogo, peça uma chave da API em info@howmanyfps.com e cole ao lado. A internet só é usada quando você clicar em CONSULTAR API.";
            saveKey.Content = "SALVAR CHAVE"; query.IsEnabled = false;
        }
        Rebuild();
    }

    private void SaveKey()
    {
        settings.HowManyFpsKey = string.IsNullOrWhiteSpace(keyBox.Text) ? null : keyBox.Text.Trim(); api.ApiKey = settings.HowManyFpsKey;
        try { settings.Save(settingsPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { sourceText.Text = "Não foi possível salvar a chave: " + e.Message; return; }
        RefreshLocal();
    }

    public async Task QueryAsync()
    {
        if (!api.Configured || owner() is not { } window) return;
        var (cpuFull, gpuFull) = hardware(); string cpu = GameCatalog.ShortCpuName(cpuFull), gpu = GameCatalog.ShortGpuName(gpuFull);
        int needed = rows.Count(r => !api.IsCached(cpu, gpu, r.Name, res, upscaling));
        if (needed > 0 && !await ConfirmDialog.AskAsync(window, "HowManyFPS", $"Isto envia até {needed} consulta(s) para {HowManyFpsClient.Endpoint} (CPU \"{cpu}\", GPU \"{gpu}\", resolução e upscaling escolhidos), usando a sua chave. As respostas ficam 7 dias em cache. Consultar agora?", "Consultar", "Cancelar")) return;
        query.IsEnabled = false; int ok = 0;
        try
        {
            foreach (var row in rows)
            {
                try { var r = await api.GetAsync(cpu, gpu, row.Name, res, upscaling, lifetime); row.Set(r.AvgFps, r.Min1Fps, "HOWMANYFPS", ""); ok++; }
                catch (OperationCanceledException) { return; }
                catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
                {
                    row.Set(row.Fps, 0, "ESTIMATIVA", $"API: {e.Message}");
                    if (e.Message.Contains("chave", StringComparison.Ordinal) || e.Message.Contains("cota", StringComparison.Ordinal)) { sourceText.Text = $"A API recusou a consulta ({e.Message}). Mostrando a estimativa local."; Rebuild(); return; }
                }
            }
            sourceText.Text = $"{ok} de {rows.Count} jogos com dados da HowManyFPS; os demais usam a estimativa local."; Rebuild();
        }
        finally { query.IsEnabled = api.Configured; }
    }

    private void Filter() => Rebuild();

    private void Rebuild()
    {
        string q = (search.Text ?? "").Trim();
        cards.Children.Clear();
        foreach (var row in rows.Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            var bar = new Grid { Height = 3, Margin = new Thickness(0, 8, 0, 8), Background = Palette.Line };
            var fill = new Border { Background = Palette.Fg, HorizontalAlignment = HorizontalAlignment.Left, Width = 190 * row.Bar }; bar.Children.Add(fill);
            var top = new Grid(); top.Children.Add(new TextBlock { Text = row.FpsText, FontSize = 28, FontWeight = FontWeight.Bold, FontFamily = new FontFamily("monospace") });
            top.Children.Add(new TextBlock { Text = row.Rating, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            var meta = new Grid(); meta.Children.Add(new TextBlock { Text = row.LowText, Foreground = Palette.Muted, FontSize = 11 }); meta.Children.Add(new TextBlock { Text = row.Source, Foreground = Palette.Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right });
            var body = new StackPanel { Children = { new TextBlock { Text = row.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = row.Genre, Foreground = Palette.Muted, FontSize = 11 }, top, bar, meta, new TextBlock { Text = row.Detail, Foreground = Palette.Faint, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis } } };
            cards.Children.Add(new Border { Name = "Game_" + LauncherPage.SafeName(row.Name), Width = 222, Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 14), Margin = new Thickness(0, 0, 10, 10), Child = body });
        }
    }
}
