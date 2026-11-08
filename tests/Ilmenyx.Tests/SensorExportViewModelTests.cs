// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Test proposal from Claude Code, reviewed and corrected locally.
using System.Text.Json;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Desktop;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class SensorExportViewModelTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ilmenyx-export-tests-" + Guid.NewGuid().ToString("N"));

    public SensorExportViewModelTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task HtmlExportWritesSelectedSnapshotWithoutCollecting()
    {
        var provider = new CountingSensorProvider();
        using var vm = await ReadyAsync(provider);
        var selected = vm.Snapshot;
        var path = Path.Combine(directory, "sensores.html");
        string? extension = null;
        bool exportingInPicker = false, canExportInPicker = true;
        SensorSnapshot? snapshotInPicker = null;

        await vm.ExportAsync(true, ext =>
        {
            extension = ext;
            exportingInPicker = vm.IsExporting;
            canExportInPicker = vm.CanExport;
            snapshotInPicker = vm.Snapshot;
            return Task.FromResult<string?>(path);
        });

        Assert.NotNull(extension);
        Assert.Equal(".html", extension);
        Assert.True(exportingInPicker);
        Assert.False(canExportInPicker);
        Assert.Same(selected, snapshotInPicker);
        Assert.Equal(1, provider.Calls);
        var text = await File.ReadAllTextAsync(path);
        Assert.Equal(SensorHtmlReport.Render(selected!), text);
        Assert.Contains("sensor-1", text);
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task JsonExportWritesSelectedSnapshotWithoutCollecting()
    {
        var provider = new CountingSensorProvider();
        using var vm = await ReadyAsync(provider);
        var path = Path.Combine(directory, "sensores.json");
        string? extension = null;

        await vm.ExportAsync(false, ext => { extension = ext; return Task.FromResult<string?>(path); });

        Assert.NotNull(extension);
        Assert.Equal(".json", extension);
        Assert.Equal(1, provider.Calls);
        var text = await File.ReadAllTextAsync(path);
        using (JsonDocument.Parse(text)) { }
        Assert.Equal(SnapshotReport.Json(vm.Snapshot!), text);
        Assert.Contains("sensor-1", text);
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportPreservesExistingFileAndReportsError(bool html)
    {
        using var vm = await ReadyAsync(new CountingSensorProvider());
        var path = Path.Combine(directory, "existente.html");
        await File.WriteAllTextAsync(path, "conteudo-antigo");

        await vm.ExportAsync(html, _ => Task.FromResult<string?>(path));

        var text = await File.ReadAllTextAsync(path);
        Assert.Equal("conteudo-antigo", text);
        Assert.Contains("Não foi possível exportar", vm.Status);
        Assert.Empty(Directory.GetFiles(directory, ".ilmenyx-*.tmp"));
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task WithoutSnapshotPickerIsNotCalled()
    {
        var provider = new CountingSensorProvider();
        using var vm = new SensorMonitorViewModel(provider);
        var calls = 0;

        Assert.False(vm.CanExport);
        await vm.ExportAsync(true, _ => { calls++; return Task.FromResult<string?>(Path.Combine(directory, "x.html")); });

        Assert.Equal(0, calls);
        Assert.Equal(0, provider.Calls);
        Assert.False(vm.IsExporting);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Fact]
    public async Task WhileRefreshingPickerIsNotCalled()
    {
        var provider = new CountingSensorProvider();
        using var vm = await ReadyAsync(provider);
        var gate = provider.HoldNext();
        Task? refresh = null;
        var calls = 0;
        try
        {
            refresh = vm.RefreshAsync();
            Assert.True(vm.IsRefreshing);
            Assert.False(vm.CanExport);

            await vm.ExportAsync(true, _ => { calls++; return Task.FromResult<string?>(Path.Combine(directory, "x.html")); });

            Assert.Equal(0, calls);
            Assert.False(vm.IsExporting);
        }
        finally
        {
            gate.TrySetResult();
            if (refresh is not null) await refresh.WaitAsync(Timeout);
        }
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickerReturningNullWritesNothing(bool html)
    {
        using var vm = await ReadyAsync(new CountingSensorProvider());

        await vm.ExportAsync(html, _ => Task.FromResult<string?>(null));

        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task PickerCanceledBecomesCanceledStatus()
    {
        using var vm = await ReadyAsync(new CountingSensorProvider());

        await vm.ExportAsync(true, _ => Task.FromCanceled<string?>(new CancellationToken(true)));

        Assert.Contains("cancelada", vm.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task CancellationAfterPickerWritesNothing()
    {
        using var vm = await ReadyAsync(new CountingSensorProvider());
        using var cts = new CancellationTokenSource();
        var path = Path.Combine(directory, "cancelado.html");

        await vm.ExportAsync(true, _ => { cts.Cancel(); return Task.FromResult<string?>(path); }, cts.Token);

        Assert.False(File.Exists(path));
        Assert.Contains("cancelada", vm.Status);
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task PreCanceledExportDoesNotCallPicker()
    {
        using var vm = await ReadyAsync(new CountingSensorProvider());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var calls = 0;

        await vm.ExportAsync(true, _ => { calls++; return Task.FromResult<string?>(Path.Combine(directory, "x.html")); }, cts.Token);

        Assert.Equal(0, calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotReplacedDuringPickerWritesNothing(bool html)
    {
        var provider = new CountingSensorProvider();
        using var vm = await ReadyAsync(provider);
        var selected = vm.Snapshot;
        var path = Path.Combine(directory, "mudou.html");

        await vm.ExportAsync(html, async _ =>
        {
            await vm.RefreshAsync();
            return path;
        });

        Assert.Equal(2, provider.Calls);
        Assert.NotSame(selected, vm.Snapshot);
        Assert.False(File.Exists(path));
        Assert.Contains("mudou", vm.Status);
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshInProgressWhenPickerReturnsWritesNothing(bool html)
    {
        var provider = new CountingSensorProvider();
        using var vm = await ReadyAsync(provider);
        var gate = provider.HoldNext();
        var path = Path.Combine(directory, "refresh.html");
        Task? refresh = null;
        string status;
        try
        {
            await vm.ExportAsync(html, _ =>
            {
                refresh = vm.RefreshAsync();
                return Task.FromResult<string?>(path);
            });
            status = vm.Status;
            Assert.False(vm.IsExporting);
        }
        finally
        {
            gate.TrySetResult();
            if (refresh is not null) await refresh.WaitAsync(Timeout);
        }

        Assert.NotNull(refresh);
        Assert.False(File.Exists(path));
        Assert.Contains("mudou", status);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task ConcurrentExportIsBlockedUntilFirstCompletes()
    {
        var provider = new CountingSensorProvider();
        using var vm = await ReadyAsync(provider);
        var picker = new GatedPicker();
        var path = Path.Combine(directory, "unico.html");
        var secondCalls = 0;
        Task? first = null;
        try
        {
            first = vm.ExportAsync(true, picker.Callback);
            await picker.Entered.WaitAsync(Timeout);

            Assert.True(vm.IsExporting);
            Assert.False(vm.CanExport);
            await vm.ExportAsync(false, _ => { secondCalls++; return Task.FromResult<string?>(Path.Combine(directory, "segundo.json")); });
            Assert.Equal(0, secondCalls);

            picker.Release(path);
            await first.WaitAsync(Timeout);
        }
        finally
        {
            picker.Release(null);
            if (first is not null) await first.WaitAsync(Timeout);
        }

        Assert.Equal(1, picker.Calls);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Path.Combine(directory, "segundo.json")));
        Assert.Equal(1, provider.Calls);
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("file-parent")]
    [InlineData("null-char")]
    public async Task WriteErrorsBecomeStatus(string kind)
    {
        using var vm = await ReadyAsync(new CountingSensorProvider());
        string path;
        switch (kind)
        {
            case "directory":
                path = Path.Combine(directory, "alvo.html");
                Directory.CreateDirectory(path);
                break;
            case "file-parent":
                var parent = Path.Combine(directory, "arquivo.txt");
                await File.WriteAllTextAsync(parent, "preservar");
                path = Path.Combine(parent, "saida.html");
                break;
            default:
                path = directory + Path.DirectorySeparatorChar + "ruim\0nome.html";
                break;
        }

        await vm.ExportAsync(true, _ => Task.FromResult<string?>(path));

        Assert.Contains("Não foi possível exportar", vm.Status);
        Assert.False(vm.IsExporting);
        Assert.True(vm.CanExport);
        if (kind == "directory") Assert.True(Directory.Exists(path));
        if (kind == "file-parent") Assert.Equal("preservar", await File.ReadAllTextAsync(Path.Combine(directory, "arquivo.txt")));
    }

    [Fact]
    public async Task ExportAfterDisposeDoesNotCallPicker()
    {
        var vm = await ReadyAsync(new CountingSensorProvider());
        vm.Dispose();
        var calls = 0;

        await vm.ExportAsync(true, _ => { calls++; return Task.FromResult<string?>(Path.Combine(directory, "x.html")); });

        Assert.Equal(0, calls);
        Assert.False(vm.CanExport);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Fact]
    public async Task DisposeWhilePickerOpenWritesNothingAndKeepsStatus()
    {
        var vm = await ReadyAsync(new CountingSensorProvider());
        var picker = new GatedPicker();
        var path = Path.Combine(directory, "descartado.html");
        var notifications = 0;
        Task? export = null;
        string statusAtDispose;
        try
        {
            export = vm.ExportAsync(true, picker.Callback);
            await picker.Entered.WaitAsync(Timeout);

            vm.Dispose();
            statusAtDispose = vm.Status;
            vm.PropertyChanged += (_, _) => Interlocked.Increment(ref notifications);

            picker.Release(path);
            await export.WaitAsync(Timeout);
        }
        finally
        {
            picker.Release(null);
            if (export is not null) await export.WaitAsync(Timeout);
            vm.Dispose();
        }

        Assert.False(File.Exists(path));
        Assert.Equal(statusAtDispose, vm.Status);
        Assert.Equal(0, Volatile.Read(ref notifications));
        Assert.False(vm.CanExport);
    }

    private static async Task<SensorMonitorViewModel> ReadyAsync(CountingSensorProvider provider)
    {
        var vm = new SensorMonitorViewModel(provider);
        await vm.RefreshAsync();
        Assert.NotNull(vm.Snapshot);
        Assert.True(vm.CanExport);
        return vm;
    }

    private sealed class GatedPicker
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string?> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public Task Entered => entered.Task;
        public void Release(string? path) => release.TrySetResult(path);
        public Task<string?> Callback(string extension)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            return release.Task;
        }
    }

    private sealed class CountingSensorProvider : ISensorProvider
    {
        private static readonly DateTimeOffset BaseTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        private int calls;
        private TaskCompletionSource? gate;
        public int Calls => Volatile.Read(ref calls);

        public TaskCompletionSource HoldNext()
        {
            var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref gate, next);
            return next;
        }

        public async Task<SensorSnapshot> CollectAsync(CancellationToken cancellationToken = default)
        {
            var n = Interlocked.Increment(ref calls);
            if (Interlocked.Exchange(ref gate, null) is { } held) await held.Task.WaitAsync(Timeout, cancellationToken);
            var at = BaseTime.AddSeconds(n);
            var reading = new SensorMeasurement("hwmon0", "fixture-chip", "temp1", $"sensor-{n}", at, "raw",
                Reading<double?>.Available(40 + n, "C", "fixture"));
            return new SensorSnapshot("1.0", at, "available", null, [reading], ["fixture limitation"]);
        }
    }
}
