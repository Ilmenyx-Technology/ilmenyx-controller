// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Text.Json;
using Ilmenyx.Contracts;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Desktop;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class MemoryValidationViewModelTests
{
    private sealed class MissingFiles : ILinuxFiles
    {
        public IReadOnlyList<string> BlockDevices() => [];
        public Task<string> ReadAsync(string path, CancellationToken ct) => throw new FileNotFoundException();
    }
    private static IHardwareInfoProvider Hardware() => new LinuxHardwareInfoProvider(new MissingFiles());
    private sealed class HeldRunner : IMemoryValidationRunner
    {
        public int Calls;
        public TaskCompletionSource<MemoryValidationResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MemoryValidationResult> RunAsync(MemoryValidationOptions options, CancellationToken ct = default, IProgress<MemoryValidationProgress>? progress = null)
        { Interlocked.Increment(ref Calls); Started.TrySetResult(); return Completion.Task.WaitAsync(ct); }
    }
    [Fact]
    public async Task RealSmallRunExportsAndRefusesOverwrite()
    {
        using var vm = new MemoryValidationViewModel(Hardware(), new MemoryValidationRunner());
        Assert.False(vm.CanExport); Assert.Null(vm.Report);
        await vm.StartAsync(); Assert.True(vm.CanExport); Assert.True(vm.CanStart); Assert.False(vm.CanCancel);
        Assert.Equal(MemoryValidationStatus.Completed, vm.Report!.Validation.Status); Assert.Null(vm.Report.Validation.Score);
        var directory = Path.Combine(Path.GetTempPath(), "ilmenyx-memory-vm-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "validation.json");
        try
        {
            await vm.ExportAsync(path); var bytes = await File.ReadAllBytesAsync(path);
            using var json = JsonDocument.Parse(bytes);
            Assert.Equal(32768, json.RootElement.GetProperty("validation").GetProperty("completedTraversalSteps").GetInt32());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("validation").GetProperty("score").ValueKind);
            await vm.ExportAsync(path); Assert.Equal(bytes, await File.ReadAllBytesAsync(path)); Assert.Contains("Não foi possível", vm.Status);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task RepeatedStartIsIgnoredAndCancelStopsAwaitedRunner()
    {
        var runner = new HeldRunner(); using var vm = new MemoryValidationViewModel(Hardware(), runner);
        var running = vm.StartAsync(); await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.CanCancel); Assert.False(vm.CanStart); Assert.False(vm.CanExport);
        await vm.StartAsync(); Assert.Equal(1, runner.Calls);
        vm.Cancel(); await running; Assert.Null(vm.Report); Assert.Contains("cancelada", vm.Status); Assert.True(vm.CanStart);
    }
    [Fact]
    public async Task DisposeCancelsWithoutLateNotifications()
    {
        var runner = new HeldRunner(); var vm = new MemoryValidationViewModel(Hardware(), runner);
        var running = vm.StartAsync(); await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int late = 0; vm.PropertyChanged += (_, _) => late++;
        vm.Dispose(); await running; Assert.Equal(0, late); Assert.False(vm.CanStart); Assert.Null(vm.Report);
    }
    private sealed class ThrowingRunner : IMemoryValidationRunner
    {
        public Task<MemoryValidationResult> RunAsync(MemoryValidationOptions options, CancellationToken ct = default, IProgress<MemoryValidationProgress>? progress = null) =>
            throw new AggregateException(new IOException("Fixture error"));
    }
    [Fact]
    public async Task WorkerFailureRestoresControlsWithoutReport()
    {
        using var vm = new MemoryValidationViewModel(Hardware(), new ThrowingRunner());
        await vm.StartAsync(); Assert.True(vm.CanStart); Assert.False(vm.CanExport); Assert.Null(vm.Report); Assert.Contains("indisponível", vm.Status);
    }
}
