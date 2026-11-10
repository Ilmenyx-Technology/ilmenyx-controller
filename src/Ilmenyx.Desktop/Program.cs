// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
namespace Ilmenyx.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        DisplayBackend backend;
        try { backend = DesktopBackend.Parse(DesktopBackend.ReadSelection()); }
        catch (ArgumentException e) { Console.Error.WriteLine(e.Message); return 2; }
        return DesktopBackend.Configure(AppBuilder.Configure<App>(), backend).StartWithClassicDesktopLifetime(args);
    }
}
public sealed class App : Application
{
    public override void Initialize() { Styles.Add(IlmenyxTheme.Create()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new ShellWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
