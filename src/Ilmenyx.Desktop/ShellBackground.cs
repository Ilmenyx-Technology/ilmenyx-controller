// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// What keeps working with the window out of sight, all opt-in in Requisitos e sobre › Preferências: the tray icon (minimize to the
/// notification area), the "CPU too hot" alert and the once-a-day "new version" check.
/// </summary>
public partial class ShellWindow
{
    private Button? pauseButton;
    private TextBlock? sideVersion;
    private TrayIcon? tray;
    private NativeMenuItem? trayPause;
    private bool? trayAvailable;
    private ToastWindow? alertToast;
    private readonly TemperatureAlert tempAlert = new(TemperatureAlert.DefaultLimit);
    // The update question waits for a later launch: the first one already shows the permissions screen and the tour offer.
    private readonly bool firstLaunch = !IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).TourOffered;

    private async Task ToggleMonitorAsync()
    {
        if (usage.IsRunning) await usage.StopAsync(); else usage.Start(stop.Token);
        string label = usage.IsRunning ? L.T("Pausar monitor") : L.T("Retomar monitor");
        if (pauseButton is not null) UiKit.SetAction(pauseButton, usage.IsRunning ? Icons.Pause : Icons.Play, label);
        if (trayPause is not null) trayPause.Header = label;
    }

    private void StartBackgroundFeatures()
    {
        tempAlert.LimitC = appSettings.TempAlertC;
        usage.Updated += OnUsageInBackground;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && appSettings.MinimizeToTray && tray is { IsVisible: true }) Hide();
        };
        _ = RefreshTrayAsync();
        ShowKnownUpdate();
        _ = UpdateOnStartAsync();
    }

    private void StopBackgroundFeatures()
    {
        usage.Updated -= OnUsageInBackground;
        if (tray is not null) { tray.IsVisible = false; tray.Dispose(); tray = null; }
        alertToast?.Close();
    }

    // ---- Tray icon -------------------------------------------------------------------------------------------------------------

    /// <summary>Shows or removes the tray icon after the preference changes. Returns false when the desktop has no notification area.</summary>
    private async Task<bool> RefreshTrayAsync()
    {
        if (!appSettings.MinimizeToTray) { if (tray is not null) tray.IsVisible = false; return true; }
        trayAvailable ??= await DesktopNotification.TrayAvailableAsync(stop.Token);
        if (trayAvailable != true) return false;
        if (tray is null)
        {
            var open = new NativeMenuItem(L.T("Abrir o Ilmenyx"));
            open.Click += (_, _) => RestoreFromTray();
            trayPause = new NativeMenuItem(usage.IsRunning ? L.T("Pausar monitor") : L.T("Retomar monitor"));
            trayPause.Click += async (_, _) => await ToggleMonitorAsync();
            var quit = new NativeMenuItem(L.T("Sair"));
            quit.Click += (_, _) => Close();
            tray = new TrayIcon { Icon = AppIcon.Window, ToolTipText = ProductIdentity.Name, Menu = new NativeMenu { Items = { open, trayPause, new NativeMenuItemSeparator(), quit } } };
            tray.Clicked += (_, _) => RestoreFromTray();
            if (Application.Current is { } app) TrayIcon.SetIcons(app, [tray]);
        }
        tray.IsVisible = true;
        return true;
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // ---- Temperature alert -----------------------------------------------------------------------------------------------------

    private void OnUsageInBackground()
    {
        double? temp = usage.Last?.CpuTempC;
        if (tray is { IsVisible: true })
            tray.ToolTipText = ProductIdentity.Name + " · CPU " + (temp is { } t ? t.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " °C · " : "") + UsageViewModel.Pct(usage.Last?.CpuPercent);
        if (!appSettings.TempAlerts || !tempAlert.Feed(DateTimeOffset.Now, temp)) return;
        double peak = tempAlert.Peak ?? temp ?? 0;
        _ = NotifyAsync(L.T("CPU muito quente"),
            L.T($"A CPU está acima de {tempAlert.LimitC:0} °C há mais de {TemperatureAlert.Hold.TotalSeconds:0} s (chegou a {peak:0} °C). Veja na página Processos o que está usando a CPU e confira a ventilação."));
    }

    /// <summary>System notification (notify-send); when it is missing, the app's own toast. Also listed in the Overview events.</summary>
    private async Task NotifyAsync(string title, string body)
    {
        sessionEvents.Insert(0, (DateTimeOffset.Now, title, body));
        if (await DesktopNotification.SendAsync(title, body, stop.Token) || stop.IsCancellationRequested) return;
        alertToast ??= new ToastWindow();
        alertToast.Notify(title, body);
    }

    // ---- New version -----------------------------------------------------------------------------------------------------------

    private async Task UpdateOnStartAsync()
    {
        if (Environment.GetEnvironmentVariable("ILMENYX_NO_ONBOARDING") == "1") return;
        if (appSettings.UpdateCheck is null)
        {
            if (firstLaunch) return;
            await Task.Delay(TimeSpan.FromSeconds(3), stop.Token).ContinueWith(_ => { });
            if (stop.IsCancellationRequested || !IsVisible) return;
            bool yes = await ConfirmDialog.AskAsync(this, L.T("Avisar quando houver versão nova?"),
                L.T("O Ilmenyx pode consultar a página de versões do projeto no GitHub uma vez por dia e avisar quando sair uma nova. Só essa consulta vai para a internet; nenhum dado do seu PC é enviado. Dá para mudar depois em Requisitos e sobre."),
                L.T("Sim, avisar"), L.T("Não"));
            SaveSetting(s => s.UpdateCheck = yes); appSettings.UpdateCheck = yes;
        }
        if (appSettings.UpdateCheck == true && (appSettings.UpdateCheckedAt is not { } at || DateTimeOffset.UtcNow - at >= ReleaseCheck.Interval))
            await CheckUpdatesAsync();
    }

    /// <summary>Asks GitHub now; returns the text for the Preferences status line.</summary>
    private async Task<string> CheckUpdatesAsync()
    {
        ReleaseInfo? newer;
        try { newer = await ReleaseCheck.FetchNewerAsync(ReleaseCheck.Current, stop.Token); }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
        { return L.T("Não foi possível consultar o GitHub agora (sem internet ou o GitHub não respondeu). O Ilmenyx tenta de novo amanhã."); }
        var now = DateTimeOffset.UtcNow; string? tag = newer?.Tag;
        SaveSetting(s => { s.UpdateCheckedAt = now; s.UpdateLatestTag = tag; });
        appSettings.UpdateCheckedAt = now; appSettings.UpdateLatestTag = tag;
        ShowKnownUpdate();
        if (newer is null) return L.T($"Você está na versão mais recente (v{ReleaseCheck.Current}).");
        if (appSettings.UpdateNotifiedTag != newer.Tag)
        {
            SaveSetting(s => s.UpdateNotifiedTag = newer.Tag); appSettings.UpdateNotifiedTag = newer.Tag;
            await NotifyAsync(L.T("Nova versão do Ilmenyx"), L.T($"A versão {newer.Tag} está disponível (você usa a v{ReleaseCheck.Current}). Clique na versão, no canto do menu lateral, para abrir a página de download."));
        }
        return L.T($"Versão {newer.Tag} disponível. Clique na versão no menu lateral para baixar.");
    }

    /// <summary>The sidebar version turns into a link while a newer release is known (until the app itself is updated).</summary>
    private void ShowKnownUpdate()
    {
        if (sideVersion is null) return;
        string current = "v" + ReleaseCheck.Current;
        string? tag = appSettings.UpdateLatestTag;
        bool newer = tag is not null && Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var v) && ReleaseCheck.Normalize(v) > ReleaseCheck.Current;
        sideVersion.Text = newer ? current + L.T($" · nova versão {tag}") : current;
        sideVersion.Foreground = newer ? Palette.AccentText : Palette.Faint;
        sideVersion.Cursor = newer ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        ToolTip.SetTip(sideVersion, newer ? L.T("Abrir a página de download no navegador") : null);
        sideVersion.Tapped -= OpenReleasesPage;
        if (newer) sideVersion.Tapped += OpenReleasesPage;
    }

    private async void OpenReleasesPage(object? sender, TappedEventArgs e)
    {
        try { await DriverTools.OpenUrlAsync(ReleaseCheck.ReleasesPage, stop.Token); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
    }

    private void SaveSetting(Action<IlmenyxSettings> change)
    {
        try { IlmenyxSettings.Update(settingsPath, change); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- Preferences card (Requisitos e sobre) ---------------------------------------------------------------------------------

    private Control PreferencesCard(CheckBox deviceToasts)
    {
        var trayToggle = new CheckBox { Name = "TrayToggle", Content = L.T("Ao minimizar, ir para a área de notificação (ícone perto do relógio)"), IsChecked = appSettings.MinimizeToTray };
        var trayStatus = new TextBlock { Name = "TrayStatus", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(28, -6, 0, 0) };
        trayToggle.IsCheckedChanged += async (_, _) =>
        {
            bool on = trayToggle.IsChecked == true;
            appSettings.MinimizeToTray = on; SaveSetting(s => s.MinimizeToTray = on);
            bool ok = await RefreshTrayAsync();
            trayStatus.IsVisible = on && !ok;
            trayStatus.Text = L.T("Este ambiente não mostra a área de notificação, então a janela só vai minimizar normalmente. No GNOME, instale a extensão \"AppIndicator and KStatusNotifierItem Support\".");
        };

        var tempToggle = new CheckBox { Name = "TempAlertToggle", Content = L.T("Avisar quando a CPU ficar quente por mais de 20 segundos"), IsChecked = appSettings.TempAlerts };
        var limit = new ComboBox { Name = "TempAlertLimit", MinWidth = 110, ItemsSource = TemperatureAlert.Limits.Select(c => c + " °C").ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(TemperatureAlert.Limits, appSettings.TempAlertC)) };
        tempToggle.IsCheckedChanged += (_, _) => { bool on = tempToggle.IsChecked == true; appSettings.TempAlerts = on; SaveSetting(s => s.TempAlerts = on); };
        limit.SelectionChanged += (_, _) =>
        {
            if (limit.SelectedIndex < 0) return;
            int c = TemperatureAlert.Limits[limit.SelectedIndex];
            appSettings.TempAlertC = c; tempAlert.LimitC = c; SaveSetting(s => s.TempAlertC = c);
        };
        var tempRow = new WrapPanel { ItemSpacing = 10, LineSpacing = 6, Children = { tempToggle, new TextBlock { Text = L.T("Limite:"), VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Muted }, limit } };
        var tempNote = new TextBlock { FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28, -6, 0, 0),
            Text = L.T("Picos rápidos (um segundo a 70–80 °C quando o processador acelera) são normais e não geram aviso. Funciona com a janela minimizada.") };

        var updateToggle = new CheckBox { Name = "UpdateCheckToggle", Content = L.T("Avisar quando houver versão nova (consulta o GitHub uma vez por dia)"), IsChecked = appSettings.UpdateCheck == true };
        var updateStatus = new TextBlock { Name = "UpdateStatus", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        var checkNow = new Button { Name = "CheckUpdatesNow", Content = L.T("Verificar agora") };
        updateToggle.IsCheckedChanged += (_, _) => { bool on = updateToggle.IsChecked == true; appSettings.UpdateCheck = on; SaveSetting(s => s.UpdateCheck = on); };
        checkNow.Click += async (_, _) =>
        {
            checkNow.IsEnabled = false; updateStatus.Text = L.T("Consultando o GitHub…");
            try { updateStatus.Text = await CheckUpdatesAsync(); } finally { checkNow.IsEnabled = true; }
        };
        var updateRow = new WrapPanel { ItemSpacing = 10, LineSpacing = 6, Children = { updateToggle, checkNow } };

        return UiKit.Card(new StackPanel { Name = "PreferencesCard", Spacing = 12, Children =
        {
            UiKit.Label(L.T("PREFERÊNCIAS")), deviceToasts, trayToggle, trayStatus, tempRow, tempNote, updateRow, updateStatus,
        } });
    }
}
