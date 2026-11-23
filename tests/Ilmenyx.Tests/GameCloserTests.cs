// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Fake process table and signal; no real process is touched.
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class GameCloserTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(5);

    [Fact]
    public async Task TermGoesOnlyToTheGamePidsAndNeverToIlmenyx()
    {
        var alive = new List<int> { 4001, 4002, Environment.ProcessId, 1 }; var sent = new List<(int, bool)>();
        bool gone = await GameCloser.CloseAsync(_ => Task.FromResult<IReadOnlyList<int>>(alive.ToList()), (p, f, _) => { sent.Add((p, f)); alive.Remove(p); return Task.CompletedTask; }, false, TimeSpan.FromMilliseconds(50), default, Fast);
        Assert.Equal([(4001, false), (4002, false)], sent);
        Assert.False(gone); // Ilmenyx's own pid and pid 1 were listed by the fake and stay "alive": never signalled, never waited away
    }

    [Fact]
    public async Task ReportsGoneWhenTheyExitAndStillAliveOnTimeout()
    {
        var alive = new List<int> { 10, 11 };
        Assert.True(await GameCloser.CloseAsync(_ => Task.FromResult<IReadOnlyList<int>>(alive.ToList()), (p, _, _) => { alive.Remove(p); return Task.CompletedTask; }, false, TimeSpan.FromSeconds(2), default, Fast));
        var stubborn = new List<int> { 20 }; var sent = new List<(int, bool)>();
        Assert.False(await GameCloser.CloseAsync(_ => Task.FromResult<IReadOnlyList<int>>(stubborn.ToList()), (p, f, _) => { sent.Add((p, f)); return Task.CompletedTask; }, false, TimeSpan.FromMilliseconds(40), default, Fast));
        Assert.True(await GameCloser.CloseAsync(_ => Task.FromResult<IReadOnlyList<int>>(stubborn.ToList()), (p, f, _) => { sent.Add((p, f)); stubborn.Clear(); return Task.CompletedTask; }, true, TimeSpan.FromSeconds(1), default, Fast));
        Assert.Equal([(20, false), (20, true)], sent);
    }

    [Fact]
    public async Task NothingRunningMeansNoSignalAndCancellationStops()
    {
        int calls = 0;
        Assert.True(await GameCloser.CloseAsync(_ => Task.FromResult<IReadOnlyList<int>>([]), (_, _, _) => { calls++; return Task.CompletedTask; }, false, TimeSpan.FromSeconds(1), default, Fast));
        Assert.Equal(0, calls);
        using var cts = new CancellationTokenSource(); cts.CancelAfter(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameCloser.CloseAsync(_ => Task.FromResult<IReadOnlyList<int>>([5]), (_, _, _) => Task.CompletedTask, false, TimeSpan.FromSeconds(5), cts.Token, Fast));
    }

    [Fact]
    public async Task SignalToIlmenyxItselfIsIgnored() => await GameCloser.SignalAsync(Environment.ProcessId, true, default); // would kill the test host otherwise
}
