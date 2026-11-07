// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public class OverviewSummaryTests
{
    private static BenchRunEntry Run(string kind, string summary, double? score, int minutesAgo = 0) => new(DateTimeOffset.Now.AddMinutes(-minutesAgo), kind, summary, score);

    [Fact]
    public void PerformanceUsesLatestCpuScoreAndDelta()
    {
        var p = PerformanceSummary.From([Run("Memória", "95.5 ns · 40 GB/s", null), Run("CPU", "multi 6,000", 6000), Run("CPU", "multi 5,400", 5400, 30), Run("OC", "estável · cfg", 1.0, 60)]);
        Assert.Equal(6000, p.CpuScore);
        Assert.Equal(600, p.CpuDelta);
        Assert.Contains("render de CPU", p.CpuSubtitle);
        Assert.Equal("95.5 ns", p.Others.Single(o => o.Key == "Memória").Value);
        Assert.Equal("estável", p.Others.Single(o => o.Key == "Verificação OC").Value);
        Assert.Equal("—", p.Others.Single(o => o.Key == "Disco").Value);
    }

    [Fact]
    public void PerformanceWithoutRunsInvitesTheUserToBenchmark()
    {
        var p = PerformanceSummary.From([]);
        Assert.Null(p.CpuScore); Assert.Null(p.CpuDelta);
        Assert.Contains("rode o Benchmark", p.CpuSubtitle);
        Assert.Null(PerformanceSummary.From([Run("CPU", "x", 100)]).CpuDelta);
    }

    private static HealthChecklist.Inputs In(bool? temp = true, KernelHardwareErrors? errors = null, int good = 0, int attention = 0, int unknown = 0, string? plan = null, bool battery = false, bool? gm = null) =>
        new(temp, errors, good, attention, unknown, plan, battery, gm);

    [Fact]
    public void MissingSourcesAreUnknownNeverHealthy()
    {
        var list = HealthChecklist.Build(In(temp: null, errors: KernelHardwareErrors.Unavailable("sem permissão")));
        var errors = list.Single();
        Assert.Null(errors.Ok);
        Assert.Contains("sem permissão", errors.Detail);
        Assert.Null(HealthChecklist.Build(In(errors: null)).Single(c => c.Title == "Erros de hardware").Ok);
        Assert.False(HealthChecklist.Build(In(temp: false)).First().Ok);
    }

    [Fact]
    public void KernelErrorsAndDisksDriveTheVerdicts()
    {
        var clean = new KernelHardwareErrors(0, 0, 0, 0, null, null);
        Assert.True(HealthChecklist.Build(In(errors: clean)).Single(c => c.Title == "Sem erros de hardware").Ok);
        var bad = HealthChecklist.Build(In(errors: new(2, 0, 1, 0, "x", null))).Single(c => c.Title == "Erros de hardware");
        Assert.False(bad.Ok); Assert.Contains("3 ocorrência", bad.Detail);
        Assert.False(HealthChecklist.Build(In(good: 1, attention: 1)).Single(c => c.Title == "Discos").Ok);
        Assert.Null(HealthChecklist.Build(In(good: 1, unknown: 1)).Single(c => c.Title == "Discos").Ok);
        Assert.True(HealthChecklist.Build(In(good: 2)).Single(c => c.Title == "Discos").Ok);
        Assert.DoesNotContain(HealthChecklist.Build(In()), c => c.Title == "Discos");
    }

    [Fact]
    public void PowerAndGameModeAreInformational()
    {
        Assert.True(HealthChecklist.Build(In(plan: "performance")).Single(c => c.Title == "Perfil de energia").Ok);
        Assert.Null(HealthChecklist.Build(In(plan: "balanced", battery: true)).Single(c => c.Title == "Perfil de energia").Ok);
        Assert.Null(HealthChecklist.Build(In(gm: false)).Single(c => c.Title == "GameMode").Ok);
        Assert.True(HealthChecklist.Build(In(gm: true)).Single(c => c.Title == "GameMode").Ok);
    }
}
