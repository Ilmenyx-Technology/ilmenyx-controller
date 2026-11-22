// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
namespace Ilmenyx.Desktop;

internal static class Program
{
    /// <summary>Games-only window ("--games", or "--play KEY" from a game shortcut), with its own socket so it can run beside the full app.</summary>
    internal static bool GamesMode { get; private set; }
    internal static string? PlayKey { get; private set; }
    internal static string? Instance => Ilmenyx.Platform.Linux.SingleInstance.DefaultPath(DesktopBackend.AppId, GamesMode ? "-jogos" : "");
    internal const string PlayMessage = "play\n";
    [STAThread]
    public static int Main(string[] args)
    {
        Ilmenyx.Core.L.Use(Ilmenyx.Core.L.Resolve(Ilmenyx.Core.IlmenyxSettings.Load(Ilmenyx.Core.IlmenyxSettings.DefaultPath()).Language));
        DisplayBackend backend;
        try { backend = DesktopBackend.Parse(DesktopBackend.ReadSelection()); }
        catch (ArgumentException e) { Console.Error.WriteLine(e.Message); return 2; }
        int play = Array.IndexOf(args, Ilmenyx.Platform.Linux.AppShortcuts.PlayArg);
        PlayKey = play >= 0 && play + 1 < args.Length ? args[play + 1] : null;
        GamesMode = PlayKey is not null || args.Contains(Ilmenyx.Platform.Linux.AppShortcuts.GamesArg);
        // A second launch only brings the running window back (it may be hidden in the notification area); a game shortcut also says which game.
        if (Instance is { } socket && Ilmenyx.Platform.Linux.SingleInstance.TryHandOff(socket, PlayKey is null ? "" : PlayMessage + PlayKey)) return 0;
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
            if (Program.GamesMode)
            {
                var games = new GamesWindow(); desktop.MainWindow = games;
                var closed = new CancellationTokenSource(); games.Closed += (_, _) => closed.Cancel();
                if (Program.PlayKey is { } key) games.Opened += async (_, _) => await games.Page.PlayByKeyAsync(key);
                if (Program.Instance is { } gamesSocket) Ilmenyx.Platform.Linux.SingleInstance.Listen(gamesSocket, (string message) => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    games.Bring();
                    if (message.StartsWith(Program.PlayMessage, StringComparison.Ordinal)) await games.Page.PlayByKeyAsync(message[Program.PlayMessage.Length..]);
                }), closed.Token);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var shell = new ShellWindow(); desktop.MainWindow = shell;
            var gone = new CancellationTokenSource(); shell.Closed += (_, _) => gone.Cancel();
            if (Program.Instance is { } socket) Ilmenyx.Platform.Linux.SingleInstance.Listen(socket, () => Avalonia.Threading.Dispatcher.UIThread.Post(shell.RestoreFromTray), gone.Token);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
