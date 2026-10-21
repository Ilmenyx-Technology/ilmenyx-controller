using System.Text.Json;
using Ilmenyx.Cli;
using Ilmenyx.Contracts;
using Ilmenyx.Core.Benchmarks;
using Xunit;

namespace Ilmenyx.Tests;

public class MemoryValidationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task MatchesBoundedUpstreamOracle(int threads)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "memory-reference.json")));
        var expected = json.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("threads").GetInt32() == threads);
        var runner = new MemoryValidationRunner();
        var a = await runner.RunAsync(new() { Threads = threads }); var b = await runner.RunAsync(new() { Threads = threads });
        Assert.Equal(MemoryValidationStatus.Completed, a.Status); Assert.Equal(5, a.TimeoutSeconds); Assert.Equal(1, a.ReadCopyRepetitions);
        Assert.Equal(32768, a.CompletedTraversalSteps); Assert.Equal(262144, a.WorkingSetBytes); Assert.Equal(802816, a.MainBufferBytes);
        Assert.Equal(expected.GetProperty("finalIndex").GetInt32(), a.FinalIndex);
        Assert.Equal(expected.GetProperty("ringSha256").GetString(), a.RingSha256);
        Assert.Equal(expected.GetProperty("copySha256").GetString(), a.CopySha256);
        Assert.Equal(expected.GetProperty("readSum").GetInt64(), a.ReadSum); Assert.Equal(a.ReadSum, a.CopySum);
        Assert.Equal(a.CopySha256, b.CopySha256); Assert.Null(a.Score); Assert.Equal("not-measured", a.PerformanceStatus);
    }
    [Fact]
    public void RingVisitsEachCacheLineExactlyOnce()
    {
        var ring = MemoryValidationRunner.BuildRing(default); var seen = new HashSet<long>(); long p = 0;
        for (int i = 0; i < 4096; i++) { Assert.True(seen.Add(p)); Assert.Equal(0, p % 8); Assert.InRange(p, 0, 32767); p = ring[p]; }
        Assert.Equal(0, p); Assert.Equal(4096, seen.Count);
        for (int i = 0; i < ring.Length; i++) if (i % 8 != 0) Assert.Equal(0, ring[i]);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(-1)]
    [InlineData(256)]
    public void InvalidWorkerCountsCannotAllocateFullWorkloads(int threads) =>
        Assert.Throws<ArgumentException>(() => MemoryValidationRunner.Validate(new() { Threads = threads }));
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void InvalidTimeoutsRejected(int seconds) => Assert.Throws<ArgumentException>(() => MemoryValidationRunner.Validate(new() { Timeout = TimeSpan.FromSeconds(seconds) }));
    private sealed class Observe(Action<MemoryValidationProgress> report) : IProgress<MemoryValidationProgress>
    { public void Report(MemoryValidationProgress value) => report(value); }
    [Fact]
    public async Task CancellationBeforeAllocationAndDuringTraversalNeverPublishesMeasurements()
    {
        using var before = new CancellationTokenSource(); before.Cancel();
        var a = await new MemoryValidationRunner().RunAsync(new(), before.Token);
        Assert.Equal(MemoryValidationStatus.Cancelled, a.Status); Assert.Equal(0, a.CompletedTraversalSteps); Assert.Null(a.CopySha256);
        using var during = new CancellationTokenSource();
        var b = await new MemoryValidationRunner().RunAsync(new(), during.Token, new Observe(p => { if (p.CompletedTraversalSteps >= 1024) during.Cancel(); }));
        Assert.Equal(MemoryValidationStatus.Cancelled, b.Status); Assert.Equal(1024, b.CompletedTraversalSteps);
        Assert.Null(b.ReadSum); Assert.Null(b.CopySum); Assert.Null(b.FinalIndex); Assert.Null(b.RingSha256); Assert.Null(b.Score);
    }
    private sealed class ManualClock : TimeProvider
    {
        public TimerCallback? Callback; private object? state;
        public override ITimer CreateTimer(TimerCallback callback, object? value, TimeSpan dueTime, TimeSpan period)
        { Callback = callback; state = value; return new Timer(this); }
        public void Fire() => Callback?.Invoke(state);
        private sealed class Timer(ManualClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => owner.Callback = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    [Fact]
    public async Task DeadlineIsDistinguishedWithoutSleepingOrLoad()
    {
        var clock = new ManualClock();
        var result = await new MemoryValidationRunner(clock).RunAsync(new(), default, new Observe(_ => clock.Fire()));
        Assert.Equal(MemoryValidationStatus.TimedOut, result.Status); Assert.Null(result.Score); Assert.Null(result.CopySha256);
    }
    [Fact]
    public void CliSupportsOnlyTheBoundedValidation()
    {
        var args = CliArguments.Parse(["bench", "memory", "--threads", "4", "--json", "--output", "test.json"]);
        Assert.Equal(CliOperation.MemoryValidation, args.Operation); Assert.Equal(4, args.Memory!.Threads); Assert.Null(args.Benchmark);
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["bench", "memory", "--profile", "full"]));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["bench", "memory", "--allow-cpu-load"]));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["bench", "memory", "--threads", "3"]));
    }
}
