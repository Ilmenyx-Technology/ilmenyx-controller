// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Source shared (linked) by the unit tests and the graphical smoke; loopback only, no real device.
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ilmenyx.TestSupport;

/// <summary>Minimal OpenRGB SDK server (protocol 3) for tests: answers the handshake and records every other packet.</summary>
public sealed class FakeOpenRgbServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public uint ServerProtocol = 3;
    public bool SendGarbage;
    public List<byte[]> Devices { get; } = [];
    private readonly List<(uint Device, uint Type, byte[] Payload)> received = [];

    public FakeOpenRgbServer() { listener.Start(); loop = Task.Run(Serve); }

    public static byte[] Str(string s) { var b = Encoding.UTF8.GetBytes(s + "\0"); var o = new byte[2 + b.Length]; BinaryPrimitives.WriteUInt16LittleEndian(o, (ushort)b.Length); b.CopyTo(o, 2); return o; }
    public static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }
    public static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); return b; }

    public static byte[] DevicePayload(string name, string vendor, int leds, int active = 1, bool truncate = false)
    {
        var ms = new MemoryStream();
        void W(byte[] b) => ms.Write(b);
        W(U32(0)); W(U32(5)); W(Str(name)); W(Str(vendor)); W(Str("desc")); W(Str("1.0")); W(Str("SERIAL-1")); W(Str("HID: /dev/hidraw3"));
        W(U16(2)); W(U32((uint)active));
        for (int m = 0; m < 2; m++)
        {
            W(Str(m == 0 ? "Direct" : "Static")); W(U32((uint)m)); W(U32(0)); W(U32(0)); W(U32(100));
            W(U32(0)); W(U32(100)); W(U32(1)); W(U32(1)); W(U32(50)); W(U32(100)); W(U32(0)); W(U32(1)); W(U16(1)); W([255, 0, 0, 0]);
        }
        W(U16(1)); W(Str("Zone")); W(U32(1)); W(U32(1)); W(U32((uint)leds)); W(U32((uint)leds)); W(U16(0));
        W(U16((ushort)leds));
        for (int i = 0; i < leds; i++) { W(Str("LED " + i)); W(U32(0)); }
        var all = ms.ToArray();
        return truncate ? all[..40] : all;
    }

    public (uint Device, uint Type, byte[] Payload)[] Snapshot() { lock (received) return received.ToArray(); }

    private async Task Serve()
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            var s = client.GetStream();
            while (!stop.IsCancellationRequested)
            {
                var h = new byte[16]; int read = 0;
                while (read < 16) { int n = await s.ReadAsync(h.AsMemory(read), stop.Token); if (n <= 0) return; read += n; }
                uint dev = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4)), type = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8)), size = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12));
                var payload = new byte[size]; read = 0;
                while (read < size) { int n = await s.ReadAsync(payload.AsMemory(read), stop.Token); if (n <= 0) return; read += n; }
                lock (received) received.Add((dev, type, payload));
                switch (type)
                {
                    case 40: await Reply(s, 40, U32(ServerProtocol)); break;
                    case 0: await Reply(s, 0, U32((uint)Devices.Count)); break;
                    case 1: await Reply(s, 1, Devices[(int)dev]); break;
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or SocketException) { }
    }

    private async Task Reply(NetworkStream s, uint type, byte[] payload) =>
        await s.WriteAsync(new byte[] { (byte)'O', (byte)'R', (byte)'G', (byte)'B' }.Concat(SendGarbage ? new byte[12] : U32(0).Concat(U32(type)).Concat(U32((uint)payload.Length)).ToArray()).Concat(payload).ToArray());

    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); try { await loop; } catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { } }
}
