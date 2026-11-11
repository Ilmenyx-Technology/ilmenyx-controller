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
/// "Nova versão": what is new (link to the notes) and Atualizar agora, which downloads, checks (SHA-256 + GPG signature) and installs the
/// release for this kind of install, with progress; then Reiniciar agora. The folder package only gets the download page.
/// </summary>
public sealed class UpdateDialog : Window
{
    private readonly string tag;
    private readonly CancellationTokenSource stop = new();
    private readonly TextBlock step = new() { Name = "UpdateStep", FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar bar = new() { Name = "UpdateProgress", Minimum = 0, Maximum = 1, IsVisible = false, MinHeight = 6 };
    private readonly Button update, restart, later, page;
    private bool busy;

    /// <summary>True after the new version was installed and the user chose to restart: the caller closes the app.</summary>
    public bool RestartRequested { get; private set; }

    public UpdateDialog(string tag)
    {
        this.tag = tag;
        Icon = AppIcon.Window; Title = L.T("Nova versão"); Width = 520; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Panel; Foreground = Palette.Fg;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var kind = SelfUpdate.Kind;
        string how = kind switch
        {
            InstallKind.AppImage => L.T("O Ilmenyx baixa a versão nova, confere a assinatura e troca o arquivo AppImage. Seus ajustes ficam como estão."),
            InstallKind.Flatpak => L.T("O Ilmenyx baixa a versão nova, confere a assinatura e reinstala pelo Flatpak, só para o seu usuário. Seus ajustes ficam como estão."),
            _ => L.T("Esta cópia é o pacote em pasta, que não se atualiza sozinho: baixe a versão nova pela página de downloads."),
        };
        update = UiKit.Primary("BtnUpdateNow", L.T("Atualizar agora")); update.IsVisible = SelfUpdate.CanInstall;
        restart = UiKit.Primary("BtnUpdateRestart", L.T("Reiniciar agora")); restart.IsVisible = false;
        later = new Button { Name = "BtnUpdateLater", Content = L.T("Depois"), IsCancel = true, Padding = new Thickness(18, 8) };
        page = new Button { Name = "BtnUpdatePage", Content = L.T("Ver o que mudou"), Padding = new Thickness(18, 8) };
        update.Click += async (_, _) => await RunAsync();
        restart.Click += (_, _) => { RestartRequested = true; Close(true); };
        later.Click += (_, _) => Close(false);
        page.Click += async (_, _) =>
        {
            try { await DriverTools.OpenUrlAsync(ReleaseCheck.ReleasesPage + "/tag/" + tag, stop.Token); }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
        };
        Closing += (_, e) => { if (busy) e.Cancel = true; }; // never close in the middle of the install
        Closed += (_, _) => { stop.Cancel(); stop.Dispose(); };
        var title = new TextBlock { Text = L.T("Ilmenyx ") + tag + L.T(" disponível"), FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
        var info = new TextBlock { Text = L.T($"Você usa a v{ReleaseCheck.Current}. ") + how, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(info, info.Text);
        Content = new StackPanel
        {
            Background = IlmenyxTheme.Ambient(), Margin = new Thickness(24), Spacing = 14,
            Children = { title, info, bar, step, new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 10, LineSpacing = 8, Children = { page, later, update, restart } } },
        };
        Opened += (_, _) => (SelfUpdate.CanInstall ? update : page).Focus();
    }

    private async Task RunAsync()
    {
        busy = true; update.IsEnabled = false; later.IsEnabled = false; bar.IsVisible = true; bar.IsIndeterminate = false; step.Foreground = Palette.Muted;
        try
        {
            var progress = new Progress<(string Step, double Fraction)>(p => { step.Text = p.Step; bar.Value = p.Fraction; });
            var (file, note) = await SelfUpdate.FetchAsync(tag, progress, stop.Token);
            step.Text = L.T("Instalando…"); bar.IsIndeterminate = true;
            await SelfUpdate.InstallAsync(file, stop.Token);
            bar.IsIndeterminate = false; bar.Value = 1;
            step.Text = L.T("Pronto: a ") + tag + L.T(" está instalada. Reinicie o Ilmenyx para usar a versão nova.") + (note.Length > 0 ? " " + note : "");
            step.Foreground = Palette.Success;
            update.IsVisible = false; restart.IsVisible = true; restart.Focus();
        }
        catch (OperationCanceledException) { step.Text = L.T("Atualização cancelada. Nada foi instalado."); }
        catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            step.Text = e.Message + " " + L.T("Você pode tentar de novo ou baixar pela página de downloads.");
            step.Foreground = Palette.Warn; update.IsEnabled = true; bar.IsVisible = false;
        }
        finally { busy = false; later.IsEnabled = true; }
    }
}
