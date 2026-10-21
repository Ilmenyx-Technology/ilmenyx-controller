using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Ilmenyx.Desktop;
using Ilmenyx.Core;
using Ilmenyx.Contracts;

internal static class Program
{
    internal static string OutputDirectory { get; private set; } = "";
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Supply an absolute directory for this opt-in graphical smoke test.");
        OutputDirectory = Path.Combine(args[0], "ilmenyx-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(OutputDirectory);
        return DesktopBackend.Configure(AppBuilder.Configure<UiSmokeApp>(), DesktopBackend.Parse(DesktopBackend.ReadSelection())).StartWithClassicDesktopLifetime(Array.Empty<string>());
    }
}

public sealed class UiSmokeApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new DashboardWindow();
        desktop.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                await ExerciseAsync(window, desktop);
                File.WriteAllText(Path.Combine(Program.OutputDirectory, "result.txt"), "PASS: application-owned UI controls and real exports; no global keyboard/mouse events; no full benchmark.\n");
                Console.WriteLine("PASS " + Program.OutputDirectory);
                window.Close(); desktop.Shutdown(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Program.OutputDirectory, "failure.txt"), e.ToString());
                Console.Error.WriteLine(e); desktop.Shutdown(1);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Window window, string name)
    {
        var button = Find<Button>(window, name);
        if (!button.IsEffectivelyEnabled) throw new InvalidOperationException("Button disabled: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task Until(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate()) { if (timer.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("UI condition timed out."); await Task.Delay(20); }
    }
    private static async Task<JsonSaveDialog> Dialog(IClassicDesktopStyleApplicationLifetime desktop)
    {
        await Until(() => desktop.Windows.OfType<JsonSaveDialog>().Any(w => w.IsVisible));
        var dialog = desktop.Windows.OfType<JsonSaveDialog>().Single(w => w.IsVisible);
        await Task.Delay(60);
        if (!dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(ProductIdentity.SharingNotice) == true))
            throw new InvalidOperationException("Sharing notice missing from export dialog.");
        return dialog;
    }
    private static void Capture(Window window, string file)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        image.Render(window); image.Save(Path.Combine(Program.OutputDirectory, file), PngBitmapEncoderOptions.Default);
    }
    private static async Task SaveAsync(Window owner, IClassicDesktopStyleApplicationLifetime desktop, string button, string path)
    {
        Click(owner, button);
        var dialog = await Dialog(desktop);
        Find<TextBox>(dialog, "DestinationPath").Text = path;
        Click(dialog, "SaveJson");
        await Until(() => !dialog.IsVisible && File.Exists(path));
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Console.WriteLine("EXPORTED " + Path.GetFileName(path));
    }
    private static async Task ExerciseAsync(DashboardWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var output = Program.OutputDirectory;
        if (window.Title != ProductIdentity.Name || Find<TextBlock>(window, "ProductName").Text != "Ilmenyx" ||
            Find<TextBlock>(window, "MaintainerSignature").Text != "by Bismuth Technology")
            throw new InvalidOperationException("Ilmenyx identity missing from live window.");
        Click(window, "About");
        await Until(() => desktop.Windows.OfType<AboutDialog>().Any(w => w.IsVisible));
        var about = desktop.Windows.OfType<AboutDialog>().Single(w => w.IsVisible);
        await Task.Delay(80);
        var attribution = Find<TextBox>(about, "AttributionText").Text!;
        if (!attribution.Contains("Gustavo (GustavoReset)") || !attribution.Contains("não oficial") || !attribution.Contains("sem afiliação"))
            throw new InvalidOperationException("About attribution incomplete.");
        var tabs = about.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 1; await Task.Delay(60);
        if (!Find<TextBox>(about, "LicenseText").Text!.Contains("Copyright (c) 2026 Gustavo (GustavoReset)"))
            throw new InvalidOperationException("MIT license missing from About.");
        tabs.SelectedIndex = 2; await Task.Delay(60);
        if (!Find<TextBox>(about, "DependenciesText").Text!.Contains("Avalonia.Wayland"))
            throw new InvalidOperationException("Third-party notices missing from About.");
        tabs.SelectedIndex = 0; await Task.Delay(60); Capture(about, "about.png");
        Click(about, "CloseAbout"); await Until(() => !about.IsVisible);
        Console.WriteLine("PASS Ilmenyx identity, by Bismuth Technology, live About/attribution/MIT/dependency tabs and close");
        var implementation = window.PlatformImpl?.GetType().FullName ?? "unavailable";
        var requested = DesktopBackend.Parse(DesktopBackend.ReadSelection());
        if (requested == DisplayBackend.Wayland && !implementation.StartsWith("Avalonia.Wayland.", StringComparison.Ordinal))
            throw new InvalidOperationException("Native Wayland was requested but another backend was selected: " + implementation);
        if (requested == DisplayBackend.X11 && !implementation.StartsWith("Avalonia.X11.", StringComparison.Ordinal))
            throw new InvalidOperationException("X11 was requested but another backend was selected: " + implementation);
        Console.WriteLine("ACTUAL WINDOW IMPLEMENTATION " + implementation);
        var handle = window.TryGetPlatformHandle();
        Console.WriteLine("DISPLAY BACKEND requested=" + (DesktopBackend.ReadSelection() ?? "auto") + "; handle=" + handle?.HandleDescriptor);
        if (DesktopBackend.Parse(DesktopBackend.ReadSelection()) == DisplayBackend.Wayland &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            throw new InvalidOperationException("For a conclusive native smoke test, unset DISPLAY.");
        await Until(() => Find<TextBlock>(window, "HardwareStatus").Text?.StartsWith("Snapshot:") == true);
        var existing = Path.Combine(output, "preserved.json");
        File.WriteAllText(existing, "preserve-this-file");
        Click(window, "ExportHardware");
        var dialog = await Dialog(desktop);
        Find<TextBox>(dialog, "DestinationPath").Text = existing;
        Click(dialog, "SaveJson");
        if (!dialog.IsVisible || !Find<TextBlock>(dialog, "DestinationError").Text!.Contains("já existe")) throw new InvalidOperationException("Existing destination was not rejected in the dialog.");
        if (File.ReadAllText(existing) != "preserve-this-file") throw new InvalidOperationException("Existing file modified.");
        Find<TextBox>(dialog, "DestinationPath").Text = Path.Combine(output, "hardware.json");
        Capture(dialog, "save-dialog.png");
        Click(dialog, "SaveJson");
        await Until(() => File.Exists(Path.Combine(output, "hardware.json")));
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "hardware.json"))))
        {
            if (json.RootElement.GetProperty("schemaVersion").GetString() != "1.2") throw new InvalidOperationException("Hardware schema incorrect.");
            var gpus = json.RootElement.GetProperty("gpus").GetProperty("devices");
            if (gpus.GetArrayLength() > 0 && !Find<TextBlock>(window, "GpuSummary").Text!.Contains(gpus[0].GetProperty("device").GetString()!))
                throw new InvalidOperationException("GPU identification not presented in dashboard.");
        }
        var validationNotice = Find<TextBlock>(window, "GpuValidationNotice");
        if (!validationNotice.IsEffectivelyVisible || validationNotice.Text != "AMD/NVIDIA · Testes não realizados em hardware real")
            throw new InvalidOperationException("AMD/NVIDIA hardware validation notice missing.");
        Console.WriteLine("PASS visible AMD/NVIDIA notice: Testes não realizados em hardware real");
        Console.WriteLine("PASS hardware UI export and overwrite refusal");

        if (Find<Button>(window, "ExportMemory").IsEnabled || !Find<TextBlock>(window, "MemorySummary").Text!.StartsWith("Nenhuma"))
            throw new InvalidOperationException("Memory validation ran automatically.");
        if (Find<Button>(window, "ExportSim").IsEnabled || !Find<TextBlock>(window, "SimSummary").Text!.StartsWith("Nenhuma"))
            throw new InvalidOperationException("GameSim ran automatically.");
        Click(window, "ExportHardwareHtml");
        dialog = await Dialog(desktop);
        var cancelledHtml = Path.Combine(output, "cancelled.html");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelledHtml;
        Click(dialog, "CancelSaveJson"); await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelledHtml)) throw new InvalidOperationException("Cancelled HTML dialog wrote a file.");
        Click(window, "ExportHardwareHtml"); dialog = await Dialog(desktop);
        Find<TextBox>(dialog, "DestinationPath").Text = Path.Combine(output, "wrong-extension.json");
        Click(dialog, "SaveJson");
        if (!dialog.IsVisible || Find<TextBlock>(dialog, "DestinationError").Text != "Use a extensão .html.")
            throw new InvalidOperationException("HTML extension was not validated.");
        var existingHtml = Path.Combine(output, "preserved.html"); File.WriteAllText(existingHtml, "preserve-html");
        Find<TextBox>(dialog, "DestinationPath").Text = existingHtml; Click(dialog, "SaveJson");
        if (!dialog.IsVisible || File.ReadAllText(existingHtml) != "preserve-html") throw new InvalidOperationException("HTML overwrite not refused.");
        var htmlPath = Path.Combine(output, "hardware.html");
        Find<TextBox>(dialog, "DestinationPath").Text = htmlPath; Capture(dialog, "html-dialog.png"); Click(dialog, "SaveJson");
        await Until(() => File.Exists(htmlPath));
        var selected = JsonSerializer.Deserialize<HardwareSnapshot>(File.ReadAllText(Path.Combine(output, "hardware.json")), SnapshotReport.Options)!;
        if (File.ReadAllText(htmlPath) != HardwareHtmlReport.Render(selected)) throw new InvalidOperationException("HTML export did not preserve the selected snapshot.");
        Console.WriteLine("PASS HTML UI export, same snapshot, cancel, extension validation and overwrite refusal");

        Click(window, "ValidateCpu");
        Click(window, "CancelCpu");
        await Until(() => Find<Button>(window, "ValidateCpu").IsEnabled);
        if (!Find<TextBlock>(window, "CpuStatus").Text!.Contains("ancel")) throw new InvalidOperationException("CPU cancellation not shown.");
        Console.WriteLine("PASS CPU cancel control");
        Click(window, "ValidateCpu");
        await Until(() => Find<TextBlock>(window, "CpuSummary").Text?.StartsWith("Concluída") == true);
        if (Find<Image>(window, "CpuPreview").Source is null) throw new InvalidOperationException("CPU preview missing.");
        Click(window, "ExportCpu");
        dialog = await Dialog(desktop);
        var cancelled = Path.Combine(output, "cancelled.json");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelled;
        Click(dialog, "CancelSaveJson");
        await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelled)) throw new InvalidOperationException("Cancelled dialog wrote a file.");
        var cpuPath = Path.Combine(output, "cpu.json");
        await SaveAsync(window, desktop, "ExportCpu", cpuPath);
        using (var json = JsonDocument.Parse(File.ReadAllText(cpuPath)))
        {
            var result = json.RootElement.GetProperty("benchmark");
            if (result.GetProperty("completedSamples").GetInt64() != 4608 || result.GetProperty("experimentalScore").ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("Reduced CPU result invalid.");
        }
        Console.WriteLine("PASS CPU real render, cancelled picker and UI export");

        Click(window, "ValidateMemory"); Click(window, "CancelMemory");
        await Until(() => Find<Button>(window, "ValidateMemory").IsEnabled);
        if (!Find<TextBlock>(window, "MemoryStatus").Text!.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Memory cancellation not shown.");
        Click(window, "ValidateMemory");
        await Until(() => Find<TextBlock>(window, "MemorySummary").Text!.StartsWith("Concluída"));
        Click(window, "ExportMemory"); dialog = await Dialog(desktop);
        var cancelledMemory = Path.Combine(output, "cancelled-memory.json");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelledMemory; Click(dialog, "CancelSaveJson");
        await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelledMemory)) throw new InvalidOperationException("Cancelled memory export wrote a file.");
        var memoryPath = Path.Combine(output, "memory.json");
        await SaveAsync(window, desktop, "ExportMemory", memoryPath);
        using (var json = JsonDocument.Parse(File.ReadAllText(memoryPath)))
        {
            var result = json.RootElement.GetProperty("validation");
            if (result.GetProperty("completedTraversalSteps").GetInt32() != 32768 || result.GetProperty("score").ValueKind != JsonValueKind.Null ||
                result.GetProperty("readSum").GetInt64() != 32768 || result.GetProperty("copySum").GetInt64() != 32768 || result.GetProperty("performanceStatus").GetString() != "not-measured" ||
                result.GetProperty("timeoutSeconds").GetDouble() != 5 || result.GetProperty("readCopyRepetitions").GetInt32() != 1)
                throw new InvalidOperationException("Memory UI report invalid.");
        }
        Console.WriteLine("PASS memory validation: no autostart, real bounded result, cancel control, cancelled dialog and JSON export");

        Click(window, "ValidateSim"); Click(window, "CancelSim");
        await Until(() => Find<Button>(window, "ValidateSim").IsEnabled);
        if (!Find<TextBlock>(window, "SimStatus").Text!.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GameSim cancellation not shown.");
        Click(window, "ValidateSim");
        await Until(() => Find<TextBlock>(window, "SimSummary").Text!.StartsWith("Concluída"));
        var preview = window.GetVisualDescendants().OfType<GameSimPreview>().Single();
        if (preview.State?.Entities.Count != 256 || preview.State.CompletedSteps != 16)
            throw new InvalidOperationException("GameSim final preview state missing.");
        Click(window, "ExportSim"); dialog = await Dialog(desktop);
        var cancelledSim = Path.Combine(output, "cancelled-sim.json");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelledSim; Click(dialog, "CancelSaveJson");
        await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelledSim)) throw new InvalidOperationException("Cancelled GameSim export wrote a file.");
        var simPath = Path.Combine(output, "sim.json");
        await SaveAsync(window, desktop, "ExportSim", simPath);
        using (var json = JsonDocument.Parse(File.ReadAllText(simPath)))
        {
            var result = json.RootElement.GetProperty("validation");
            if (result.GetProperty("completedSteps").GetInt32() != 16 || result.GetProperty("score").ValueKind != JsonValueKind.Null ||
                result.GetProperty("state").GetProperty("entities").GetArrayLength() != 256 ||
                result.GetProperty("stateSha256").GetString() != "6815765e7f314653b328268725f02c597dc31bb5334670769ab3accfc772c880")
                throw new InvalidOperationException("GameSim UI report invalid.");
        }
        Console.WriteLine("PASS GameSim: no autostart; cancel; 256 entities/16 steps; immutable final preview; oracle hash; cancelled dialog; JSON export");

        await Until(() => Find<Button>(window, "RefreshSensors").IsEnabled);
        Click(window, "MonitorSensors");
        await Until(() => Find<Button>(window, "StopSensors").IsEnabled);
        await Task.Delay(2200); // Two real read-only samples, not a load test.
        Click(window, "StopSensors");
        await Until(() => !Find<Button>(window, "StopSensors").IsEnabled && Find<Button>(window, "RefreshSensors").IsEnabled);
        var sensorsPath = Path.Combine(output, "sensors.json");
        await SaveAsync(window, desktop, "ExportSensors", sensorsPath);
        using (var json = JsonDocument.Parse(File.ReadAllText(sensorsPath)))
        {
            if (json.RootElement.GetProperty("schemaVersion").GetString() != "1.1" || json.RootElement.GetProperty("powerSupplies").ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Aggregated sensor schema incorrect.");
            var readings = json.RootElement.GetProperty("readings");
            if (readings.GetArrayLength() > 0 && !Find<TextBlock>(window, "SensorValues").Text!.Contains(readings[0].GetProperty("device").GetString()!))
                throw new InvalidOperationException("Sensor source not shown in UI.");
        }
        Find<ScrollViewer>(window, "DashboardScroll").ScrollToHome();
        await Task.Delay(80); Capture(window, "dashboard.png");
        Find<ScrollViewer>(window, "DashboardScroll").ScrollToEnd();
        await Task.Delay(80); Capture(window, "validation-panels.png");
        Console.WriteLine("PASS sensor monitor start/stop and UI export");
    }
}
