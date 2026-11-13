// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
namespace Ilmenyx.Desktop;

internal static class Program
{
    internal static string? Instance => Ilmenyx.Platform.Linux.SingleInstance.DefaultPath(DesktopBackend.AppId);
    [STAThread]
    public static int Main(string[] args)
    {
        Ilmenyx.Core.L.Use(Ilmenyx.Core.L.Resolve(Ilmenyx.Core.IlmenyxSettings.Load(Ilmenyx.Core.IlmenyxSettings.DefaultPath()).Language));
        DisplayBackend backend;
        try { backend = DesktopBackend.Parse(DesktopBackend.ReadSelection()); }
        catch (ArgumentException e) { Console.Error.WriteLine(e.Message); return 2; }
        // A second launch only brings the running window back (it may be hidden in the notification area).
        if (Instance is { } socket && Ilmenyx.Platform.Linux.SingleInstance.TryHandOff(socket)) return 0;
        return DesktopBackend.Configure(AppBuilder.Configure<App>(), backend).StartWithClassicDesktopLifetime(args);
    }
}
public sealed class App : Application
{
    public override void Initialize() { Styles.Add(IlmenyxTheme.Create()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = new ShellWindow(); desktop.MainWindow = shell;
            var gone = new CancellationTokenSource(); shell.Closed += (_, _) => gone.Cancel();
            if (Program.Instance is { } socket) Ilmenyx.Platform.Linux.SingleInstance.Listen(socket, () => Avalonia.Threading.Dispatcher.UIThread.Post(shell.RestoreFromTray), gone.Token);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
