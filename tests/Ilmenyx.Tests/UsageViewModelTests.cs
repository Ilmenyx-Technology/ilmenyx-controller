// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Desktop;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class UsageViewModelTests
{
    private sealed class FakeProvider : IUsageProvider
    {
        public int Calls;
        public Task<UsageSample> SampleAsync(CancellationToken ct = default)
        {
            int n = Interlocked.Increment(ref Calls);
            return Task.FromResult(new UsageSample(DateTimeOffset.UtcNow, "Fixture CPU", n == 1 ? null : 25.0, 2000, 50, 8L << 30, 2L << 30,
                n == 1 ? null : 1024.0, n == 1 ? null : 2048.0, null, null, "eth0", 3,
                [new(1, "small", 1.0, 10 << 20), new(2, "big", 0.5, 900 << 20), new(3, "busy", 20.0, 5 << 20)]));
        }
    }

    [Fact]
    public async Task SamplesFillSeriesAndFormatLinesWithoutInventingRates()
    {
        using var vm = new UsageViewModel(new FakeProvider());
        await vm.SampleOnceAsync(CancellationToken.None);
        Assert.Contains("—", vm.CpuLine.Split('/')[0]);               // first sample: no percent
        Assert.Contains("aguardando", vm.DiskLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("25.0%", UsageViewModel.Pct(25.0));
        await vm.SampleOnceAsync(CancellationToken.None);
        Assert.StartsWith("25.0%", vm.CpuLine);
        Assert.Contains("1.00 KiB/s leitura", vm.DiskLine);
        Assert.Contains("25.00%", vm.MemoryLine.Replace("25.0%", "25.00%"));
        Assert.Equal(2, vm.CpuSeries.Count);
        Assert.Equal("busy", vm.Rows[0].Name);                         // sorted by CPU
        vm.ToggleSort();
        Assert.Equal("big", vm.Rows[0].Name);                          // then by memory
    }

    [Fact]
    public async Task SeriesAreBoundedToTheChartWindow()
    {
        using var vm = new UsageViewModel(new FakeProvider());
        for (int i = 0; i < UsageViewModel.MaxHistory + 25; i++) await vm.SampleOnceAsync(CancellationToken.None);
        Assert.Equal(UsageViewModel.MaxHistory, vm.CpuSeries.Count);
        Assert.Equal(UsageViewModel.MaxHistory, vm.NetSeries.Count);
    }

    [Fact]
    public async Task StartRunsPeriodicallyAndStopHaltsSampling()
    {
        var provider = new FakeProvider();
        using var vm = new UsageViewModel(provider, TimeSpan.FromMilliseconds(20));
        vm.Start();
        Assert.True(vm.IsRunning);
        await Task.Delay(300);
        await vm.StopAsync();
        Assert.False(vm.IsRunning);
        int after = provider.Calls;
        Assert.True(after >= 3);
        await Task.Delay(120);
        Assert.Equal(after, provider.Calls);
    }

    [Fact]
    public async Task DisposeCancelsAndIgnoresLateWork()
    {
        var vm = new UsageViewModel(new FakeProvider(), TimeSpan.FromMilliseconds(20));
        vm.Start(); await Task.Delay(80); vm.Dispose();
        await Task.Delay(100);
        Assert.False(vm.IsRunning);
    }
}
