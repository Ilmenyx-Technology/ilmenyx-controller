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

    public AppsPage(LinuxAppInstaller installer, bool flatpakAvailable, Func<Window?> owner, CancellationToken lifetime)
    {
        this.installer = installer; this.flatpakAvailable = flatpakAvailable; this.owner = owner; this.lifetime = lifetime; Spacing = 4;
        install.HorizontalAlignment = HorizontalAlignment.Right; install.VerticalAlignment = VerticalAlignment.Top; install.Click += async (_, _) => await InstallAsync();
        var header = UiKit.PageHeader("Instalar apps", "Os essenciais depois de instalar o sistema · Flatpak do Flathub, instalação por usuário (sem root)");
        header.RowDefinitions = new("Auto,Auto");
        Grid.SetRow(install, 1); install.Margin = new Thickness(0, 10, 0, 0);
        header.Children.Add(install);
        track.Children.Add(progress); track.SizeChanged += (_, _) => progress.Width = track.Bounds.Width * fraction;
        var all = new Button { Name = "BtnAppsAll", Content = "MARCAR TODOS", Padding = new Thickness(10, 4), FontSize = 11 }; var none = new Button { Name = "BtnAppsNone", Content = "DESMARCAR", Padding = new Thickness(10, 4), FontSize = 11 };
        all.Click += (_, _) => { if (busy) return; foreach (var a in apps) a.Selected = !a.Installed; Rebuild(); }; none.Click += (_, _) => { if (busy) return; foreach (var a in apps) a.Selected = false; Rebuild(); };
        flathub.Click += async (_, _) => await AddFlathubAsync();
        var head = new Grid(); head.Children.Add(UiKit.Label("APPS")); head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { all, none } });
        Children.Add(header); Children.Add(track); Children.Add(status);
        Children.Add(UiKit.Card(new StackPanel { Children = { head, new Border { Height = 8 }, list, flathub } }));
        Children.Add(new TextBlock { Text = "Cada instalação baixa do Flathub (repositório de apps Flatpak) e só acontece ao clicar em INSTALAR SELECIONADOS, depois de uma confirmação com a lista. Nada é instalado para todos os usuários e nenhum privilégio é pedido.", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void SetProgress(double f) { fraction = Math.Clamp(f, 0, 1); progress.Width = track.Bounds.Width * fraction; }

    public async Task RefreshAsync()
    {
        if (busy) return;
        if (!flatpakAvailable) { install.IsEnabled = false; status.Text = "O flatpak não foi encontrado. O Flatpak é um componente do sistema (precisa de root e de serviços próprios), por isso não vai dentro do Ilmenyx. Rode no terminal: " + LinuxAppInstaller.FlatpakInstallHint(Distro().Id, Distro().IdLike) + " — depois reabra o aplicativo e volte a esta página."; Rebuild(); return; }
        await installer.RefreshInstalledAsync(apps, lifetime);
        foreach (var a in apps) a.Selected = a.PreSelect && !a.Installed;
        bool hasFlathub = await installer.HasFlathubAsync(lifetime);
        flathub.IsVisible = !hasFlathub; install.IsEnabled = hasFlathub;
        int installed = apps.Count(a => a.Installed);
        status.Text = hasFlathub ? $"{installed} de {apps.Count} já instalados. Marque o que quer e clique em INSTALAR SELECIONADOS."
            : "O repositório Flathub não está configurado para o seu usuário. Clique em ADICIONAR FLATHUB (grava só na sua configuração do flatpak) e depois volte a instalar.";
        Rebuild();
    }

    private void Rebuild()
    {
        list.Children.Clear();
        foreach (var app in apps)
        {
            var check = new CheckBox { Name = "App_" + app.FlatpakId.Replace('.', '_'), IsChecked = app.Selected, IsEnabled = !app.Installed && !busy, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
            AutomationProperties.SetName(check, "Instalar " + app.Name); check.IsCheckedChanged += (_, _) => app.Selected = check.IsChecked == true;
            var body = new StackPanel { Children = { new TextBlock { Text = app.Name + (app.Installed ? "  ·  JÁ INSTALADO" : ""), FontWeight = FontWeight.SemiBold }, new TextBlock { Text = app.Description, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap }, new TextBlock { Name = "AppStatus_" + app.FlatpakId.Replace('.', '_'), Text = app.Status, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) } } };
            var row = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new Thickness(0, 0, 0, 10) }; Grid.SetColumn(body, 1); row.Children.Add(check); row.Children.Add(body);
            list.Children.Add(UiKit.Card(row, new Thickness(0, 0, 0, 8)));
        }
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
        var chosen = apps.Where(a => a.Selected && !a.Installed).ToList();
        if (chosen.Count == 0) { status.Text = "Nenhum app selecionado."; return; }
        if (owner() is not { } window) return;
        string names = string.Join("\n", chosen.Select(a => $"  • {a.Name} ({a.FlatpakId})"));
        if (!await ConfirmDialog.AskAsync(window, "Instalar apps", $"Baixar e instalar {chosen.Count} app(s) do Flathub para o seu usuário?\n\n{names}\n\nPode levar vários minutos e baixar centenas de MB.", "Instalar", "Cancelar")) return;
        busy = true; stopRequested = false; install.Content = "PARAR"; Rebuild();
        foreach (var a in chosen) a.Status = "Na fila.";
        int ok = 0, done = 0; var failed = new List<string>(); SetProgress(0);
        try
        {
            foreach (var app in chosen)
            {
                if (stopRequested) { app.Status = "Não instalado (parado)."; continue; }
                status.Text = $"Instalando {app.Name} ({done + 1} de {chosen.Count})… baixando do Flathub, pode levar alguns minutos."; app.Status = "Instalando…"; Rebuild();
                try { var (good, message) = await installer.InstallAsync(app, lifetime); app.Status = message; if (good) { ok++; app.Installed = true; app.Selected = false; } else failed.Add(app.Name); }
                catch (OperationCanceledException) { app.Status = "Cancelado."; break; }
                catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or ArgumentException or System.ComponentModel.Win32Exception) { app.Status = "Falhou: " + e.Message; failed.Add(app.Name); }
                done++; SetProgress((double)done / chosen.Count);
            }
            status.Text = $"{ok} de {chosen.Count} instalados." + (failed.Count > 0 ? $" Falharam: {string.Join(", ", failed)}." : "") + (stopRequested ? " Parado pelo usuário." : "");
        }
        finally { busy = false; install.IsEnabled = true; install.Content = "INSTALAR SELECIONADOS"; Rebuild(); }
    }
}
