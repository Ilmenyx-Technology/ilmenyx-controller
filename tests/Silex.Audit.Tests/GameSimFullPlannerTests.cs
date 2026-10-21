using System.Text.Json;
using Silex.Audit.Cli;
using Silex.Audit.Contracts;
using Silex.Audit.Core;
using Silex.Audit.Core.Benchmarks;
using Xunit;
namespace Silex.Audit.Tests;

public class GameSimFullPlannerTests
{
    [Fact]
    public void PlanStatesOriginalWorkloadAndDoesNotClaimExecutionOrScore()
    {
        var plan=GameSimFullPlanner.Create(new());
        Assert.Equal(30000,plan.Entities);Assert.Equal(2,plan.WarmupSeconds);Assert.Equal(1/60f,plan.DeltaTime);
        Assert.Equal(12345u,plan.Seed);Assert.Equal(64,plan.GridSize);Assert.Equal(4,plan.Chunks);
        Assert.False(plan.ExecutionAvailable);Assert.Equal("not-executed",plan.ExecutionStatus);Assert.Null(plan.Score);
        Assert.Equal("not-validated",plan.ScoreComparability);Assert.Contains("not the mean",plan.Low1Definition);
    }
    [Theory]
    [InlineData(0,3,10)] [InlineData(65,3,10)] [InlineData(1,0,10)] [InlineData(1,11,15)]
    [InlineData(1,3,5)] [InlineData(1,3,16)]
    public void InvalidBudgetsRejected(int threads,int duration,int timeout)=>Assert.Throws<ArgumentException>(()=>
        GameSimFullPlanner.Create(new(){Threads=threads,Duration=TimeSpan.FromSeconds(duration),Timeout=TimeSpan.FromSeconds(timeout)}));
    [Fact]
    public void MaximumBoundedPlanIsAllowedWithoutExecution()
    {
        var plan=GameSimFullPlanner.Create(new(){Threads=64,Duration=TimeSpan.FromSeconds(10),Timeout=TimeSpan.FromSeconds(15)});
        Assert.Equal(256,plan.Chunks);Assert.False(plan.ExecutionAvailable);
    }
    [Fact]
    public void CliPlanCannotBeMistakenForLoadOptIn()
    {
        var args=CliArguments.Parse(["bench","sim-plan","--threads","2","--duration-seconds","1","--timeout-seconds","4","--json"]);
        Assert.Equal(CliOperation.GameSimPlan,args.Operation);Assert.Null(args.Simulation);Assert.Null(args.Benchmark);
        Assert.Equal(TimeSpan.FromSeconds(1),args.SimulationPlan!.Duration);
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench","sim-plan","--allow-cpu-load"]));
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench","sim","--duration-seconds","1"]));
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench","sim-plan","--threads","1","--threads","2"]));
    }
    [Fact]
    public async Task PlanExportUsesSameAtomicNoOverwriteAndCancelContract()
    {
        var directory=Path.Combine(Path.GetTempPath(),"silex-sim-plan-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"plan.json");var plan=GameSimFullPlanner.Create(new());
            await SnapshotReport.ExportAsync(plan,path);var original=await File.ReadAllTextAsync(path);
            using var json=JsonDocument.Parse(original);
            Assert.False(json.RootElement.GetProperty("executionAvailable").GetBoolean());
            Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("score").ValueKind);
            await Assert.ThrowsAsync<IOException>(()=>SnapshotReport.ExportAsync(plan,path));
            Assert.Equal(original,await File.ReadAllTextAsync(path));
            using var stop=new CancellationTokenSource();stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>SnapshotReport.ExportAsync(plan,Path.Combine(directory,"cancel.json"),stop.Token));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally{Directory.Delete(directory,true);}
    }
}
