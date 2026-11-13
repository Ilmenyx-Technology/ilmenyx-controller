// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// "Baixar versão…": pick a GE-Proton or UMU-Proton release from GitHub and download it into Ilmenyx's runners folder, with progress
/// and cancel. Nothing is contacted before the window opens (a click); the archive is checked against the release's SHA-512.
/// </summary>
public sealed class RunnerDownloadDialog : Window
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<RunnerRelease>>> list;
    private readonly Func<RunnerRelease, IProgress<double>, CancellationToken, Task<string>> install;
    private readonly CancellationTokenSource stop = new();
    private readonly ListBox versions = new() { Name = "RunnerVersions", Height = 240 };
    private readonly TextBlock status = new() { Name = "RunnerDownloadStatus", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar bar = new() { Name = "RunnerDownloadProgress", Minimum = 0, Maximum = 1, IsVisible = false, MinHeight = 6 };
    private readonly Button download, close;
    private IReadOnlyList<RunnerRelease> shown = [];
    private CancellationTokenSource? running;

    /// <summary>Folder of the build just installed (or already there); null when the user closed without one.</summary>
    public string? Installed { get; private set; }

    /// <param name="list">Test seam: releases of a source (default: GitHub).</param>
    /// <param name="install">Test seam: installs one release and returns its folder (default: download + SHA-512 + extract).</param>
    public RunnerDownloadDialog(Func<string, CancellationToken, Task<IReadOnlyList<RunnerRelease>>>? list = null, Func<RunnerRelease, IProgress<double>, CancellationToken, Task<string>>? install = null)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        this.list = list ?? ((repo, ct) => RunnerCatalog.ListAsync(http, repo, ct));
        this.install = install ?? ((release, progress, ct) => RunnerCatalog.InstallAsync(http, release, progress, ct));
        Icon = AppIcon.Window; Title = L.T("Baixar uma versão do Proton"); Width = 520; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Panel; Foreground = Palette.Fg;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(versions, L.T("Versões disponíveis"));
        download = UiKit.Primary("BtnRunnerDownloadGo", L.T("Baixar")); download.IsEnabled = false;
        close = new Button { Name = "BtnRunnerDownloadClose", Content = L.T("Fechar"), IsCancel = true, Padding = new Thickness(18, 8) };
        var sources = new UiKit.ChipGroup<string>("RunnerSource", RunnerCatalog.Sources.Select(s => (s.Label, s.Repo)), RunnerCatalog.Sources[0].Repo, repo => _ = LoadAsync(repo));
        versions.SelectionChanged += (_, _) => download.IsEnabled = running is null && versions.SelectedIndex >= 0;
        download.Click += async (_, _) => await DownloadAsync();
        close.Click += (_, _) => { if (running is { } r) r.Cancel(); else Close(); }; // while downloading the button reads "Cancelar"
        Closing += (_, e) => { if (running is not null) { running.Cancel(); e.Cancel = true; } };
        Closed += (_, _) => { stop.Cancel(); stop.Dispose(); http.Dispose(); };
        Content = new StackPanel
        {
            Background = IlmenyxTheme.Ambient(), Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = L.T("Baixar uma versão do Proton"), FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock { Text = L.T("As versões vêm do GitHub de cada projeto e ficam guardadas no Ilmenyx. O arquivo é conferido com a soma SHA-512 da própria versão antes de ser usado."), FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap },
                sources.Panel, versions, bar, status,
                new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 10, LineSpacing = 8, Children = { close, download } },
            },
        };
        Opened += async (_, _) => await LoadAsync(RunnerCatalog.Sources[0].Repo);
    }

    private static string Size(long bytes) => (bytes / (double)(1 << 20)).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " MB";

    private async Task LoadAsync(string repo)
    {
        if (running is not null) return;
        versions.ItemsSource = null; shown = []; download.IsEnabled = false;
        status.Foreground = Palette.Muted; status.Text = L.T("Buscando as versões no GitHub…");
        try
        {
            shown = await list(repo, stop.Token);
            versions.ItemsSource = shown.Select(r => r.Name + " · " + Size(r.Size) + (GameRunners.IsProton(Path.Combine(ClassicProton.Root(), r.Name)) ? L.T(" · já baixado") : "")).ToList();
            status.Text = shown.Count == 0 ? L.T("Nenhuma versão encontrada agora. Tente de novo em alguns minutos.") : L.T("Escolha uma versão. As mais novas vêm primeiro.");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or HttpRequestException or System.Text.Json.JsonException or TimeoutException)
        { status.Foreground = Palette.Warn; status.Text = L.T("Não foi possível buscar as versões: ") + e.Message; }
    }

    private async Task DownloadAsync()
    {
        if (versions.SelectedIndex < 0 || versions.SelectedIndex >= shown.Count || running is not null) return;
        var release = shown[versions.SelectedIndex];
        running = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        download.IsEnabled = false; close.Content = L.T("Cancelar"); bar.IsVisible = true; bar.Value = 0; status.Foreground = Palette.Muted;
        try
        {
            var progress = new Progress<double>(v => { bar.Value = v; status.Text = v < 0.9 ? L.T($"Baixando {release.Name}… {v / 0.9:P0}") : L.T($"Conferindo e extraindo {release.Name}…"); });
            Installed = await install(release, progress, running.Token);
            running = null; Close();
        }
        catch (OperationCanceledException) { status.Text = L.T("Download cancelado. Nada foi instalado."); }
        catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException or TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception)
        { status.Foreground = Palette.Warn; status.Text = e.Message; }
        finally { running?.Dispose(); running = null; bar.IsVisible = false; close.Content = L.T("Fechar"); download.IsEnabled = versions.SelectedIndex >= 0; }
    }
}
