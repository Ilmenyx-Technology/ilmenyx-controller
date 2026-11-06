// New Ilmenyx additions (layout follows the original Games page: search, options, data source with API key, game card grid; cinematic surfaces since 2026-11-06): see LICENSING.md.
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
    private readonly WrapPanel cards = new() { Name = "GameCards", ItemSpacing = 10, LineSpacing = 10 };
    private double cardWidth = 222;
    private readonly TextBlock sub = new() { Name = "GamesSub", Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Name = "GameSearch", Width = 240, PlaceholderText = "Buscar jogo…", Padding = new Thickness(34, 7, 10, 7) };
    private readonly TextBlock sourceTitle = new() { Name = "GameSourceTitle", FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock sourceText = new() { Name = "GameSourceText", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBox keyBox = new() { Name = "ApiKeyBox", PasswordChar = '•', Width = 240, PlaceholderText = "Chave da API HowManyFPS" };
    private readonly Button saveKey = new() { Name = "BtnApiKey", Content = "SALVAR CHAVE", Padding = new Thickness(16, 8), FontSize = 12.5 };
    private readonly Button query = UiKit.Primary("BtnFpsQuery", "CONSULTAR API", 16, 8);
    private string res = "1920x1080", preset = "Alto", upscaling = "Native";
    private double gpuIdx = 50, cpuIdx = 80;
    public IReadOnlyList<GameFpsRow> Rows => rows;
    public double GpuIndex => gpuIdx; public double CpuIndex => cpuIdx;

    public GamesFpsPage(HowManyFpsClient api, IlmenyxSettings settings, string settingsPath, Func<Window?> owner, Func<(string Cpu, string Gpu)> hardware, CancellationToken lifetime)
    {
        this.api = api; this.settings = settings; this.settingsPath = settingsPath; this.owner = owner; this.hardware = hardware; this.lifetime = lifetime; Spacing = 4;
        query.IsEnabled = false;
        var icon = UiKit.Icon(Icons.Search, 15, Palette.Faint); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); icon.IsHitTestVisible = false;
        var header = UiKit.PageHeader("Estimativa de FPS", null, new Grid { Children = { search, icon } });
        ((StackPanel)header.Children[0]).Children.Add(sub);
        search.TextChanged += (_, _) => Filter();
        Children.Add(header);
        var resChips = new UiKit.ChipGroup<string>("GameResPanel", [("1080p", "1920x1080"), ("1440p", "2560x1440"), ("4K", "3840x2160")], res, v => { res = v; RefreshLocal(); });
        var presetChips = new UiKit.ChipGroup<string>("GamePresetPanel", [("Baixo", "Baixo"), ("Médio", "Médio"), ("Alto", "Alto"), ("Ultra", "Ultra")], preset, v => { preset = v; RefreshLocal(); });
        var upChips = new UiKit.ChipGroup<string>("GameUpscalePanel", [("Nativo", "Native"), ("Qualidade", "Quality"), ("Desempenho", "Performance")], upscaling, v => { upscaling = v; RefreshLocal(); });
        Control Opt(string label, Control chips) => new StackPanel { Margin = new Thickness(0, 0, 28, 4), Children = { UiKit.Label(label), new Border { Height = 6 }, chips } };
        Children.Add(UiKit.Card(new WrapPanel { LineSpacing = 10, Children = { Opt("RESOLUÇÃO", resChips.Panel), Opt("GRÁFICOS", presetChips.Panel), Opt("UPSCALING (DLSS / FSR)", upChips.Panel) } }));
        AutomationProperties.SetName(keyBox, "Chave da API HowManyFPS");
        saveKey.Click += (_, _) => SaveKey(); query.Click += async (_, _) => await QueryAsync();
        var keyRow = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { keyBox, saveKey, query } };
        var srcHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UiKit.Icon(Icons.Network, 15, Palette.AccentText), sourceTitle } };
        Children.Add(UiKit.Card(new StackPanel { Children = { srcHead, sourceText, keyRow } }));
        Children.Add(UiKit.SectionHeader("Jogos do catálogo"));
        var cardsScroll = new ScrollViewer { Name = "GameCardsScroll", Content = cards, MaxHeight = 900 };
        // Responsive grid: as many ~220 px columns as fit, stretched to fill the row.
        cardsScroll.SizeChanged += (_, e) =>
        {
            double avail = e.NewSize.Width - 2; int cols = Math.Max(2, (int)((avail + 10) / 230));
            double w = Math.Floor((avail - 10 * (cols - 1)) / cols);
            if (Math.Abs(w - cardWidth) < 0.5) return;
            cardWidth = w; foreach (var c in cards.Children) c.Width = w;
        };
        Children.Add(cardsScroll);
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

    private static IBrush RatingBrush(double fps) => fps switch { <= 0 => Palette.Faint, >= 90 => Palette.Success, >= 60 => Palette.AccentText, >= 30 => Palette.Warn, _ => Palette.Error };

    /// <summary>Poster band of a card: a soft diagonal tint from the data palette (stable per genre), the reference's image strip without imagery.</summary>
    private static IBrush Band(string genre)
    {
        var tints = new[] { Palette.DataCpu, Palette.DataGpu, Palette.DataMemory, Palette.DataNetwork, Palette.DataDisk, Palette.DataTemperature };
        int h = 0; foreach (char c in genre) h = (h * 31 + c) & 0x7FFFFFFF;
        var c0 = ((ISolidColorBrush)tints[h % tints.Length]).Color;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb((byte)(Palette.IsLight ? 0x30 : 0x42), c0.R, c0.G, c0.B), 0), new GradientStop(Color.FromArgb(0, c0.R, c0.G, c0.B), 1) },
        };
    }

    private void Rebuild()
    {
        string q = (search.Text ?? "").Trim();
        cards.Children.Clear();
        foreach (var row in rows.Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            var tone = RatingBrush(row.Fps);
            var band = new Border
            {
                Background = Band(row.Genre), Padding = new Thickness(16, 14, 16, 10),
                Child = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = row.Name, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = row.Genre, Foreground = Palette.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis } } },
            };
            var bar = new Grid { Height = 4, Margin = new Thickness(0, 8, 0, 8) };
            bar.Children.Add(new Border { Background = Palette.Line, CornerRadius = new CornerRadius(2) });
            bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(row.Bar, 0.0001), GridUnitType.Star)); bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - row.Bar, 0.0001), GridUnitType.Star));
            var track = (Border)bar.Children[0]; Grid.SetColumnSpan(track, 2);
            bar.Children.Add(new Border { Background = tone, CornerRadius = new CornerRadius(2), IsVisible = row.Bar > 0 });
            var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { new TextBlock { Text = row.FpsText, FontSize = 28, FontWeight = FontWeight.Bold }, new TextBlock { Text = "FPS", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) } } };
            var top = new Grid { ColumnDefinitions = new("*,Auto"), Children = { value } };
            if (row.Rating.Length > 0)
            {
                var rating = new TextBlock { Text = row.Rating, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = tone, LetterSpacing = 0.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(rating, 1); top.Children.Add(rating);
            }
            var meta = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = row.LowText, Foreground = Palette.Muted, FontSize = 11 } } };
            var source = new TextBlock { Text = row.Source, Foreground = Palette.Faint, FontSize = 10.5, LetterSpacing = 0.4 }; Grid.SetColumn(source, 1); meta.Children.Add(source);
            var body = new StackPanel { Margin = new Thickness(16, 6, 16, 14), Children = { top, bar, meta, new TextBlock { Text = row.Detail, Foreground = Palette.Faint, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = row.Detail.Length > 0 } } };
            cards.Children.Add(new Border
            {
                Name = "Game_" + LauncherPage.SafeName(row.Name), Width = cardWidth, Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), ClipToBounds = true, Child = new StackPanel { Children = { band, body } },
            });
        }
    }
}
