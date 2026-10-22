// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

/// <summary>Opt-in (ILMENYX_RUN_COMPONENT_TESTS=1 and ILMENYX_COMPONENTS_DIR=…/out): starts the bundled OpenRGB server on loopback and talks to it. A pass without the variable proves nothing.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public class BundledComponentsRealTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("ILMENYX_RUN_COMPONENT_TESTS") == "1" && BundledComponents.Root() is not null;

    [Fact]
    public async Task BundledOpenRgbServerStartsOnLoopbackAnswersAndStops()
    {
        if (!Enabled) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var server = await OpenRgbServer.StartAsync(6799, TimeSpan.FromSeconds(60), cts.Token);
        Assert.True(server.IsRunning);
        await using var client = await OpenRgbClient.ConnectAsync("127.0.0.1", 6799, cts.Token);
        var devices = await client.ListAsync(cts.Token);
        Assert.NotNull(devices);
    }
}
