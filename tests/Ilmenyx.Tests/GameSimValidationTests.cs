// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Reflection;
using System.Text.Json;
using Ilmenyx.Cli;
using Ilmenyx.Contracts;
using Ilmenyx.Core.Benchmarks;
using Xunit;

namespace Ilmenyx.Tests;

public class GameSimValidationTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)]
    public void AllStepsAndEdgeCasesMatchIndependentOriginal(int threads)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gamesim-reference.json")));
        foreach (var expected in fixture.RootElement.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("threads").GetInt32() == threads))
        {
            var engine = new GameSimStateEngine(); engine.Reset();
            if (expected.GetProperty("scenario").GetString() == "edge-cases")
            {
                Set(0,500,500,0,0,20); Set(1,501,500,0,0,.001f); Set(2,499,500,0,0,100);
                Set(3,1001,500,40,0,100); Set(4,999,999,100,100,100); Set(5,200,-1,0,-40,100);
                Set(6,500,501,0,0,100); Set(7,500,500,0,0,100);
            }
            int steps = expected.GetProperty("steps").GetInt32();
            for (int i=0;i<steps;i++) engine.Step(threads);
            var state = engine.Capture(); GameSimValidationRunner.ValidateState(state);
            Assert.Equal(expected.GetProperty("stateSha256").GetString(), GameSimValidationRunner.HashState(state));
            Assert.Equal(expected.GetProperty("rngState").GetUInt32(), state.RngState);
            Assert.Equal(expected.GetProperty("visibleCount").GetInt32(), state.DrawOrder.Count);
            Assert.Equal(steps, state.CompletedSteps);
            Assert.Equal(state.DrawDepthKeys.Order(), state.DrawDepthKeys);
            if (expected.GetProperty("scenario").GetString() == "edge-cases")
            {
                Assert.Equal(100, state.Entities[1].Health); // Dead entity respawned.
                Assert.Equal(100, state.Entities[4].Health); // Scoring entity respawned.
                Assert.True(state.Entities[0].Damage > 0); // Contact was actually exercised.
            }
            void Set(int id,float x,float y,float vx,float vy,float hp)
            {
                foreach (var item in new[] { ("_x",x),("_y",y),("_vx",vx),("_vy",vy),("_hp",hp) })
                    ((float[])typeof(GameSimStateEngine).GetField(item.Item1,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(engine)!)[id]=item.Item2;
            }
        }
    }
    [Fact]
    public void SnapshotOwnsCopiesAndCannotBeMutatedByConsumers()
    {
        var engine = new GameSimStateEngine(); engine.Reset(); engine.Step(1);
        var snapshot = engine.Capture(); var hash = GameSimValidationRunner.HashState(snapshot);
        engine.Step(4);
        Assert.Equal(hash,GameSimValidationRunner.HashState(snapshot));
        Assert.Throws<NotSupportedException>(() => ((IList<GameSimEntity>)snapshot.Entities)[0] = snapshot.Entities[1]);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)snapshot.DrawOrder)[0] = 0);
        engine.Reset(); engine.Step(2);
        Assert.Equal(hash,GameSimValidationRunner.HashState(engine.Capture()));
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)]
    public async Task RunnerCompletesOnlyFixedWorkWithoutPerformanceMetrics(int threads)
    {
        var result = await new GameSimValidationRunner().RunAsync(new() {Threads=threads});
        Assert.Equal(GameSimValidationStatus.Completed,result.Status); Assert.Equal(16,result.CompletedSteps);
        Assert.Equal(256,result.State!.Entities.Count); Assert.Equal(threads*4,result.Chunks);
        Assert.Equal(0,result.Workload.WarmupSteps); Assert.Null(result.Score); Assert.Equal("not-measured",result.PerformanceStatus);
        Assert.Equal(GameSimValidationRunner.HashState(result.State),result.StateSha256);
    }
    private sealed class Observe(Action<GameSimValidationProgress> action) : IProgress<GameSimValidationProgress>
    { public void Report(GameSimValidationProgress value) => action(value); }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(16)]
    public async Task CancellationNeverPublishesPartialOrLateState(int cancelAt)
    {
        using var stop = new CancellationTokenSource();
        if (cancelAt==0) stop.Cancel();
        var result=await new GameSimValidationRunner().RunAsync(new(),stop.Token,new Observe(p=>{if(p.CompletedSteps==cancelAt)stop.Cancel();}));
        Assert.Equal(GameSimValidationStatus.Cancelled,result.Status); Assert.Equal(cancelAt,result.CompletedSteps);
        Assert.Null(result.State); Assert.Null(result.StateSha256); Assert.Null(result.Score);
    }
    private sealed class Clock : TimeProvider
    {
        public TimerCallback? Callback; private object? state;
        public override ITimer CreateTimer(TimerCallback callback,object? value,TimeSpan dueTime,TimeSpan period)
        { Callback=callback; state=value; return new Timer(this); }
        public void Fire()=>Callback?.Invoke(state);
        private sealed class Timer(Clock clock):ITimer
        {
            public bool Change(TimeSpan dueTime,TimeSpan period)=>true;
            public void Dispose()=>clock.Callback=null;
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
    [Fact]
    public async Task DeadlineReturnsExplicitUnavailableState()
    {
        var clock=new Clock();
        var result=await new GameSimValidationRunner(clock).RunAsync(new(),default,new Observe(_=>clock.Fire()));
        Assert.Equal(GameSimValidationStatus.TimedOut,result.Status); Assert.Null(result.State); Assert.Null(result.StateSha256);
    }
    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(256)]
    public void InvalidWorkersRejected(int threads)=>Assert.Throws<ArgumentException>(()=>GameSimValidationRunner.Validate(new(){Threads=threads}));
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(11)]
    public void InvalidDeadlineRejected(int seconds)=>Assert.Throws<ArgumentException>(()=>GameSimValidationRunner.Validate(new(){Timeout=TimeSpan.FromSeconds(seconds)}));
    [Fact]
    public void CliCannotSelectFullOrDuration()
    {
        var args=CliArguments.Parse(["bench","sim","--threads","4","--json"]);
        Assert.Equal(CliOperation.GameSimValidation,args.Operation); Assert.Equal(4,args.Simulation!.Threads);
        foreach(var flags in new[]{new[]{"--profile","full"},new[]{"--min-seconds","60"},new[]{"--allow-cpu-load"},new[]{"--threads","3"}})
            Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench","sim",..flags]));
    }
    [Fact]
    public void CorruptSnapshotRejected()
    {
        var engine=new GameSimStateEngine();engine.Reset();engine.Step(1);var state=engine.Capture();
        var entities=state.Entities.ToArray();entities[0]=entities[0] with {X=float.NaN};
        Assert.Throws<InvalidDataException>(()=>GameSimValidationRunner.ValidateState(state with {Entities=entities}));
        Assert.Throws<InvalidDataException>(()=>GameSimValidationRunner.ValidateState(state with {DrawOrder=new[]{0,0},DrawDepthKeys=new[]{1f,1f}}));
    }
}
