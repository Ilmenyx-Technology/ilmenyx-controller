// New Ilmenyx additions: see LICENSING.md. Nenhum destes testes inicia carga de CPU/RAM: só planejamento, guardas e a lógica de verificação em blocos minúsculos.
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class StressTests
{
    private const long Gi = 1L << 30, Mi = 1L << 20;

    [Theory]
    [InlineData(16 * Gi, 0.6, 9 * Gi + 512 * Mi)]    // 60 % = 9.6 GiB → 76 whole 128 MiB chunks = 9.5 GiB
    [InlineData(16 * Gi, 0.95, 14 * Gi + 384 * Mi)]  // fraction clamped to 0.9 (14.4 GiB) → 115 chunks
    [InlineData(2 * Gi, 0.9, 1 * Gi)]                // 1.8 GiB wanted but 1 GiB must stay free
    [InlineData(1 * Gi, 0.6, 0)]                     // nothing above the reserve
    [InlineData(1 * Gi + 128 * Mi, 0.9, 0)]          // 128 MiB < 256 MiB minimum
    [InlineData(0, 0.6, 0)]
    public void MemoryPlanKeepsAReserveRoundsToChunksAndRefusesTinyBudgets(long available, double fraction, long expected) => Assert.Equal(expected, StressMemory.Plan(available, fraction));

    [Fact]
    public void MemoryPlanIsCappedAtSixtyFourGiB() => Assert.Equal(StressMemory.MaxBytes, StressMemory.Plan(512 * Gi, 0.9));

    [Fact]
    public void FillAndVerifyDetectCorruptionInATinyBlock()
    {
        var chunk = new long[4096];
        StressTester.FillPattern(chunk, 3); Assert.Equal(0, StressTester.CountMismatches(chunk, 3));
        Assert.Equal(chunk.Length, StressTester.CountMismatches(chunk, 4)); // a different pass pattern does not match (no false pass)
        chunk[17] ^= 1; chunk[4000] = 0; Assert.Equal(2, StressTester.CountMismatches(chunk, 3));
        StressTester.FillPattern(chunk, 9); Assert.Equal(0, StressTester.CountMismatches(chunk, 9)); // refilled
    }

    [Fact]
    public void RefusedRamPlanThrowsBeforeAnyThreadStartsAndEmptySelectionHasNoLoad()
    {
        var t = new StressTester();
        Assert.Throws<InvalidOperationException>(() => t.Start(cpu: true, ram: true, 0.6, availableMemoryBytes: 512 * Mi));
        Assert.False(t.Running);
        t.Start(cpu: false, ram: false, 0.6, 0); // lifecycle only: no CPU and no RAM workers
        Assert.True(t.Running); Assert.Equal(0, t.MemoryAllocatedBytes); t.Stop(); Assert.False(t.Running); t.Stop(); // idempotent
    }

    private static readonly StressLimits Limits = new(90, 85, TimeSpan.FromMinutes(10));

    [Fact]
    public void GuardFollowsTheOriginalPriorityOrder()
    {
        var t = TimeSpan.FromMinutes(1);
        Assert.Contains("CPU a 95", StressGuard.Check(Limits, true, 95, true, 99, 5, true, null, TimeSpan.FromHours(1)));             // CPU beats everything
        Assert.Contains("GPU a 88", StressGuard.Check(Limits, true, 60, true, 88, 5, true, null, TimeSpan.FromHours(1)));            // then GPU
        Assert.Contains("5 erros de memória", StressGuard.Check(Limits, true, 60, true, 60, 5, true, null, TimeSpan.FromHours(1)));  // then memory errors
        Assert.Contains("falha na GPU (boom)", StressGuard.Check(Limits, true, 60, true, 60, 0, true, "boom", TimeSpan.FromHours(1)));
        Assert.Contains("Janela da GPU", StressGuard.Check(Limits, true, 60, true, 60, 0, true, null, t));
        Assert.Equal("Concluído (tempo esgotado)", StressGuard.Check(Limits, true, 60, false, null, 0, false, null, TimeSpan.FromMinutes(10)));
        Assert.Null(StressGuard.Check(Limits, true, 60, false, null, 0, false, null, TimeSpan.FromMinutes(9.9)));
    }

    [Fact]
    public void GuardIgnoresUnavailableSensorsAndDisabledLoadsAndNeverStopsAnInfiniteRun()
    {
        Assert.Null(StressGuard.Check(Limits, true, null, false, null, 0, false, null, TimeSpan.FromMinutes(1)));        // no sensor → no temperature abort (the UI warned beforehand)
        Assert.Null(StressGuard.Check(Limits, false, 120, false, 120, 0, false, null, TimeSpan.FromMinutes(1)));         // CPU load not running → CPU temperature irrelevant
        Assert.Null(StressGuard.Check(Limits with { Duration = TimeSpan.Zero }, true, 60, false, null, 0, false, null, TimeSpan.FromDays(3))); // 0 = no time limit
        Assert.Null(StressGuard.Check(Limits with { GpuTempLimit = null }, true, 60, true, 120, 0, false, null, TimeSpan.Zero));
        Assert.Contains("CPU a 90", StressGuard.Check(Limits, true, 90, false, null, 0, false, null, TimeSpan.Zero)); // limit is inclusive
    }

    [Theory]
    [InlineData("90", "85", false, "10", true)]
    [InlineData("39", "85", false, "10", false)]
    [InlineData("111", "85", false, "10", false)]
    [InlineData("abc", "85", false, "10", false)]
    [InlineData("90", "101", true, "10", false)]
    [InlineData("90", "101", false, "10", true)]  // GPU limit ignored when GPU is off
    [InlineData("90", "85", false, "-1", false)]
    [InlineData("90", "85", false, "1441", false)]
    [InlineData("90", "85", false, "0", true)]
    public void ValidationAcceptsOnlySaneLimits(string cpu, string gpu, bool useGpu, string minutes, bool valid)
    {
        var error = StressGuard.Validate(cpu, gpu, useGpu, minutes, out var limits);
        Assert.Equal(valid, error is null); Assert.Equal(valid, limits is not null);
    }
}
