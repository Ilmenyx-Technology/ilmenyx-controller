// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class OverviewInsightTests
{
    private const long GiB = 1L << 30;

    [Fact]
    public void MemoryIsTightWhenALotWentToSwapEvenAtModerateUse()
    {
        Assert.Equal(InsightLevel.Watch, OverviewInsight.Memory(16 * GiB, 10 * GiB, 9 * GiB)); // HP 250 G8, 2026-11-12: 65.8% used, 9.7 GiB in zram
        Assert.Equal(InsightLevel.Fine, OverviewInsight.Memory(16 * GiB, 10 * GiB, 1 * GiB));
        Assert.Equal(InsightLevel.Watch, OverviewInsight.Memory(16 * GiB, 14 * GiB, 0));
        Assert.Equal(InsightLevel.Critical, OverviewInsight.Memory(16 * GiB, 15 * GiB + GiB / 2, 0));
        Assert.Equal(InsightLevel.Fine, OverviewInsight.Memory(0, 0, 0));
    }

    [Fact]
    public void IdleHogIsTheBiggestAppDoingNothing()
    {
        static AppGroup App(string id, double cpu, long bytes) => new(id, [new ProcessUsage(1, id, cpu, bytes)]);
        var hog = OverviewInsight.IdleHog([App("busy", 30, 4 * GiB), App("chat", 0.2, GiB), App("notes", 0, 2 * GiB), App("tiny", 0, 100 << 20)]);
        Assert.Equal("notes", hog?.AppId);
        Assert.Null(OverviewInsight.IdleHog([App("busy", 30, 4 * GiB), App("tiny", 0, 100 << 20)]));
    }

    [Fact]
    public void FreeMemoryIsReadAndNeverAboveAvailable()
    {
        Assert.Equal((16000L << 10, 10000L << 10, 3000L << 10), LinuxUsageProvider.Memory("MemTotal: 16000 kB\nMemFree: 3000 kB\nMemAvailable: 6000 kB\n"));
        Assert.Null(LinuxUsageProvider.Memory("MemTotal: 16000 kB\nMemFree: 7000 kB\nMemAvailable: 6000 kB\n").free);
    }
}
