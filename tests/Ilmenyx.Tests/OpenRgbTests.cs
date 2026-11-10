// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ilmenyx.Core;
using Ilmenyx.TestSupport;
using Xunit;

namespace Ilmenyx.Tests;

public class OpenRgbTests
{
    private static byte[] DevicePayload(string name, string vendor, int leds, int active = 1, bool truncate = false) => FakeOpenRgbServer.DevicePayload(name, vendor, leds, active, truncate);

    [Fact]
    public async Task ListsOnlyDevicesWithLedsAfterNegotiating()
    {
        await using var server = new FakeOpenRgbServer(); server.Devices.AddRange([DevicePayload("Teclado", "Corsair", 4), DevicePayload("Sem LED", "X", 0), DevicePayload("Fita", "Gigabyte", 2)]);
        await using var client = await OpenRgbClient.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        var list = await client.ListAsync(CancellationToken.None);
        Assert.Equal(["0:Teclado", "2:Fita"], list.Select(d => d.Id));
        Assert.Equal(4, list[0].Leds);
        Assert.Contains("Corsair", list[0].Detail);
        Assert.Equal(1, list[0].ActiveMode);
        Assert.NotNull(list[0].ActiveModeBytes);
        Assert.Equal(new[] { 0.0, 1 / 3.0, 2 / 3.0, 1.0 }, list[0].LedX());
        var received = server.Snapshot();
        Assert.Equal(50u, received[0].Type);
        Assert.Equal("Ilmenyx\0", Encoding.ASCII.GetString(received[0].Payload));
    }

    [Fact]
    public async Task OldServerProtocolIsRejectedWithAClearMessage()
    {
        await using var server = new FakeOpenRgbServer { ServerProtocol = 2 };
        await using var client = await OpenRgbClient.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        var e = await Assert.ThrowsAsync<OpenRgbException>(() => client.ListAsync(CancellationToken.None));
        Assert.Contains("0.9", e.Message);
    }

    [Fact]
    public async Task AcquireSendAndReleaseSpeakTheProtocolAndRestoreTheOriginalMode()
    {
        await using var server = new FakeOpenRgbServer(); server.Devices.Add(DevicePayload("Teclado", "Corsair", 3, active: 1));
        await using var client = await OpenRgbClient.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        var d = (await client.ListAsync(CancellationToken.None)).Single();
        await client.AcquireAsync(d, CancellationToken.None);
        await client.SendAsync(d, new (byte, byte, byte)[] { (1, 2, 3), (4, 5, 6), (7, 8, 9), (10, 11, 12) }, CancellationToken.None);
        await client.ReleaseAsync(d, CancellationToken.None);
        await Task.Delay(300);
        var received = server.Snapshot().Where(r => r.Type >= 1000).ToArray();
        Assert.Equal([1100u, 1050u, 1101u], received.Select(r => r.Type));
        var leds = received[1].Payload;
        Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(leds.AsSpan(4)));
        Assert.Equal(new byte[] { 1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0 }, leds[6..18]);
        var mode = received[2].Payload;
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(mode.AsSpan(4)));
        Assert.Equal(d.ActiveModeBytes, mode[8..]);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("example.com")]
    [InlineData("0.0.0.0")]
    public async Task OnlyLoopbackHostsAreAccepted(string host) =>
        await Assert.ThrowsAsync<OpenRgbException>(() => OpenRgbClient.ConnectAsync(host, 6742, CancellationToken.None));

    [Fact]
    public async Task RefusedConnectionExplainsHowToStartTheServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var e = await Assert.ThrowsAsync<OpenRgbException>(() => OpenRgbClient.ConnectAsync("localhost", port, CancellationToken.None));
        Assert.Contains("servidor SDK", e.Message);
    }

    [Fact]
    public async Task GarbageAndTruncatedResponsesFailCleanly()
    {
        await using (var server = new FakeOpenRgbServer { SendGarbage = true })
        {
            await using var client = await OpenRgbClient.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
            await Assert.ThrowsAsync<OpenRgbException>(() => client.ListAsync(CancellationToken.None));
        }
        await using (var server = new FakeOpenRgbServer()) 
        {
            server.Devices.Add(DevicePayload("Cortado", "X", 3, truncate: true));
            await using var client = await OpenRgbClient.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
            await Assert.ThrowsAsync<OpenRgbException>(() => client.ListAsync(CancellationToken.None));
        }
    }

    [Fact]
    public void ParseDeviceReadsNameVendorAndLeds()
    {
        var (name, vendor, leds, active, mode) = OpenRgbClient.ParseDevice(DevicePayload("Placa", "MSI", 5, 0), 3);
        Assert.Equal(("Placa", "MSI", 5, 0), (name, vendor, leds, active));
        Assert.NotNull(mode);
    }

    [Theory]
    [InlineData(0, 0, 0, 255)] [InlineData(120, 0, 255, 0)] [InlineData(240, 0, 0, 255)] [InlineData(360, 255, 0, 0)] [InlineData(-120, 0, 0, 255)]
    public void HsvPrimaryColours(double h, int r, int g, int b)
    {
        if (h == 0) { Assert.Equal(((byte)255, (byte)0, (byte)0), LightEffects.Hsv(0, 1, 1)); return; }
        Assert.Equal(((byte)r, (byte)g, (byte)b), LightEffects.Hsv(h, 1, 1));
    }

    [Fact]
    public void LiveEffectsMapTemperatureAndLoadToColours()
    {
        Assert.Equal(LightEffects.Hsv(240, 1, 1), LightEffects.Live("cpu", 30));
        Assert.Equal(LightEffects.Hsv(0, 1, 1), LightEffects.Live("gpu", 90));
        Assert.Equal(LightEffects.Hsv(120, 1, 1), LightEffects.Live("load", 0));
        Assert.Equal(LightEffects.Hsv(0, 1, 1), LightEffects.Live("load", 100));
        Assert.Equal(((byte)0, (byte)0, (byte)40), LightEffects.Live("cpu", null));
    }

    [Fact]
    public void FramesApplyBrightnessAndPerEffectRules()
    {
        double[] x = [0, 0.5, 1];
        var white = LightEffects.Frame("static", x, 0, 1, (255, 255, 255), 0.5, null);
        Assert.All(white, c => Assert.Equal(((byte)127, (byte)127, (byte)127), c));
        Assert.All(LightEffects.Frame("off", x, 5, 1, (255, 0, 0), 1, null), c => Assert.Equal(((byte)0, (byte)0, (byte)0), c));
        var rainbow = LightEffects.Frame("rainbow", x, 0, 1, (0, 0, 0), 1, null);
        Assert.NotEqual(rainbow[0], rainbow[1]);
        var breathDark = LightEffects.Frame("breath", [0.0], 0, 1, (200, 200, 200), 1, null)[0];
        Assert.True(breathDark.R < 20);
        Assert.Throws<ArgumentException>(() => LightEffects.Frame("strobe", x, 0, 1, (1, 1, 1), 1, null));
        Assert.Equal(10, LightEffects.Smooth(double.NaN, 10));
        Assert.InRange(LightEffects.Smooth(10, 20), 10.7, 10.9);
    }
}
