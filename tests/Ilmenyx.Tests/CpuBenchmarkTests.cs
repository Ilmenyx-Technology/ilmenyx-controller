// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Buffers.Binary;
using System.Text.Json;
using Ilmenyx.Cli;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class CpuBenchmarkTests
{
    private const string ReferenceHash = "4a2ff589647b89851f86e20f15d093b8d61dff21f75c9fd9e5b11b8a02d8e830";
    private static byte[] ReferenceBytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cpu-reference.bgra"));

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task ReducedImageMatchesUnchangedUpstreamRenderer(int threads)
    {
        var render = await new CpuBenchmarkRunner().RunAsync(new CpuBenchmarkOptions { Threads = threads });
        var expected = ReferenceBytes();
        var actual = render.CopyPixels();
        Assert.Equal(32 * 18, actual.Length);
        for (int i = 0; i < actual.Length; i++) Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(expected.AsSpan(i * 4)), actual[i]);
        Assert.Equal(ReferenceHash, render.Result.ImageSha256);
        Assert.Equal(4608, render.Result.CompletedSamples);
        Assert.Equal(1, render.Result.CompletedPasses);
        Assert.Equal(CpuRunStatus.Completed, render.Result.Status);
        Assert.Null(render.Result.ExperimentalScore);
        Assert.Equal("unvalidated", render.Result.ScoreComparability);
        Assert.Equal(0, render.Result.Environment.WarmupPasses);
        Assert.Equal(0, render.Result.RequestedMinimumSeconds);
        Assert.Equal(10, render.Result.TimeoutSeconds);
    }

    [Fact]
    public async Task ConcurrentRunsDoNotSharePixelsOrCounters()
    {
        var runner = new CpuBenchmarkRunner();
        var runs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => runner.RunAsync(new CpuBenchmarkOptions { Threads = 2 })));
        Assert.All(runs, run => { Assert.Equal(ReferenceHash, run.Result.ImageSha256); Assert.Equal(4608, run.Result.CompletedSamples); });
        var pixels = runs[0].CopyPixels();
        pixels[0] = 0;
        Assert.NotEqual(0, runs[0].CopyPixels()[0]);
        Assert.Equal(runs[0].CopyPixels(), runs[1].CopyPixels());
    }

    [Fact]
    public async Task PreCancelledRunHasNoWorkAndNoScore()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var result = (await new CpuBenchmarkRunner().RunAsync(new CpuBenchmarkOptions(), stop.Token)).Result;
        Assert.Equal(CpuRunStatus.Cancelled, result.Status);
        Assert.Equal(0, result.CompletedSamples);
        Assert.Equal(0, result.CompletedPasses);
        Assert.Null(result.ExperimentalScore);
    }

    [Fact]
    public async Task CancellingBetweenTilesReturnsPartialResultWithoutScore()
    {
        using var stop = new CancellationTokenSource();
        var observer = new Observer(_ => stop.Cancel());
        var output = await new CpuBenchmarkRunner().RunAsync(new CpuBenchmarkOptions(), stop.Token, observer);
        Assert.Equal(CpuRunStatus.Cancelled, output.Result.Status);
        Assert.InRange(output.Result.CompletedSamples, 1, 4607);
        Assert.Equal(0, output.Result.CompletedPasses);
        Assert.Null(output.Result.ExperimentalScore);
        Assert.Contains(unchecked((int)0xFF000000), output.CopyPixels());
        // No worker can mutate the returned frame after the await completes.
        var frozen = output.CopyPixels();
        await Task.Delay(20);
        Assert.Equal(frozen, output.CopyPixels());
    }

    [Fact]
    public async Task TimeoutReturnsNoScoreAndWorkersAreJoined()
    {
        var output = await new CpuBenchmarkRunner().RunAsync(new CpuBenchmarkOptions { Timeout = TimeSpan.FromMilliseconds(1) },
            progress: new Observer(_ => Thread.Sleep(20)));
        Assert.Equal(CpuRunStatus.TimedOut, output.Result.Status);
        Assert.Null(output.Result.ExperimentalScore);
        Assert.Equal(0, output.Result.CompletedPasses);
    }

    [Fact]
    public async Task WorkerObserverFailureIsPropagatedInsteadOfCrashingProcess()
    {
        await Assert.ThrowsAsync<AggregateException>(() => new CpuBenchmarkRunner().RunAsync(new CpuBenchmarkOptions(),
            progress: new Observer(_ => throw new InvalidOperationException("fixture observer failure"))));
    }

    [Fact]
    public void FullWorkloadRequiresConsentBeforeSchedulingAnyWork()
    {
        Assert.Throws<ArgumentException>(() => CpuBenchmarkRunner.Validate(new CpuBenchmarkOptions { Profile = CpuWorkloadProfile.Full }));
        var full = CpuBenchmarkRunner.DescribeWorkload(CpuWorkloadProfile.Full);
        Assert.Equal(960, full.Width); Assert.Equal(540, full.Height); Assert.Equal(1024, full.SamplesPerPixel);
        Assert.Equal(32, full.TileSize); Assert.Equal(6, full.MaximumDepth);
        // Validation only: never run this full profile in the test suite.
        CpuBenchmarkRunner.Validate(new CpuBenchmarkOptions { Profile = CpuWorkloadProfile.Full, AllowCpuLoad = true });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(257)]
    public void RejectsInvalidThreadCounts(int threads) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        CpuBenchmarkRunner.Validate(new CpuBenchmarkOptions { Threads = threads }));

    [Fact]
    public void ReducedWorkloadCannotBeTurnedIntoSustainedLoad()
    {
        Assert.Throws<ArgumentException>(() => CpuBenchmarkRunner.Validate(new CpuBenchmarkOptions { MinimumDuration = TimeSpan.FromSeconds(1) }));
        Assert.Throws<ArgumentException>(() => CpuBenchmarkRunner.Validate(new CpuBenchmarkOptions { Threads = 5 }));
        Assert.Throws<ArgumentException>(() => CpuBenchmarkRunner.Validate(new CpuBenchmarkOptions { Timeout = TimeSpan.FromSeconds(11) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuBenchmarkRunner.Validate(new CpuBenchmarkOptions { Timeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuBenchmarkRunner.Validate(new CpuBenchmarkOptions { Profile = (CpuWorkloadProfile)42 }));
    }

    [Fact]
    public void ScorePreservesOriginalSamplesPerSecondDividedBy10000()
    {
        Assert.Equal(21233.664, CpuScore.Points(530841600, 2.5), 6);
        Assert.Equal(0, CpuScore.Points(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuScore.Points(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuScore.Points(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuScore.Points(1, double.NaN));
    }

    [Fact]
    public async Task ResultJsonIdentifiesVersionEnvironmentAndUnavailableScore()
    {
        var result = (await new CpuBenchmarkRunner().RunAsync(new CpuBenchmarkOptions())).Result;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, SnapshotReport.Options));
        Assert.Equal("completed", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("experimentalScore").ValueKind);
        Assert.Equal(CpuBenchmarkRunner.AlgorithmVersion, doc.RootElement.GetProperty("algorithmVersion").GetString());
        Assert.Equal(CpuBenchmarkRunner.ReferenceCommit, doc.RootElement.GetProperty("referenceCommit").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("environment").GetProperty("runtime").GetString()));
    }

    [Theory]
    [InlineData("bench cpu --profile full")]
    [InlineData("bench cpu --profile unknown")]
    [InlineData("bench cpu --threads 0")]
    [InlineData("bench cpu --min-seconds 1")]
    [InlineData("bench cpu --timeout-seconds NaN")]
    [InlineData("bench cpu --threads 1 --threads 2")]
    [InlineData("bench cpu --allow-cpu-load")]
    [InlineData("info --output --json")]
    [InlineData("capabilities --output foo.json")]
    public void CliRejectsUnsafeOrMalformedArgumentsBeforeRunning(string args) => Assert.ThrowsAny<ArgumentException>(() => CliArguments.Parse(args.Split(' ')));

    [Fact]
    public void CliDefaultsToReducedAndParsesExplicitOptInWithoutExecuting()
    {
        var reduced = CliArguments.Parse(new[] { "bench", "cpu", "--json" });
        Assert.Equal(CpuWorkloadProfile.Validation, reduced.Benchmark!.Profile);
        Assert.False(reduced.Benchmark.AllowCpuLoad);
        var full = CliArguments.Parse(new[] { "bench", "cpu", "--profile", "full", "--allow-cpu-load", "--threads", "1", "--min-seconds", "600", "--timeout-seconds", "900" });
        Assert.Equal(CpuWorkloadProfile.Full, full.Benchmark!.Profile);
        Assert.Equal(TimeSpan.FromMinutes(10), full.Benchmark.MinimumDuration);
        Assert.True(full.Benchmark.AllowCpuLoad);
    }

    private sealed class Observer(Action<CpuBenchmarkProgress> callback) : IProgress<CpuBenchmarkProgress>
    {
        public void Report(CpuBenchmarkProgress value) => callback(value);
    }
}
