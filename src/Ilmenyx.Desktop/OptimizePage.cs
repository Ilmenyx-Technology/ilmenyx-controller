// New Ilmenyx additions (layout follows the original RESET OTIMIZA page: RAM card with OTIMIZA, programs to close, game tweaks): see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed record OptimizeServices(Func<CancellationToken, Task<string?>> GetPowerProfile, Func<string, CancellationToken, Task<bool>> SetPowerProfile);

public sealed class OptimizePage : StackPanel
{
    private readonly LinuxOptimizer optimizer;
    private readonly OptimizeServices services;
    private readonly UsageViewModel usage;
    private readonly string settingsPath;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly IlmenyxSettings settings;
    private readonly TextBlock ramText = new() { Name = "OptRamText", FontFamily = new FontFamily("monospace"), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly MeterView ramMeter = new("Em uso", "OptRamBar");
    private readonly TextBlock result = new() { Name = "OptResult", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel appList = new() { Name = "OptApps" };
    private readonly StackPanel tweakList = new() { Name = "OptTweaks" };
    private readonly Button optimize = UiKit.Primary("BtnOptimize", "OTIMIZA", 30, 14);
    private IReadOnlyList<BackgroundApp> apps = [];
    public IReadOnlyList<BackgroundApp> Apps => apps;
    public IlmenyxSettings Settings => settings;

    public OptimizePage(LinuxOptimizer optimizer, OptimizeServices services, UsageViewModel usage, string settingsPath, Func<Window?> owner, CancellationToken lifetime)
    {
        this.optimizer = optimizer; this.services = services; this.usage = usage; this.settingsPath = settingsPath; this.owner = owner; this.lifetime = lifetime; Spacing = 4;
        settings = IlmenyxSettings.Load(settingsPath);
        Children.Add(UiKit.PageHeader("Otimiza", "Fecha o que atrapalha e ajusta o sistema para jogos (somente o que não exige root)"));
        optimize.Click += async (_, _) => await OptimizeAsync();
        var ram = new Grid { ColumnDefinitions = new("*,Auto") };
        var info = new StackPanel { Children = { new Grid { Children = { UiKit.Label("MEMÓRIA RAM"), ramText } }, new Border { Height = 6 }, ramMeter, result } };
        Grid.SetColumn(optimize, 1); optimize.VerticalAlignment = VerticalAlignment.Center; optimize.Margin = new Thickness(24, 0, 0, 0); ram.Children.Add(info); ram.Children.Add(optimize);
        var memoryCard = UiKit.FeatureCard(ram);
        memoryCard.Margin = new Thickness(0, 0, 0, 14); Children.Add(memoryCard);
        var refresh = new Button { Name = "BtnOptRefresh", Content = "ATUALIZAR", Padding = new Thickness(10, 4), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        refresh.Click += async (_, _) => await RefreshAppsAsync();
        var left = UiKit.Card(new StackPanel { Children = { UiKit.SectionHeader("PROGRAMAS PARA FECHAR", refresh), new Border { Height = 8 }, appList, new TextBlock { Text = "Só aparecem aplicativos instalados (entradas .desktop) e auxiliares sugeridos, do seu usuário. Fechar envia SIGTERM (como fechar a janela); nada é forçado nem encerrado à força.", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) } } }, new Thickness(0, 0, 8, 14));
        var restore = new Button { Name = "BtnTweaksRestore", Content = "RESTAURAR ORIGINAIS", Padding = new Thickness(10, 4), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        restore.Click += async (_, _) => await RestoreAsync();
        var right = UiKit.Card(new StackPanel { Children = { UiKit.SectionHeader("AJUSTES DO SISTEMA PARA JOGOS", restore), new Border { Height = 8 }, tweakList } }, new Thickness(8, 0, 0, 14));
        var columns = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto") }; columns.Children.Add(left); columns.Children.Add(right);
        columns.SizeChanged += (_, _) =>
        {
            bool wide = columns.Bounds.Width >= 1000;
            Grid.SetColumnSpan(left, wide ? 1 : 2); Grid.SetColumn(right, wide ? 1 : 0);
            Grid.SetColumnSpan(right, wide ? 1 : 2); Grid.SetRow(right, wide ? 0 : 1);
            left.Margin = new Thickness(0, 0, wide ? 8 : 0, 14); right.Margin = new Thickness(wide ? 8 : 0, 0, 0, 14);
        };
        Children.Add(columns);
        Loaded += async (_, _) => { await RefreshAppsAsync(); await RefreshTweaksAsync(); };
        usage.Updated += UpdateRam; UpdateRam();
    }

    private void UpdateRam()
    {
        if (usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l) { ramText.Text = $"{RamInfoBuilder.Bytes(u)} de {RamInfoBuilder.Bytes(l.MemoryTotal!.Value)} · {100.0 * u / l.MemoryTotal.Value:0}%"; ramMeter.Set(ramText.Text, (double)u / l.MemoryTotal.Value); }
    }

    public async Task RefreshAppsAsync()
    {
        apps = await Task.Run(() => optimizer.ListApps(), lifetime);
        appList.Children.Clear();
        if (apps.Count == 0) appList.Children.Add(new TextBlock { Name = "OptAppsEmpty", Text = "Nenhum programa para fechar.", Foreground = Palette.Muted });
        foreach (var app in apps)
        {
            var check = new CheckBox { Name = "OptApp_" + app.ProcessName, IsChecked = app.Selected, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
            AutomationProperties.SetName(check, "Fechar " + app.Title);
            check.IsCheckedChanged += (_, _) => app.Selected = check.IsChecked == true;
            var text = new StackPanel { Children = { new TextBlock { Text = app.Title, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = app.Detail, Foreground = Palette.Faint, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis } } };
            var mem = new TextBlock { Text = app.MemoryText, FontFamily = new FontFamily("monospace"), Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 8) }; Grid.SetColumn(text, 1); Grid.SetColumn(mem, 2); row.Children.Add(check); row.Children.Add(text); row.Children.Add(mem);
            appList.Children.Add(row);
        }
    }

    public async Task OptimizeAsync()
    {
        var chosen = apps.Where(a => a.Selected).ToList();
        if (chosen.Count == 0) { result.Text = "Nenhum programa selecionado. Marque o que deseja fechar."; return; }
        if (owner() is not { } window) return;
        string names = string.Join("\n", chosen.Take(12).Select(a => $"  • {a.Title} ({a.MemoryText})")) + (chosen.Count > 12 ? $"\n  … e mais {chosen.Count - 12}" : "");
        if (!await ConfirmDialog.AskAsync(window, "Otimiza", $"Pedir para fechar {chosen.Count} programa(s)?\n\n{names}\n\nTrabalho não salvo pode ser perdido. Os programas são avisados (SIGTERM), não encerrados à força.", "Fechar", "Cancelar")) return;
        optimize.IsEnabled = false; result.Text = "Fechando…";
        try
        {
            long before = AvailableMemory();
            var r = await optimizer.CloseAsync(chosen, lifetime);
            await Task.Delay(300, lifetime);
            long freed = Math.Max(0, AvailableMemory() - before);
            result.Text = $"Fechados: {(r.Closed.Count > 0 ? string.Join(", ", r.Closed) : "nenhum")}." + (r.NotClosed.Count > 0 ? $" Não fecharam: {string.Join(", ", r.NotClosed)}." : "") + $" Memória liberada: {RamInfoBuilder.Bytes(freed)}. O cache de disco do kernel é gerenciado sozinho; liberar à mão exige root (sync; echo 1 | sudo tee /proc/sys/vm/drop_caches).";
            await RefreshAppsAsync();
        }
        catch (OperationCanceledException) { result.Text = "Cancelado."; }
        finally { optimize.IsEnabled = true; }
    }

    private long AvailableMemory() { var l = usage.Last; return l?.MemoryTotal is { } t && l.MemoryUsed is { } u ? t - u : 0; }

    private void Save() { try { settings.Save(settingsPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { result.Text = "Não foi possível salvar o backup dos ajustes: " + e.Message; } }

    public async Task RefreshTweaksAsync()
    {
        tweakList.Children.Clear();
        string? plan = await services.GetPowerProfile(lifetime);
        AddTweak(new GameTweak { Id = "power", Title = "Perfil de energia: Alto desempenho", Adjustable = plan is not null, IsOn = plan == "performance",
            Description = "Mantém a CPU pronta para subir o clock na hora, sem economia agressiva de energia (power-profiles-daemon, sem root).",
            Effect = "Menos engasgos e 1% low melhor em alguns jogos. Em notebooks gasta mais bateria.", Status = plan is null ? "power-profiles-daemon indisponível." : "Atual: " + LinuxPowerReader.ProfileName(plan) }, async on =>
        {
            if (!settings.TweakBackup.ContainsKey("power")) { settings.TweakBackup["power"] = plan ?? "balanced"; Save(); }
            return await services.SetPowerProfile(on ? "performance" : (settings.TweakBackup["power"] is "performance" or null ? "balanced" : settings.TweakBackup["power"]!), lifetime);
        });
        string? mouse = LinuxOptimizer.IsGnome() ? await optimizer.MouseAccelProfileAsync(lifetime) : null;
        AddTweak(new GameTweak { Id = "mouse", Title = "Desligar aceleração do mouse", Adjustable = mouse is not null, IsOn = mouse == "flat",
            Description = "Perfil de aceleração 'flat': o cursor anda sempre a mesma distância para o mesmo movimento (GNOME).",
            Effect = "Não muda FPS, mas deixa a mira consistente em jogos de tiro. Em KDE/outros desktops ajuste em Configurações > Mouse (perfil plano).",
            Status = mouse is null ? "Disponível só no GNOME (gsettings)." : "Atual: " + mouse }, async on =>
        {
            if (!settings.TweakBackup.ContainsKey("mouse")) { settings.TweakBackup["mouse"] = mouse ?? "default"; Save(); }
            return await optimizer.SetMouseAccelProfileAsync(on ? "flat" : (settings.TweakBackup["mouse"] is "flat" or null ? "default" : settings.TweakBackup["mouse"]!), lifetime);
        });
        foreach (var t in optimizer.ReadOnlyTweaks()) AddTweak(t, null);
    }

    private void AddTweak(GameTweak t, Func<bool, Task<bool>>? apply)
    {
        var toggle = new CheckBox { Name = "Tweak_" + t.Id, IsChecked = t.IsOn, IsEnabled = apply is not null && t.Adjustable, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        AutomationProperties.SetName(toggle, t.Title);
        var status = new TextBlock { Name = "TweakStatus_" + t.Id, Text = t.Status, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        toggle.Click += async (_, _) =>
        {
            bool on = toggle.IsChecked == true;
            toggle.IsEnabled = false;
            try { bool ok = await apply!(on); status.Text = ok ? (on ? "Ligado." : "Desligado.") + " Vale agora." : "Não foi possível alterar."; if (!ok) toggle.IsChecked = !on; }
            finally { toggle.IsEnabled = true; }
        };
        var head = new Grid(); head.Children.Add(new TextBlock { Text = t.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 110, 0) });
        if (t.Badge.Length > 0) head.Children.Add(new TextBlock { Text = t.Badge, FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
        var effect = new TextBox { Text = t.Effect, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Palette.Faint, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 0) };
        var body = new StackPanel { Children = { head, new TextBlock { Text = t.Description, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }, effect, status } };
        var row = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new Thickness(0, 0, 0, 14) }; Grid.SetColumn(body, 1); row.Children.Add(toggle); row.Children.Add(body);
        tweakList.Children.Add(row);
    }

    private async Task RestoreAsync()
    {
        var done = new List<string>();
        if (settings.TweakBackup.TryGetValue("power", out var plan) && plan is not null && await services.SetPowerProfile(plan, lifetime)) done.Add("perfil de energia");
        if (settings.TweakBackup.TryGetValue("mouse", out var mouse) && mouse is not null && await optimizer.SetMouseAccelProfileAsync(mouse, lifetime)) done.Add("aceleração do mouse");
        settings.TweakBackup.Clear(); Save();
        result.Text = done.Count > 0 ? "Restaurado: " + string.Join(", ", done) + "." : "Nada para restaurar (nenhum ajuste foi alterado pelo Ilmenyx).";
        await RefreshTweaksAsync();
    }
}
