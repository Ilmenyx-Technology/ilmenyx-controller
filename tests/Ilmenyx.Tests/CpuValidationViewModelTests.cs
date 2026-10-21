using System.Text.Json;
using System.Collections.Concurrent;
using Ilmenyx.Contracts;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Desktop;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class CpuValidationViewModelTests
{
    [Fact]
    public async Task RealReducedRunPublishesFrameContextAndNoScore()
    {
        using var model = new CpuValidationViewModel(new FixtureHardware(), new CpuBenchmarkRunner());
        Assert.False(model.CanExport);
        await model.StartAsync();
        Assert.True(model.CanStart); Assert.False(model.CanCancel); Assert.True(model.CanExport);
        Assert.Equal(CpuRunStatus.Completed, model.Report!.Benchmark.Status);
        Assert.Equal(4608, model.CompletedSamples);
        Assert.Null(model.Report.Benchmark.ExperimentalScore);
        Assert.Equal(576, model.Frame!.CopyPixels().Length);
        Assert.Contains("Concluída", model.Status);
    }

    [Fact]
    public async Task DuplicateStartsAreIgnoredAndCancelReachesRunner()
    {
        var runner = new CancelledRunner();
        using var model = new CpuValidationViewModel(new FixtureHardware(), runner);
        var running = model.StartAsync();
        await runner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsRunning); Assert.True(model.CanCancel); Assert.False(model.CanStart); Assert.False(model.CanExport);
        await model.StartAsync();
        Assert.Equal(1, runner.Calls);
        Assert.Equal(CpuWorkloadProfile.Validation, runner.Options!.Profile);
        Assert.False(runner.Options.AllowCpuLoad);
        model.Cancel();
        Assert.False(model.CanCancel);
        await running;
        Assert.Equal(CpuRunStatus.Cancelled, model.Report!.Benchmark.Status);
        Assert.Null(model.Report.Benchmark.ExperimentalScore);
        Assert.True(model.CanStart); Assert.True(model.CanExport);
    }

    [Fact]
    public async Task CancellingHardwareCollectionDoesNotStartRenderer()
    {
        var provider = new WaitingHardware();
        var runner = new CancelledRunner();
        using var model = new CpuValidationViewModel(provider, runner);
        var running = model.StartAsync();
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        model.Cancel();
        await running;
        Assert.Equal(0, runner.Calls);
        Assert.Null(model.Report); Assert.False(model.CanExport); Assert.True(model.CanStart);
        Assert.Contains("cancelada", model.Status);
    }

    [Fact]
    public async Task WorkerFailureRestoresControlsAndDoesNotInventReport()
    {
        using var model = new CpuValidationViewModel(new FixtureHardware(), new FailingRunner());
        await model.StartAsync();
        Assert.Contains("indisponível", model.Status);
        Assert.True(model.CanStart); Assert.False(model.CanExport); Assert.Null(model.Report); Assert.Null(model.Frame);
    }

    [Fact]
    public async Task ExportSavesSameReportAndRefusesOverwriteWithoutLosingResult()
    {
        using var model = new CpuValidationViewModel(new FixtureHardware(), new CpuBenchmarkRunner());
        await model.StartAsync();
        var directory = Path.Combine(Path.GetTempPath(), "ilmenyx-ui-export-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "report.json");
        try
        {
            await model.ExportAsync(path);
            var original = await File.ReadAllTextAsync(path);
            using var report = JsonDocument.Parse(original);
            Assert.Equal("validation-32x18-spp8", report.RootElement.GetProperty("benchmark").GetProperty("workload").GetProperty("id").GetString());
            Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("benchmark").GetProperty("experimentalScore").ValueKind);
            await model.ExportAsync(path);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
            Assert.Contains("Não foi possível", model.Status);
            Assert.True(model.CanExport); Assert.True(model.CanStart);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DisposeCancelsWorkAndSuppressesFurtherViewNotifications()
    {
        var runner = new CancelledRunner();
        var model = new CpuValidationViewModel(new FixtureHardware(), runner);
        int changes = 0;
        model.PropertyChanged += (_, _) => changes++;
        var running = model.StartAsync();
        await runner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        model.Dispose();
        int before = changes;
        await running;
        Assert.Equal(before, changes); Assert.False(model.CanStart); Assert.Null(model.Report);
    }

    [Fact]
    public async Task QueuedProgressAfterCompletionCannotChangePublishedResult()
    {
        var output = await new CpuBenchmarkRunner().RunAsync(new CpuBenchmarkOptions());
        var hardware = await new FixtureHardware().CollectAsync();
        var context = new QueuedContext();
        using var model = new CpuValidationViewModel(new ReadyHardware(hardware), new ProgressRunner(output));
        Task running;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            running = model.StartAsync();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await context.UntilCompletedAsync(running);
        await running;
        context.Drain();
        Assert.Equal(4608, model.CompletedSamples);
        Assert.Contains("Concluída", model.Status);
    }

    private sealed class FixtureHardware : IHardwareInfoProvider
    {
        public Task<HardwareSnapshot> CollectAsync(CancellationToken ct = default) => new LinuxHardwareInfoProvider(new FixtureFiles()).CollectAsync(ct);
    }
    private sealed class FixtureFiles : ILinuxFiles
    {
        public Task<string> ReadAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(path switch { "/proc/cpuinfo" => "processor: 0\nmodel name: Test fixture CPU", "/proc/meminfo" => "MemTotal: 32768 kB\nMemAvailable: 4096 kB", "/etc/os-release" => "PRETTY_NAME=Test fixture OS", "/proc/sys/kernel/osrelease" => "fixture-kernel", _ => throw new FileNotFoundException() });
        }
        public IReadOnlyList<string> BlockDevices() => Array.Empty<string>();
    }
    private sealed class ReadyHardware(HardwareSnapshot snapshot) : IHardwareInfoProvider
    {
        public Task<HardwareSnapshot> CollectAsync(CancellationToken ct = default) => Task.FromResult(snapshot);
    }
    private sealed class WaitingHardware : IHardwareInfoProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<HardwareSnapshot> CollectAsync(CancellationToken ct = default)
        {
            Entered.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Fixture must be cancelled.");
        }
    }
    private sealed class CancelledRunner : ICpuBenchmarkRunner
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public CpuBenchmarkOptions? Options { get; private set; }
        public async Task<CpuRenderOutput> RunAsync(CpuBenchmarkOptions options, CancellationToken ct = default, IProgress<CpuBenchmarkProgress>? progress = null)
        {
            Calls++; Options = options; Entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
            return await new CpuBenchmarkRunner().RunAsync(options, ct);
        }
    }
    private sealed class FailingRunner : ICpuBenchmarkRunner
    {
        public Task<CpuRenderOutput> RunAsync(CpuBenchmarkOptions options, CancellationToken ct = default, IProgress<CpuBenchmarkProgress>? progress = null) => throw new AggregateException("Fixture worker failure");
    }
    private sealed class ProgressRunner(CpuRenderOutput output) : ICpuBenchmarkRunner
    {
        public Task<CpuRenderOutput> RunAsync(CpuBenchmarkOptions options, CancellationToken ct = default, IProgress<CpuBenchmarkProgress>? progress = null)
        {
            progress?.Report(new CpuBenchmarkProgress(999999, 12, 12, 0));
            return Task.FromResult(output);
        }
    }
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();
        private readonly SemaphoreSlim posted = new(0);
        public override void Post(SendOrPostCallback callback, object? state) { queue.Enqueue((callback, state)); posted.Release(); }
        public void Drain()
        {
            var previous = Current;
            try { SetSynchronizationContext(this); while (queue.TryDequeue(out var item)) item.Callback(item.State); }
            finally { SetSynchronizationContext(previous); }
        }
        public async Task UntilCompletedAsync(Task task)
        {
            while (!task.IsCompleted)
            {
                if (!await posted.WaitAsync(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fixture UI pump stalled.");
                Drain();
            }
        }
    }
}
