using Ilmenyx.Cli;
using Ilmenyx.Contracts;
using Ilmenyx.Core.Benchmarks;
using Xunit;
namespace Ilmenyx.Tests;

public class GameSimBenchmarkTests
{
    // Only a scripted engine/clock. Never instantiate the 30,000-entity adapter here.
    private sealed class Clock : TimeProvider
    {
        public long Now; private readonly List<Timer> timers=[];
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Now;
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {var timer=new Timer(this,callback,state,Now+dueTime.Ticks);timers.Add(timer);return timer;}
        public void Advance(double ms)
        { Now+=(long)(ms*TimeSpan.TicksPerMillisecond);foreach(var timer in timers.ToArray())timer.Fire(); }
        private sealed class Timer(Clock owner,TimerCallback callback,object? state,long due):ITimer
        {
            private bool fired;
            public void Fire(){if(!fired && owner.Now>=due){fired=true;callback(state);}}
            public bool Change(TimeSpan dueTime,TimeSpan period)=>throw new NotSupportedException();
            public void Dispose()=>owner.timers.Remove(this);
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
    private sealed class Engine(Clock clock,Func<bool,int,double> milliseconds,Action<bool,int>? observe=null):IGameSimTimedEngine
    {
        public int Resets,Warmups,Frames;
        public void Reset()=>Resets++;
        public GameSimStepTiming Step(bool warmup)
        {
            int n=warmup?++Warmups:++Frames;
            clock.Advance(milliseconds(warmup,n));observe?.Invoke(warmup,n);
            return new(10*n,5*n);
        }
    }
    private static GameSimBenchmarkOptions Options()=>new(){AllowCpuLoad=true,Duration=TimeSpan.FromSeconds(1),Timeout=TimeSpan.FromSeconds(5)};
    [Fact]
    public void ResettingWarmupFrameCounterPreservesEvolvedWorld()
    {
        var engine=new GameSimStateEngine();engine.Reset();var initial=engine.Capture();
        engine.Step(1);var evolved=engine.Capture();engine.ResetFrameCounter();var resetCounter=engine.Capture();
        Assert.NotEqual(initial.Entities[0].X,evolved.Entities[0].X);
        Assert.Equal(0,resetCounter.CompletedSteps);Assert.Equal(evolved.Entities,resetCounter.Entities);
        Assert.Equal(evolved.RngState,resetCounter.RngState);Assert.Equal(evolved.DrawOrder,resetCounter.DrawOrder);
    }
    [Fact]
    public async Task OriginalAverageAndReciprocalP99ExcludeWarmup()
    {
        var clock=new Clock();var engine=new Engine(clock,(warm,n)=>warm?1000:n==1?300:700);
        var result=await new GameSimBenchmarkRunner(clock,_=>engine).RunAsync(Options());
        Assert.Equal(GameSimBenchmarkStatus.Completed,result.Status);Assert.Equal(2,result.WarmupSteps);
        Assert.Equal(1,engine.Resets);Assert.Equal(2,result.MeasuredFrames);Assert.Equal(1,result.MeasuredSeconds);
        Assert.Equal(2,result.AverageFps);Assert.Equal(result.AverageFps,result.ExperimentalScore);
        Assert.Equal(1000d/700,result.Low1Fps!.Value,12);Assert.Equal(15,result.MainThreadMs);Assert.Equal(7.5,result.ParallelMs);
        Assert.Equal("not-validated",result.ScoreComparability);
    }
    [Fact]
    public async Task P99UsesOriginalFloorIndexRatherThanWorstOnePercentMean()
    {
        var clock=new Clock();var engine=new Engine(clock,(warm,n)=>warm?1000:n==199?300:n==200?502:1);
        var result=await new GameSimBenchmarkRunner(clock,_=>engine).RunAsync(Options());
        Assert.Equal(200,result.MeasuredFrames);Assert.Equal(1000d/300,result.Low1Fps!.Value,12); // sorted[198]=300ms; worst 1% mean=401ms
    }
    [Fact]
    public async Task PreCancellationDoesNotCreateRealEngine()
    {
        using var stop=new CancellationTokenSource();stop.Cancel();
        var result=await new GameSimBenchmarkRunner(new Clock(),_=>throw new InvalidOperationException("Must not allocate")).RunAsync(Options(),stop.Token);
        Assert.Equal(GameSimBenchmarkStatus.Cancelled,result.Status);Assert.Null(result.ExperimentalScore);Assert.Equal(0,result.MeasuredFrames);
    }
    [Fact]
    public async Task RealPublicEntryCanBeCancelledBeforeAnyFullAllocation()
    {
        using var stop=new CancellationTokenSource();stop.Cancel();
        var result=await new GameSimBenchmarkRunner().RunAsync(Options(),stop.Token);
        Assert.Equal(GameSimBenchmarkStatus.Cancelled,result.Status);NoMetrics(result);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task CancellationDuringWarmupOrMeasuredStepSuppressesEveryMetric(bool inWarmup)
    {
        using var stop=new CancellationTokenSource();var clock=new Clock();
        var engine=new Engine(clock,(warm,n)=>warm?1000:500,(warm,n)=>{if(warm==inWarmup)stop.Cancel();});
        var result=await new GameSimBenchmarkRunner(clock,_=>engine).RunAsync(Options(),stop.Token);
        Assert.Equal(GameSimBenchmarkStatus.Cancelled,result.Status);NoMetrics(result);
        Assert.Equal(inWarmup?0:1,result.MeasuredFrames);
    }
    [Fact]
    public async Task DeadlineSuppressesScoreEvenAfterLastLongStep()
    {
        var clock=new Clock();var engine=new Engine(clock,(warm,n)=>warm?1000:4000);
        var result=await new GameSimBenchmarkRunner(clock,_=>engine).RunAsync(Options());
        Assert.Equal(GameSimBenchmarkStatus.TimedOut,result.Status);Assert.Equal(1,result.MeasuredFrames);NoMetrics(result);
    }
    [Fact]
    public async Task SampleLimitIsBoundedAndNeverTruncatesAReportedScore()
    {
        var clock=new Clock();var engine=new Engine(clock,(warm,n)=>warm?1000:.001);
        var result=await new GameSimBenchmarkRunner(clock,_=>engine).RunAsync(Options());
        Assert.Equal(GameSimBenchmarkStatus.SampleLimitReached,result.Status);
        Assert.Equal(GameSimBenchmarkRunner.MaximumStoredFrameTimes,result.MeasuredFrames);NoMetrics(result);
    }
    [Fact]
    public async Task ZeroFrameDurationNeverProducesInfinityOrScore()
    {
        var clock=new Clock();var engine=new Engine(clock,(warm,n)=>warm?1000:0);
        var result=await new GameSimBenchmarkRunner(clock,_=>engine).RunAsync(Options());
        Assert.Equal(GameSimBenchmarkStatus.InvalidTiming,result.Status);NoMetrics(result);
    }
    private static void NoMetrics(GameSimBenchmarkResult result)
    {Assert.Null(result.AverageFps);Assert.Null(result.Low1Fps);Assert.Null(result.MainThreadMs);Assert.Null(result.ParallelMs);Assert.Null(result.ExperimentalScore);}
    [Fact]
    public void PublicApiAndCliRequireSpecificOptInBeforeAnyWork()
    {
        Assert.Throws<ArgumentException>(()=>{_ = new GameSimBenchmarkRunner().RunAsync(new());});
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench","sim-full"]));
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench","sim-full","--allow-cpu-load","--duration-seconds","11"]));
        var args=CliArguments.Parse(["bench","sim-full","--allow-cpu-load","--duration-seconds","1","--threads","1","--timeout-seconds","4"]);
        Assert.Equal(CliOperation.GameSimBenchmark,args.Operation);Assert.True(args.SimulationBenchmark!.AllowCpuLoad);Assert.Null(args.Simulation);
    }
}
