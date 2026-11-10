// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Adapted from RESET EXPLODE LightBackends.cs OpenRgbBackend (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ilmenyx.Core;

public sealed record LightDevice(int Index, string Id, string Name, string Vendor, int Leds, int ActiveMode, byte[]? ActiveModeBytes)
{
    public string Detail => $"OpenRGB · {(string.IsNullOrWhiteSpace(Vendor) ? "—" : Vendor)} · {Leds} LEDs";
    /// <summary>Horizontal position (0–1) of each LED, used by position-dependent effects.</summary>
    public double[] LedX() { var x = new double[Leds]; for (int k = 0; k < Leds; k++) x[k] = Leds > 1 ? k / (double)(Leds - 1) : 0; return x; }
}

public sealed class OpenRgbException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>
/// Client of the OpenRGB SDK server (TCP, protocol 3 = OpenRGB 0.9+). Connects only to loopback: the server has no authentication and
/// Ilmenyx never controls lights over the network. It changes lights only after Acquire and always restores the previous mode in Release.
/// </summary>
public sealed class OpenRgbClient : IAsyncDisposable
{
    public const int DefaultPort = 6742, MaxPacket = 4_000_000, MaxDevices = 128, MaxLeds = 4096;
    private const uint ReqControllerCount = 0, ReqControllerData = 1, ReqProtocolVersion = 40, SetClientName = 50, UpdateMode = 1101, UpdateLeds = 1050, SetCustomMode = 1100;
    private readonly Stream stream;
    private readonly IDisposable? owner;
    private readonly Dictionary<string, LightDevice> devices = [];
    public int Protocol { get; private set; } = 3;
    public int MinIntervalMs => 25;

    public OpenRgbClient(Stream stream, IDisposable? owner = null) { this.stream = stream; this.owner = owner; }

    public static bool IsLoopback(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);

    public static async Task<OpenRgbClient> ConnectAsync(string host, int port, CancellationToken ct)
    {
        if (!IsLoopback(host)) throw new OpenRgbException("O Ilmenyx só se conecta ao OpenRGB na própria máquina (localhost): o servidor SDK não tem autenticação.");
        if (port is < 1024 or > 65535) throw new OpenRgbException("Porta inválida.");
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync(host, port, timeout.Token);
            return new OpenRgbClient(tcp.GetStream(), tcp);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            tcp.Dispose();
            throw new OpenRgbException($"O OpenRGB não está com o servidor SDK ligado em {host}:{port} (abra o OpenRGB e ative Server → Start Server, ou rode `openrgb --server`).", e);
        }
    }

    private async Task WriteAsync(uint device, uint type, byte[] payload, CancellationToken ct)
    {
        var packet = new byte[16 + payload.Length];
        "ORGB"u8.CopyTo(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), device);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), type);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), (uint)payload.Length);
        payload.CopyTo(packet, 16);
        await stream.WriteAsync(packet, ct);
        await stream.FlushAsync(ct);
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var buffer = new byte[count]; int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct);
            if (n <= 0) throw new OpenRgbException("Conexão encerrada pelo OpenRGB.");
            read += n;
        }
        return buffer;
    }

    /// <summary>Reads packets until the wanted type arrives (asynchronous server notices are skipped).</summary>
    private async Task<byte[]> ReadTypeAsync(uint wanted, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            for (int i = 0; i < 20; i++)
            {
                var header = await ReadExactAsync(16, timeout.Token);
                if (header[0] != 'O' || header[1] != 'R' || header[2] != 'G' || header[3] != 'B') throw new OpenRgbException("Resposta inválida (não é o protocolo do OpenRGB).");
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)), size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
                if (size > MaxPacket) throw new OpenRgbException("Pacote grande demais.");
                var data = size > 0 ? await ReadExactAsync((int)size, timeout.Token) : [];
                if (type == wanted) return data;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new OpenRgbException("O OpenRGB não respondeu a tempo."); }
        throw new OpenRgbException("O OpenRGB não respondeu como esperado.");
    }

    private static string Str(BinaryReader r)
    {
        int len = r.ReadUInt16();
        var b = r.ReadBytes(len);
        if (b.Length != len) throw new OpenRgbException("Dados do dispositivo truncados.");
        int n = b.Length; while (n > 0 && b[n - 1] == 0) n--;
        return Encoding.UTF8.GetString(b, 0, n);
    }

    internal static (string Name, string Vendor, int Leds, int Active, byte[]? ModeBytes) ParseDevice(byte[] data, int protocol)
    {
        try
        {
            using var ms = new MemoryStream(data); using var r = new BinaryReader(ms);
            r.ReadUInt32(); r.ReadInt32();
            string name = Str(r), vendor = protocol >= 1 ? Str(r) : "";
            Str(r); Str(r); Str(r); Str(r);
            int numModes = r.ReadUInt16(), active = r.ReadInt32(); byte[]? activeBytes = null;
            for (int m = 0; m < numModes; m++)
            {
                int start = (int)ms.Position;
                Str(r); r.ReadInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
                if (protocol >= 3) { r.ReadUInt32(); r.ReadUInt32(); }
                r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
                if (protocol >= 3) r.ReadUInt32();
                r.ReadUInt32(); r.ReadUInt32();
                int nc = r.ReadUInt16(); if (r.ReadBytes(nc * 4).Length != nc * 4) throw new EndOfStreamException();
                if (m == active) activeBytes = data[start..(int)ms.Position];
            }
            int numZones = r.ReadUInt16();
            for (int z = 0; z < numZones; z++)
            {
                Str(r); r.ReadInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
                int matrixLen = r.ReadUInt16(); if (matrixLen > 0 && r.ReadBytes(matrixLen).Length != matrixLen) throw new EndOfStreamException();
            }
            return (name, vendor, r.ReadUInt16(), active, activeBytes);
        }
        catch (EndOfStreamException e) { throw new OpenRgbException("Dados do dispositivo truncados.", e); }
    }

    /// <summary>Negotiates the protocol and lists devices that have LEDs.</summary>
    public async Task<IReadOnlyList<LightDevice>> ListAsync(CancellationToken ct)
    {
        await WriteAsync(0, SetClientName, Encoding.ASCII.GetBytes("Ilmenyx\0"), ct);
        await WriteAsync(0, ReqProtocolVersion, BitConverter.GetBytes(3u), ct);
        uint server;
        try { server = BitConverter.ToUInt32(await ReadTypeAsync(ReqProtocolVersion, ct), 0); }
        catch (OpenRgbException) { throw new OpenRgbException("O OpenRGB é antigo demais (sem negociação de protocolo). Atualize para a versão 0.9 ou mais nova."); }
        Protocol = (int)Math.Min(3u, server);
        if (Protocol < 3) throw new OpenRgbException($"Protocolo {Protocol} do OpenRGB é antigo. Atualize para a versão 0.9 ou mais nova.");
        await WriteAsync(0, ReqControllerCount, [], ct);
        int count = (int)Math.Min(BitConverter.ToUInt32(await ReadTypeAsync(ReqControllerCount, ct), 0), (uint)MaxDevices);
        devices.Clear();
        for (int i = 0; i < count; i++)
        {
            await WriteAsync((uint)i, ReqControllerData, BitConverter.GetBytes((uint)Protocol), ct);
            var (name, vendor, leds, active, modeBytes) = ParseDevice(await ReadTypeAsync(ReqControllerData, ct), Protocol);
            if (leds <= 0 || leds > MaxLeds) continue;
            var device = new LightDevice(i, $"{i}:{name}", name, vendor, leds, active, modeBytes);
            devices[device.Id] = device;
        }
        return devices.Values.ToList();
    }

    /// <summary>Takes control: switches the device to its custom (direct) mode.</summary>
    public Task AcquireAsync(LightDevice d, CancellationToken ct) => WriteAsync((uint)d.Index, SetCustomMode, [], ct);

    public Task SendAsync(LightDevice d, ReadOnlySpan<(byte R, byte G, byte B)> colors, CancellationToken ct)
    {
        int n = Math.Min(colors.Length, d.Leds);
        var p = new byte[6 + 4 * n];
        BinaryPrimitives.WriteUInt32LittleEndian(p, (uint)p.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), (ushort)n);
        for (int i = 0; i < n; i++) { p[6 + i * 4] = colors[i].R; p[7 + i * 4] = colors[i].G; p[8 + i * 4] = colors[i].B; }
        return WriteAsync((uint)d.Index, UpdateLeds, p, ct);
    }

    /// <summary>Restores the mode that was active when Ilmenyx connected. Best effort: a dead connection cannot be restored.</summary>
    public async Task ReleaseAsync(LightDevice d, CancellationToken ct)
    {
        if (d.ActiveModeBytes is not { } mode) return;
        var p = new byte[8 + mode.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(p, (uint)p.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), (uint)d.ActiveMode);
        mode.CopyTo(p, 8);
        await WriteAsync((uint)d.Index, UpdateMode, p, ct);
    }

    public async ValueTask DisposeAsync() { await stream.DisposeAsync(); owner?.Dispose(); }
}

/// <summary>Lighting effects. Pure functions of time and sensor value so they are testable without any device.</summary>
public static class LightEffects
{
    public static readonly string[] Names = ["static", "breath", "rainbow", "cpu", "gpu", "load", "off"];

    public static (byte R, byte G, byte B) Hsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    /// <summary>Exponential smoothing of the live value (NaN = first sample).</summary>
    public static double Smooth(double previous, double value) => double.IsNaN(previous) ? value : previous + (value - previous) * 0.08;

    /// <summary>Colour for a live-value effect: temperature blue→red between 35 and 85 °C, load green→red; sensor missing = dim blue.</summary>
    public static (byte R, byte G, byte B) Live(string effect, double? value) => value is not double v ? ((byte)0, (byte)0, (byte)40)
        : effect == "load" ? Hsv(120 * (1 - Math.Clamp(v / 100.0, 0, 1)), 1, 1) : Hsv(240 * (1 - Math.Clamp((v - 35) / 50.0, 0, 1)), 1, 1);

    public static (byte R, byte G, byte B)[] Frame(string effect, double[] ledX, double seconds, double speed, (byte R, byte G, byte B) color, double brightness, double? live)
    {
        if (!Names.Contains(effect)) throw new ArgumentException("Efeito desconhecido.", nameof(effect));
        brightness = Math.Clamp(brightness, 0, 1);
        var colors = new (byte, byte, byte)[ledX.Length];
        var liveColor = effect is "cpu" or "gpu" or "load" ? Live(effect, live) : default;
        for (int i = 0; i < colors.Length; i++)
        {
            (byte R, byte G, byte B) c;
            switch (effect)
            {
                case "breath":
                    double k = 0.04 + 0.96 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * (0.1 * speed) * seconds));
                    c = ((byte)(color.R * k), (byte)(color.G * k), (byte)(color.B * k)); break;
                case "rainbow": c = Hsv(ledX[i] * 360 + seconds * speed * 36, 1, 1); break;
                case "cpu": case "gpu": case "load": c = liveColor; break;
                case "off": c = ((byte)0, (byte)0, (byte)0); break;
                default: c = color; break;
            }
            colors[i] = ((byte)(c.R * brightness), (byte)(c.G * brightness), (byte)(c.B * brightness));
        }
        return colors;
    }
}
