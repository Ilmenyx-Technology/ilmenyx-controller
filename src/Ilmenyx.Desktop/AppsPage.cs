// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Install apps page: install button, progress, list with select all/none): see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class AppsPage : StackPanel
{
    private readonly LinuxAppInstaller installer;
    private readonly bool flatpakAvailable;
    private readonly Func<Window?> owner;
    private readonly Func<string, CancellationToken, Task> openUrl;
    private readonly CancellationToken lifetime;
    private readonly List<InstallableApp> apps = AppCatalog.Default();
    private readonly StackPanel list = new() { Name = "AppsList" };
    private readonly TextBlock status = new() { Name = "AppsStatus", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button install = UiKit.Primary("BtnAppsInstall", "INSTALAR SELECIONADOS");
    private readonly Button flathub = new() { Name = "BtnAppsFlathub", Content = "ADICIONAR FLATHUB (SOMENTE PARA O SEU USUÁRIO)", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Border progress = new() { Name = "AppsProgress", Background = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Left, Height = 3 };
    private readonly Grid track = new() { Height = 3, Margin = new Thickness(0, 0, 0, 8), Background = Palette.Line };
    private bool busy, stopRequested; private double fraction;
    public IReadOnlyList<InstallableApp> Apps => apps;

    private static (string Id, string IdLike) Distro()
    {
        try { var text = File.ReadAllText(Ilmenyx.Platform.Linux.HostSpawn.HostPath("/etc/os-release")); string F(string k) => text.Split('\n').Where(l => l.StartsWith(k + "=", StringComparison.Ordinal)).Select(l => l[(k.Length + 1)..].Trim().Trim('"')).FirstOrDefault() ?? ""; return (F("ID"), F("ID_LIKE")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ("", ""); }
    }

    public AppsPage(LinuxAppInstaller installer, bool flatpakAvailable, Func<Window?> owner, CancellationToken lifetime, Func<string, CancellationToken, Task>? openUrl = null)
    {
        this.installer = installer; this.flatpakAvailable = flatpakAvailable; this.owner = owner; this.lifetime = lifetime; Spacing = 4;
        this.openUrl = openUrl ?? ((url, ct) => OperatingSystem.IsLinux() ? DriverTools.OpenUrlAsync(url, ct) : Task.CompletedTask);
        install.HorizontalAlignment = HorizontalAlignment.Right; install.VerticalAlignment = VerticalAlignment.Top; install.Click += async (_, _) => await InstallAsync();
        var header = UiKit.PageHeader("Instalar apps", "Os essenciais depois de instalar o sistema · Flathub (por usuário) e pacotes oficiais da sua distribuição");
        header.RowDefinitions = new("Auto,Auto");
        Grid.SetRow(install, 1); install.Margin = new Thickness(0, 10, 0, 0);
        header.Children.Add(install);
        track.Children.Add(progress); track.SizeChanged += (_, _) => progress.Width = track.Bounds.Width * fraction;
        var all = new Button { Name = "BtnAppsAll", Content = "MARCAR TODOS", Padding = new Thickness(10, 4), FontSize = 11 }; var none = new Button { Name = "BtnAppsNone", Content = "DESMARCAR", Padding = new Thickness(10, 4), FontSize = 11 };
        all.Click += (_, _) => { if (busy) return; foreach (var a in apps) a.Selected = !a.Installed && a.Unavailable is null && !a.LinkOnly; Rebuild(); }; none.Click += (_, _) => { if (busy) return; foreach (var a in apps) a.Selected = false; Rebuild(); };
        flathub.Click += async (_, _) => await AddFlathubAsync();
        var head = new Grid(); head.Children.Add(UiKit.Label("APPS")); head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { all, none } });
        Children.Add(header); Children.Add(track); Children.Add(status);
        Children.Add(UiKit.Card(new StackPanel { Children = { head, new Border { Height = 8 }, list, flathub } }));
        Children.Add(new TextBlock { Text = "Nada é instalado sem clicar em INSTALAR SELECIONADOS e confirmar a lista. Flathub: só para o seu usuário, sem senha. ArtCraft: pacote oficial da sua distribuição, com a senha do sistema.", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void SetProgress(double f) { fraction = Math.Clamp(f, 0, 1); progress.Width = track.Bounds.Width * fraction; }

    public async Task RefreshAsync()
    {
        if (busy) return;
        foreach (var a in apps.Where(a => !a.LinkOnly)) a.Unavailable = null;
        await installer.RefreshInstalledAsync(apps, lifetime);
        bool hasFlathub = flatpakAvailable && await installer.HasFlathubAsync(lifetime);
        // Flathub apps need Flatpak and the Flathub remote; apps from a GitHub release (ArtCraft) use the distribution's own packages.
        string? flatpakMissing = !flatpakAvailable ? "Precisa do Flatpak: " + LinuxAppInstaller.FlatpakInstallHint(Distro().Id, Distro().IdLike) + "."
            : !hasFlathub ? "Precisa do repositório Flathub (botão ADICIONAR FLATHUB abaixo)." : null;
        if (flatpakMissing is not null) foreach (var a in apps.Where(a => a.FlatpakId.Length > 0 && !a.Installed)) a.Unavailable = flatpakMissing;
        foreach (var a in apps) a.Selected = a.PreSelect && !a.Installed && a.Unavailable is null;
        flathub.IsVisible = flatpakAvailable && !hasFlathub; install.IsEnabled = true;
        int total = apps.Count(a => !a.LinkOnly);
        status.Text = $"{apps.Count(a => a.Installed && !a.LinkOnly)} de {total} já instalados." + (flatpakMissing is null ? " Marque o que quer e clique em INSTALAR SELECIONADOS." : " Apps do Flathub indisponíveis por enquanto.");
        ToolTip.SetTip(status, flatpakMissing);
        Rebuild();
    }

    private void Rebuild()
    {
        list.Children.Clear();
        foreach (var group in apps.GroupBy(a => a.Category).OrderBy(g => Array.IndexOf(AppCatalog.Categories, g.Key) is var i and >= 0 ? i : 99))
        {
            list.Children.Add(new TextBlock { Name = "AppsGroup_" + group.Key, Text = group.Key.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Muted, Margin = new Thickness(2, list.Children.Count == 0 ? 0 : 10, 0, 6) });
            foreach (var app in group) list.Children.Add(Row(app));
        }
    }

    private Border Row(InstallableApp app)
    {
        string key = app.Key.Replace('.', '_');
        var check = new CheckBox { Name = "App_" + key, IsChecked = app.Selected, IsEnabled = !app.Installed && !busy && app.Unavailable is null && !app.LinkOnly, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        AutomationProperties.SetName(check, "Instalar " + app.Name); check.IsCheckedChanged += (_, _) => app.Selected = check.IsChecked == true;
        if (app.LinkOnly) check.IsVisible = false;
        string title = app.Name + (app.Installed ? "  ·  JÁ INSTALADO" : app.LinkOnly ? "  ·  SÓ PELO SITE OFICIAL" : "");
        var body = new StackPanel { Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = app.Description, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap } } };
        if (app.Caveat.Length > 0) body.Children.Add(new TextBlock { Name = "AppCaveat_" + key, Text = "Atenção: " + app.Caveat, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        string meta = string.Join(" · ", new[] { app.FlatpakId.Length > 0 ? "Flathub: " + app.Ref : app.FromGithub ? "GitHub: " + app.GithubRepo + ", " + GithubPackages.Label(installer.Github.Format) : "", app.License.Length > 0 ? "Licença: " + app.License : "" }.Where(x => x.Length > 0));
        if (meta.Length > 0) body.Children.Add(new TextBlock { Text = meta, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        if (app.Unavailable is not null && !app.LinkOnly && !app.Installed) body.Children.Add(new TextBlock { Name = "AppUnavailable_" + key, Text = app.Unavailable, Foreground = Palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        if (app.LinkOnly)
        {
            var open = new Button { Name = "AppLink_" + key, Content = "ABRIR PÁGINA OFICIAL", FontSize = 11, Padding = new Thickness(10, 4), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(open, app.LinkUrl);
            open.Click += async (_, _) => { try { await openUrl(app.LinkUrl, lifetime); app.Status = "Página aberta no navegador: " + app.LinkUrl; } catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { app.Status = "Não consegui abrir o navegador. Endereço: " + app.LinkUrl; } Rebuild(); };
            body.Children.Add(open);
        }
        body.Children.Add(new TextBlock { Name = "AppStatus_" + key, Text = app.Status, IsVisible = app.Status.Length > 0, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var row = new Grid { ColumnDefinitions = new("Auto,*") }; Grid.SetColumn(body, 1); row.Children.Add(check); row.Children.Add(body);
        return UiKit.Card(row, new Thickness(0, 0, 0, 8));
    }

    private async Task AddFlathubAsync()
    {
        if (owner() is not { } window || !await ConfirmDialog.AskAsync(window, "Instalar apps", "Adicionar o repositório Flathub (https://flathub.org) à instalação Flatpak do seu usuário? Nada é instalado para outros usuários e nenhum privilégio é usado.", "Adicionar", "Cancelar")) return;
        flathub.IsEnabled = false;
        try { var (ok, message) = await installer.AddFlathubAsync(lifetime); status.Text = message; if (ok) await RefreshAsync(); }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { status.Text = "Não foi possível adicionar o Flathub: " + e.Message; }
        finally { flathub.IsEnabled = true; }
    }

    public async Task InstallAsync()
    {
        if (busy) { stopRequested = true; install.IsEnabled = false; status.Text = "Parando depois do app atual…"; return; }
        var chosen = apps.Where(a => a.Selected && !a.Installed && !a.LinkOnly && a.Unavailable is null).ToList();
        if (chosen.Count == 0) { status.Text = "Nenhum app selecionado."; return; }
        if (owner() is not { } window) return;
        string names = string.Join("\n", chosen.Select(a => $"  • {a.Name} ({(a.FromGithub ? a.GithubRepo + ", " + GithubPackages.Label(installer.Github.Format) : a.Ref)})"));
        var notes = chosen.Where(a => a.Caveat.Length > 0).Select(a => $"  • {a.Name}: {a.Caveat}").ToList();
        string extra = notes.Count > 0 ? "\n\nPrecisam de algo a mais no sistema:\n" + string.Join("\n", notes) : "";
        bool asksPassword = chosen.Any(a => a.FromGithub) && installer.Github.Format != PackageFormat.AppImage;
        string where = chosen.All(a => a.FromGithub) ? "" : chosen.Any(a => a.FromGithub) ? " (Flathub para o seu usuário; ArtCraft pelo pacote da sua distribuição)" : " do Flathub para o seu usuário";
        string password = asksPassword ? "\n\nO sistema vai pedir a sua senha para cada app ArtCraft (instalação para todo o sistema). Cada pacote é conferido pela soma SHA-256 oficial antes." : "";
        if (!await ConfirmDialog.AskAsync(window, "Instalar apps", $"Baixar e instalar {chosen.Count} app(s){where}?\n\n{names}\n\nPode levar vários minutos e baixar centenas de MB.{password}{extra}", "Instalar", "Cancelar")) return;
        busy = true; stopRequested = false; install.Content = "PARAR"; Rebuild();
        foreach (var a in chosen) a.Status = "Na fila.";
        int ok = 0, done = 0; var failed = new List<string>(); SetProgress(0);
        try
        {
            foreach (var app in chosen)
            {
                if (stopRequested) { app.Status = "Não instalado (parado)."; continue; }
                string head = $"Instalando {app.Name} ({done + 1} de {chosen.Count})…";
                status.Text = head; app.Status = "Instalando…"; Rebuild();
                int step = done; bool appDone = false;
                // Progress reports arrive asynchronously: one landing after the app finished must not overwrite the final message.
                var report = new Progress<double>(f => { if (appDone) return; status.Text = $"{head} {f:P0}"; SetProgress((step + f) / chosen.Count); });
                try { var (good, message) = await installer.InstallAsync(app, lifetime, progress: report); appDone = true; app.Status = message; if (good) { ok++; app.Installed = true; app.Selected = false; } else failed.Add(app.Name); }
                catch (OperationCanceledException) { appDone = true; app.Status = "Cancelado."; break; }
                catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or ArgumentException or System.ComponentModel.Win32Exception) { appDone = true; app.Status = "Falhou: " + e.Message; failed.Add(app.Name); }
                done++; SetProgress((double)done / chosen.Count);
            }
            status.Text = $"{ok} de {chosen.Count} instalados." + (failed.Count > 0 ? $" Falharam: {string.Join(", ", failed)}." : "") + (stopRequested ? " Parado pelo usuário." : "");
        }
        finally { busy = false; install.IsEnabled = true; install.Content = "INSTALAR SELECIONADOS"; Rebuild(); }
    }
}
