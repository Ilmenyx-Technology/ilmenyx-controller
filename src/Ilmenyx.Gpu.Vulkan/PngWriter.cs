// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Buffers.Binary;
using System.IO.Compression;

namespace Ilmenyx.Gpu.Vulkan;

/// <summary>Minimal 8-bit RGBA PNG encoder (no dependencies) used for GPU scene snapshots.</summary>
public static class PngWriter
{
    private static readonly uint[] Table = BuildTable();
    private static uint[] BuildTable() { var t = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; } return t; }
    internal static uint Crc(ReadOnlySpan<byte> data, uint crc = 0xFFFFFFFF) { foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8); return crc; }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length); output.Write(length);
        var name = System.Text.Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(data);
        uint crc = Crc(data, Crc(name)) ^ 0xFFFFFFFF; Span<byte> c = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(c, crc); output.Write(c);
    }

    /// <summary>Encodes RGBA rows (width*height*4 bytes, top row first).</summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4) throw new ArgumentException("Dimensões não conferem com os dados.");
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = 8; header[9] = 6; header[10] = 0; header[11] = 0; header[12] = 0;
        Chunk(png, "IHDR", header);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
            for (int y = 0; y < height; y++) { z.WriteByte(0); z.Write(rgba.Slice(y * width * 4, width * 4)); }
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }
}
