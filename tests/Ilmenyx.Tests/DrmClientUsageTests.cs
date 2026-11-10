// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// fdinfo no formato real do i915 (Intel Iris Xe, kernel 7.3, 2026-11-09).
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class DrmClientUsageTests
{
    private static string Fdinfo(string client, ulong render, ulong video) =>
        $"pos:\t0\nflags:\t02100002\ndrm-driver:\ti915\ndrm-client-id:\t{client}\ndrm-pdev:\t0000:00:02.0\ndrm-total-system0:\t0\n" +
        $"drm-engine-render:\t{render} ns\ndrm-engine-copy:\t0 ns\ndrm-engine-video:\t{video} ns\ndrm-engine-capacity-video:\t2\ndrm-engine-video-enhance:\t0 ns\n";

    private static Dictionary<string, IReadOnlyDictionary<string, ulong>> Sample(params DrmClient[] c) => c.ToDictionary(x => x.ClientId, x => x.EngineNs);

    [Fact]
    public void ParsesEnginesAndCapacity()
    {
        var c = DrmClientUsage.Parse(Fdinfo("305", 1500, 20))!;
        Assert.Equal("0000:00:02.0", c.Pdev); Assert.Equal("305", c.ClientId);
        Assert.Equal(1500UL, c.EngineNs["render"]); Assert.Equal(2, c.Capacity["video"]);
        Assert.Null(DrmClientUsage.Parse("pos:\t0\nflags:\t02\n")); // not a DRM client
    }

    [Fact]
    public void LoadIsTheBusiestEngineOverTheInterval()
    {
        var before = Sample(DrmClientUsage.Parse(Fdinfo("1", 0, 0))!, DrmClientUsage.Parse(Fdinfo("2", 100, 0))!);
        var now = Sample(DrmClientUsage.Parse(Fdinfo("1", 300_000_000, 400_000_000))!, DrmClientUsage.Parse(Fdinfo("2", 200_000_100, 0))!, DrmClientUsage.Parse(Fdinfo("3", 999_000_000, 0))!);
        var cap = new Dictionary<string, int> { ["video"] = 2 };
        // 1 s: render 300 ms + 200 ms = 50 %; video 400 ms over 2 engines = 20 %; client 3 is new, so not counted.
        Assert.Equal(50, DrmClientUsage.Busy(before, now, cap, 1e9)!.Value, 3);
        Assert.Equal(100, DrmClientUsage.Busy(before, now, cap, 1e8)); // clamped
        Assert.Equal(0, DrmClientUsage.Busy(before, before, cap, 1e9));
        Assert.Null(DrmClientUsage.Busy(Sample(), Sample(), cap, 1e9));
    }
}
