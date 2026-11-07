// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Test fixture generator only. Compiled with the MIT upstream source in an isolated temp directory.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ResetExplode;

if (args.Length != 2) throw new ArgumentException("Expected destination and fixed oracle mode.");
if (args[1] == "original-pixels")
{
    GenerateOriginalPixels(args[0]);
    return;
}
if (args[1] != "reduced") throw new ArgumentException("Unknown oracle mode.");
if (!HasConstants(32, 18, 8, 8))
    throw new InvalidOperationException("Reference generator only permits the fixed reduced workload.");
byte[]? image = null;
foreach (int threads in new[] { 1, 4 })
{
    var renderer = new BenchRenderer();
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var result = renderer.Run(threads, TimeSpan.Zero, stop.Token);
    if (stop.IsCancellationRequested || result.Passes != 1 || result.Samples != 4608)
        throw new InvalidOperationException("Reference did not complete.");
    var bytes = new byte[32 * 18 * 4];
    for (int i = 0; i < renderer.Pixels.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), renderer.Pixels[i]);
    if (image is not null && !image.SequenceEqual(bytes)) throw new InvalidOperationException("Reference is not deterministic.");
    image = bytes;
}
Directory.CreateDirectory(args[0]);
File.WriteAllBytes(Path.Combine(args[0], "cpu-reference.bgra"), image!);
var metadata = new
{
    upstreamCommit = "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6",
    source = "upstream/ResetExplode/BenchRenderer.cs",
    changes = "Only Width=32, Height=18, Spp=8, BucketSize=8; all rendering code unchanged.",
    width = 32, height = 18, samplesPerPixel = 8, totalSamples = 4608,
    encoding = "BGRA32 little-endian, row-major", sha256 = Convert.ToHexString(SHA256.HashData(image!)).ToLowerInvariant(),
    runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    operatingSystem = RuntimeInformation.OSDescription, threadsVerified = new[] { 1, 4 },
    copyright = "Copyright (c) 2026 Gustavo (GustavoReset); upstream MIT license retained."
};
string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(args[0], "cpu-reference.json"), json + "\n");
Console.WriteLine(json);


static void GenerateOriginalPixels(string destination)
{
    if (!HasConstants(960, 540, 1024, 32))
        throw new InvalidOperationException("Original-dimension oracle requires unchanged constants.");
    // Call only 1x1 buckets, NEVER Run(). These fixed 16 points bound the entire workload.
    (int X, int Y)[] points = { (0, 0), (959, 0), (0, 539), (959, 539), (480, 270), (320, 270),
        (640, 270), (480, 120), (480, 420), (160, 360), (800, 360), (320, 180),
        (640, 180), (479, 269), (31, 31), (928, 512) };
    if (points.Length != 16) throw new InvalidOperationException("Oracle budget exceeded.");
    var method = typeof(BenchRenderer).GetMethod("RenderBucket", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("RenderBucket");
    var renderer = new BenchRenderer();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var pixels = new List<object>();
    var bytes = new byte[points.Length * 4];
    for (int index = 0; index < points.Length; index++)
    {
        timeout.Token.ThrowIfCancellationRequested();
        var (x, y) = points[index];
        method.Invoke(renderer, new object[] { (x, y, 1, 1), timeout.Token });
        timeout.Token.ThrowIfCancellationRequested();
        int bgra = renderer.Pixels[y * BenchRenderer.Width + x];
        pixels.Add(new { x, y, bgra });
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), bgra);
    }
    var metadata = new
    {
        upstreamCommit = "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6",
        source = "upstream/ResetExplode/BenchRenderer.cs", changes = "None; invoke RenderBucket only for sixteen fixed 1x1 buckets; never Run().",
        width = 960, height = 540, samplesPerPixel = 1024, selectedPixels = 16, totalSamples = 16384,
        sha256Bgra = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), pixels,
        runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        copyright = "Copyright (c) 2026 Gustavo (GustavoReset); upstream MIT license retained."
    };
    string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
    Directory.CreateDirectory(destination);
    File.WriteAllText(Path.Combine(destination, "cpu-original-pixels.json"), json + "\n");
    Console.WriteLine(json);
}

static bool HasConstants(int width, int height, int samples, int bucket) =>
    BenchRenderer.Width == width && BenchRenderer.Height == height && BenchRenderer.Spp == samples && BenchRenderer.BucketSize == bucket;
