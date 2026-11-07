// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Desktop;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

// Fixtures sintéticos; nenhuma consulta a disco, smartctl ou sysfs.
public sealed class SmartImportUiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ilmenyx-smart-ui-" + Guid.NewGuid().ToString("N"));
    public SmartImportUiTests() => Directory.CreateDirectory(directory);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    private const string Json = "{\"smartctl\":{\"exit_status\":0},\"device\":{\"name\":\"/dev/sda\"},\"smart_status\":{\"passed\":true}}";

    private static SmartImportViewModel Model() =>
        new((device, path, ct) => SmartctlReportImport.ReadFileAsync(device, path, ct));

    [Fact]
    public async Task ReadsRegularFileAndFiltersReport()
    {
        string path = Path.Combine(directory, "ok.json");
        await File.WriteAllTextAsync(path, Json);
        var report = await SmartctlReportImport.ReadFileAsync("sda", path);
        Assert.Equal("smart-import", report.ReportKind);
        Assert.Equal("sda", report.Diagnostic.Device);
    }

    [Theory]
    [InlineData("relativo.json")]
    [InlineData("/dev/null")]
    [InlineData("/proc/self/status")]
    [InlineData("/sys/kernel/version")]
    [InlineData("")]
    public async Task RejectsRelativeAndSystemPaths(string path) =>
        await Assert.ThrowsAsync<ArgumentException>(() => SmartctlReportImport.ReadFileAsync("sda", path));

    [Fact]
    public async Task RejectsSymlinkMissingEmptyAndOversizedFiles()
    {
        string real = Path.Combine(directory, "real.json"), link = Path.Combine(directory, "link.json");
        await File.WriteAllTextAsync(real, Json);
        File.CreateSymbolicLink(link, real);
        await Assert.ThrowsAsync<ArgumentException>(() => SmartctlReportImport.ReadFileAsync("sda", link));
        await Assert.ThrowsAsync<ArgumentException>(() => SmartctlReportImport.ReadFileAsync("sda", Path.Combine(directory, "ausente.json")));
        string empty = Path.Combine(directory, "empty.json"), big = Path.Combine(directory, "big.json");
        await File.WriteAllTextAsync(empty, "");
        await File.WriteAllTextAsync(big, new string(' ', 300000));
        await Assert.ThrowsAsync<IOException>(() => SmartctlReportImport.ReadFileAsync("sda", empty));
        await Assert.ThrowsAsync<IOException>(() => SmartctlReportImport.ReadFileAsync("sda", big));
    }

    [Fact]
    public async Task RejectsDeviceMismatchAndInvalidDeviceName()
    {
        string path = Path.Combine(directory, "ok.json");
        await File.WriteAllTextAsync(path, Json);
        await Assert.ThrowsAsync<ArgumentException>(() => SmartctlReportImport.ReadFileAsync("sdb", path));
        await Assert.ThrowsAsync<ArgumentException>(() => SmartctlReportImport.ReadFileAsync("../sda", path));
    }

    [Fact]
    public async Task ViewModelKeepsPreviousReportOnFailure()
    {
        string path = Path.Combine(directory, "ok.json");
        await File.WriteAllTextAsync(path, Json);
        using var vm = Model();
        Assert.False(vm.CanExport);
        await vm.ImportAsync(" sda ", path);
        var first = vm.Report;
        Assert.NotNull(first);
        Assert.True(vm.CanExport);
        await vm.ImportAsync("sdb", path);
        Assert.Same(first, vm.Report);
        Assert.Contains("recusada", vm.Status);
    }

    [Fact]
    public async Task ExportWritesSelectedReportAndBlocksReimportDuringPicker()
    {
        string path = Path.Combine(directory, "ok.json"), json = Path.Combine(directory, "out.json"), html = Path.Combine(directory, "out.html");
        await File.WriteAllTextAsync(path, Json);
        using var vm = Model();
        await vm.ImportAsync("sda", path);
        string? ext = null;
        await vm.ExportAsync(false, e => { ext = e; return Task.FromResult<string?>(json); });
        Assert.Equal(".json", ext);
        Assert.Contains("smart-import", await File.ReadAllTextAsync(json));
        await vm.ExportAsync(true, e => Task.FromResult<string?>(html));
        Assert.Contains("<html", await File.ReadAllTextAsync(html), StringComparison.OrdinalIgnoreCase);
        // Re-import while the picker is open is blocked, so the selected report is what gets written.
        string guarded = Path.Combine(directory, "guarded.json");
        var selected = vm.Report;
        await vm.ExportAsync(false, async e => { Assert.False(vm.CanImport); await vm.ImportAsync("sda", path); return guarded; });
        Assert.Same(selected, vm.Report);
        Assert.True(File.Exists(guarded));
        // Cancelled picker writes nothing and re-enables actions.
        await vm.ExportAsync(false, e => Task.FromResult<string?>(null));
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task DisposedViewModelIgnoresLateWork()
    {
        string path = Path.Combine(directory, "ok.json");
        await File.WriteAllTextAsync(path, Json);
        var vm = Model();
        await vm.ImportAsync("sda", path);
        string late = Path.Combine(directory, "late.json");
        var picker = new TaskCompletionSource<string?>();
        var export = vm.ExportAsync(false, _ => picker.Task);
        vm.Dispose();
        picker.SetResult(late);
        await export;
        Assert.False(File.Exists(late));
    }
}
