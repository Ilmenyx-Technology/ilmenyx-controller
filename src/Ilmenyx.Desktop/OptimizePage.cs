// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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

public sealed record OptimizeServices(Func<CancellationToken, Task<string?>> GetPowerProfile, Func<string, CancellationToken, Task<bool>> SetPowerProfile)
{
    /// <summary>User environment.d file for the bigger shader cache; default: the real one under ~/.config/environment.d.</summary>
    public LinuxShaderCache? ShaderCache { get; init; }
    /// <summary>Steam folder, to show the size of Steam's own shader cache (null = not shown).</summary>
    public Func<string?>? SteamRoot { get; init; }
}

public sealed class OptimizePage : StackPanel
{
    private readonly LinuxOptimizer optimizer;
    private readonly OptimizeServices services;
    private readonly UsageViewModel usage;
    private readonly string settingsPath;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly IlmenyxSettings settings;
    private readonly MeterView ramMeter = new(L.T("Em uso"), "OptRamBar");
    private readonly TextBlock result = new() { Name = "OptResult", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel appList = new() { Name = "OptApps" };
    private readonly StackPanel tweakList = new() { Name = "OptTweaks" };
    private readonly Button optimize = UiKit.Primary("BtnOptimize", L.T("Otimiza"), 30, 14);
    private IReadOnlyList<BackgroundApp> apps = [];
    public IReadOnlyList<BackgroundApp> Apps => apps;
    public IlmenyxSettings Settings => settings;

    public ZramPanel? Zram { get; }

    public OptimizePage(LinuxOptimizer optimizer, OptimizeServices services, UsageViewModel usage, string settingsPath, Func<Window?> owner, CancellationToken lifetime, ZramServices? zram = null)
    {
        this.optimizer = optimizer; this.services = services; this.usage = usage; this.settingsPath = settingsPath; this.owner = owner; this.lifetime = lifetime; Spacing = 4;
        settings = IlmenyxSettings.Load(settingsPath);
        Children.Add(UiKit.PageHeader(L.T("Otimiza"), L.T("Fecha o que atrapalha e ajusta o sistema para jogos. O que exige a senha do sistema (ZRAM) só roda depois da sua confirmação.")));
        optimize.Click += async (_, _) => await OptimizeAsync();
        var ram = new Grid { ColumnDefinitions = new("*,Auto") };
        var info = new StackPanel { Children = { UiKit.Label(L.T("MEMÓRIA RAM")), new Border { Height = 6 }, ramMeter, result } };
        Grid.SetColumn(optimize, 1); optimize.VerticalAlignment = VerticalAlignment.Center; optimize.Margin = new Thickness(24, 0, 0, 0); ram.Children.Add(info); ram.Children.Add(optimize);
        var memoryCard = UiKit.FeatureCard(ram);
        memoryCard.Margin = new Thickness(0, 0, 0, 14); Children.Add(memoryCard);
        var refresh = new Button { Name = "BtnOptRefresh", Content = L.T("Atualizar"), Padding = new Thickness(10, 4), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        refresh.Click += async (_, _) => await RefreshAppsAsync();
        var left = UiKit.Card(new StackPanel { Children = { UiKit.SectionHeader(L.T("PROGRAMAS PARA FECHAR"), refresh), new Border { Height = 8 }, appList, new TextBlock { Text = L.T("Só aparecem aplicativos instalados (entradas .desktop) e auxiliares sugeridos, do seu usuário. Fechar envia SIGTERM (como fechar a janela); nada é forçado nem encerrado à força."), Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) } } }, new Thickness(0, 0, 8, 14));
        var restore = new Button { Name = "BtnTweaksRestore", Content = L.T("Restaurar originais"), Padding = new Thickness(10, 4), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        restore.Click += async (_, _) => await RestoreAsync();
        var right = UiKit.Card(new StackPanel { Children = { UiKit.SectionHeader(L.T("AJUSTES DO SISTEMA PARA JOGOS"), restore), new Border { Height = 8 }, tweakList } }, new Thickness(8, 0, 0, 14));
        var columns = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto") }; columns.Children.Add(left); columns.Children.Add(right);
        columns.SizeChanged += (_, _) =>
        {
            bool wide = columns.Bounds.Width >= 1000;
            Grid.SetColumnSpan(left, wide ? 1 : 2); Grid.SetColumn(right, wide ? 1 : 0);
            Grid.SetColumnSpan(right, wide ? 1 : 2); Grid.SetRow(right, wide ? 0 : 1);
            left.Margin = new Thickness(0, 0, wide ? 8 : 0, 14); right.Margin = new Thickness(wide ? 8 : 0, 0, 0, 14);
        };
        Children.Add(columns);
        if (zram is not null) { Zram = new ZramPanel(zram, owner, lifetime) { Name = "OptZram" }; Children.Add(UiKit.Card(Zram)); }
        Loaded += async (_, _) => { await RefreshAppsAsync(); await RefreshTweaksAsync(); };
        usage.Updated += UiKit.OnScreen(this, UpdateRam); UpdateRam();
    }

    private void UpdateRam()
    {
        if (usage.Last is { MemoryTotal: > 0, MemoryUsed: { } u } l) { ramMeter.Set(L.T($"{RamInfoBuilder.Bytes(u)} de {RamInfoBuilder.Bytes(l.MemoryTotal!.Value)} · {100.0 * u / l.MemoryTotal.Value:0}%"), (double)u / l.MemoryTotal.Value); }
    }

    public async Task RefreshAppsAsync()
    {
        apps = await Task.Run(() => optimizer.ListApps(), lifetime);
        appList.Children.Clear();
        if (apps.Count == 0) appList.Children.Add(new TextBlock { Name = "OptAppsEmpty", Text = L.T("Nenhum programa para fechar."), Foreground = Palette.Muted });
        foreach (var app in apps)
        {
            var check = new CheckBox { Name = "OptApp_" + app.ProcessName, IsChecked = app.Selected, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
            AutomationProperties.SetName(check, L.T("Fechar ") + app.Title);
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
        if (chosen.Count == 0) { result.Text = L.T("Nenhum programa selecionado. Marque o que deseja fechar."); return; }
        if (owner() is not { } window) return;
        string names = string.Join("\n", chosen.Take(12).Select(a => $"  • {a.Title} ({a.MemoryText})")) + (chosen.Count > 12 ? L.T($"\n  … e mais {chosen.Count - 12}") : "");
        if (!await ConfirmDialog.AskAsync(window, L.T("Otimiza"), L.T($"Pedir para fechar {chosen.Count} programa(s)?\n\n{names}\n\nTrabalho não salvo pode ser perdido. Os programas são avisados (SIGTERM), não encerrados à força."), L.T("Fechar"), L.T("Cancelar"))) return;
        optimize.IsEnabled = false; result.Text = L.T("Fechando…");
        try
        {
            long before = AvailableMemory();
            var r = await optimizer.CloseAsync(chosen, lifetime);
            await Task.Delay(300, lifetime);
            long freed = Math.Max(0, AvailableMemory() - before);
            result.Text = L.T($"Fechados: {(r.Closed.Count > 0 ? string.Join(", ", r.Closed) : L.T("nenhum"))}.") + (r.NotClosed.Count > 0 ? L.T($" Não fecharam: {string.Join(", ", r.NotClosed)}.") : "") + L.T($" Memória liberada: {RamInfoBuilder.Bytes(freed)}. O cache de disco do kernel é gerenciado sozinho; liberar à mão exige root (sync; echo 1 | sudo tee /proc/sys/vm/drop_caches).");
            await RefreshAppsAsync();
        }
        catch (OperationCanceledException) { result.Text = "Cancelado."; }
        finally { optimize.IsEnabled = true; }
    }

    private long AvailableMemory() { var l = usage.Last; return l?.MemoryTotal is { } t && l.MemoryUsed is { } u ? t - u : 0; }

    /// <summary>Applies the backup change to the current file (atomic, keeps what other pages saved) and refreshes this page's copy.</summary>
    private void Save(Action<IlmenyxSettings> change)
    {
        try { settings.TweakBackup = IlmenyxSettings.Update(settingsPath, change).TweakBackup; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { change(settings); result.Text = L.T("Não foi possível salvar o backup dos ajustes: ") + e.Message; }
    }

    public async Task RefreshTweaksAsync()
    {
        // Everything slow is read first, so the list is rebuilt in one go (no half-built list between awaits).
        var cache = ShaderCache; string? steamRoot = services.SteamRoot?.Invoke();
        var shader = await Task.Run(() => (cache.IsOn, cache.ForeignFile, cache.SessionValue, Sizes: cache.Caches(steamRoot)), lifetime);
        string? plan = await services.GetPowerProfile(lifetime);
        string? mouse = LinuxOptimizer.IsGnome() ? await optimizer.MouseAccelProfileAsync(lifetime) : null;
        tweakList.Children.Clear();
        AddTweak(new GameTweak { Id = "power", Title = L.T("Perfil de energia: Alto desempenho"), Adjustable = plan is not null, IsOn = plan == "performance",
            Description = L.T("Mantém a CPU pronta para subir o clock na hora, sem economia agressiva de energia (power-profiles-daemon, sem root)."),
            Effect = L.T("Menos engasgos e 1% low melhor em alguns jogos. Em notebooks gasta mais bateria."), Status = plan is null ? L.T("power-profiles-daemon indisponível.") : "Atual: " + LinuxPowerReader.ProfileName(plan) }, async on =>
        {
            if (!settings.TweakBackup.ContainsKey("power")) Save(s => s.TweakBackup.TryAdd("power", plan ?? "balanced")); // first original wins, also across instances
            return await services.SetPowerProfile(on ? "performance" : (settings.TweakBackup["power"] is "performance" or null ? "balanced" : settings.TweakBackup["power"]!), lifetime);
        });
        AddTweak(new GameTweak { Id = "mouse", Title = L.T("Desligar aceleração do mouse"), Adjustable = mouse is not null, IsOn = mouse == "flat",
            Description = L.T("Perfil de aceleração 'flat': o cursor anda sempre a mesma distância para o mesmo movimento (GNOME)."),
            Effect = L.T("Não muda FPS, mas deixa a mira consistente em jogos de tiro. Em KDE/outros desktops ajuste em Configurações > Mouse (perfil plano)."),
            Status = mouse is null ? L.T("Disponível só no GNOME (gsettings).") : "Atual: " + mouse }, async on =>
        {
            if (!settings.TweakBackup.ContainsKey("mouse")) Save(s => s.TweakBackup.TryAdd("mouse", mouse ?? "default"));
            return await optimizer.SetMouseAccelProfileAsync(on ? "flat" : (settings.TweakBackup["mouse"] is "flat" or null ? "default" : settings.TweakBackup["mouse"]!), lifetime);
        });
        AddShaderCache(shader.IsOn, shader.ForeignFile, shader.SessionValue, shader.Sizes);
        foreach (var t in optimizer.ReadOnlyTweaks()) AddTweak(t, null);
    }

    private LinuxShaderCache ShaderCache => services.ShaderCache ?? (shaderCache ??= new LinuxShaderCache());
    private LinuxShaderCache? shaderCache;

    /// <summary>Detect (file, session value, cache sizes) → explain → confirm → write the user's environment.d file; off deletes it.</summary>
    private void AddShaderCache(bool on, bool foreign, string? session, IReadOnlyList<(string Label, long? Bytes)> sizes)
    {
        var cache = ShaderCache;
        var t = ShaderCacheTweak.Describe(on, foreign, session, sizes, cache.ConfigPath);
        AddTweak(t, async turnOn =>
        {
            if (turnOn)
            {
                if (owner() is not { } window || !await ConfirmDialog.AskAsync(window, L.T("Cache de shaders maior"),
                    L.T($"Gravar o arquivo abaixo, só para o seu usuário (sem senha)?\n\n{cache.ConfigPath}\n\n{ShaderCacheTweak.MesaVariable}={ShaderCacheTweak.MesaValue}\n{ShaderCacheTweak.NvidiaVariable}={ShaderCacheTweak.NvidiaBytes}\n\n") +
                    L.T("• Vale a partir do próximo login (saia da sessão e entre de novo) e já nos jogos abertos pelo Ilmenyx.\n• O cache pode chegar a 10 GB em disco, crescendo só conforme os jogos precisarem.\n• Não troca o driver de vídeo nem mexe em nada do sistema.\n• Para desfazer, desligue esta opção (o arquivo é apagado)."), L.T("Gravar"), L.T("Cancelar"))) return false;
                if (cache.Apply() is { } error) { result.Text = error; return false; }
                if (!settings.TweakBackup.ContainsKey("shadercache")) Save(s => s.TweakBackup.TryAdd("shadercache", "<absent>"));
                return true;
            }
            if (cache.Undo() is { } undoError) { result.Text = undoError; return false; }
            return true;
        }, on => on ? L.T("Ligado. Vale a partir do próximo login (e já nos jogos abertos pelo Ilmenyx).") : L.T("Desligado: arquivo apagado. O limite volta ao padrão no próximo login."));
    }

    private void AddTweak(GameTweak t, Func<bool, Task<bool>>? apply, Func<bool, string>? done = null)
    {
        var toggle = new CheckBox { Name = "Tweak_" + t.Id, IsChecked = t.IsOn, IsEnabled = apply is not null && t.Adjustable, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        AutomationProperties.SetName(toggle, t.Title);
        var status = new TextBlock { Name = "TweakStatus_" + t.Id, Text = t.Status, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        toggle.Click += async (_, _) =>
        {
            bool on = toggle.IsChecked == true;
            toggle.IsEnabled = false;
            try { bool ok = await apply!(on); status.Text = ok ? done?.Invoke(on) ?? (on ? "Ligado." : "Desligado.") + L.T(" Vale agora.") : L.T("Não foi possível alterar (ou cancelado)."); if (!ok) toggle.IsChecked = !on; }
            finally { toggle.IsEnabled = true; }
        };
        var head = new Grid(); head.Children.Add(new TextBlock { Text = t.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 110, 0) });
        if (t.Badge.Length > 0) head.Children.Add(new TextBlock { Text = t.Badge, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
        var effect = new TextBox { Text = t.Effect, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Palette.Faint, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 0) };
        var body = new StackPanel { Children = { head, new TextBlock { Text = t.Description, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }, effect, status } };
        var row = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new Thickness(0, 0, 0, 14) }; Grid.SetColumn(body, 1); row.Children.Add(toggle); row.Children.Add(body);
        tweakList.Children.Add(row);
    }

    private async Task RestoreAsync()
    {
        var done = new List<string>();
        if (settings.TweakBackup.TryGetValue("power", out var plan) && plan is not null && await services.SetPowerProfile(plan, lifetime)) done.Add(L.T("perfil de energia"));
        if (settings.TweakBackup.TryGetValue("mouse", out var mouse) && mouse is not null && await optimizer.SetMouseAccelProfileAsync(mouse, lifetime)) done.Add(L.T("aceleração do mouse"));
        if (settings.TweakBackup.ContainsKey("shadercache") && ShaderCache.IsOn && ShaderCache.Undo() is null) done.Add(L.T("cache de shaders (arquivo apagado)"));
        var restored = settings.TweakBackup.Keys.ToList();
        Save(s => { foreach (var k in restored) s.TweakBackup.Remove(k); });
        result.Text = done.Count > 0 ? "Restaurado: " + string.Join(", ", done) + "." : L.T("Nada para restaurar (nenhum ajuste foi alterado pelo Ilmenyx).");
        await RefreshTweaksAsync();
    }
}
