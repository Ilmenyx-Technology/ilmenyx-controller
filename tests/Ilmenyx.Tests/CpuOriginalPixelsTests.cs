// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Ilmenyx.Core.Benchmarks;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class CpuOriginalPixelsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void SelectedPixelsMatchOriginalDimensionsAndSampleCount(int threads)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cpu-original-pixels.json")));
        var root = fixture.RootElement;
        Assert.Equal(CpuBenchmarkRunner.ReferenceCommit, root.GetProperty("upstreamCommit").GetString());
        Assert.Equal(960, root.GetProperty("width").GetInt32());
        Assert.Equal(540, root.GetProperty("height").GetInt32());
        Assert.Equal(1024, root.GetProperty("samplesPerPixel").GetInt32());
        var points = root.GetProperty("pixels").EnumerateArray().ToArray();
        Assert.Equal(16, points.Length); // A hard budget: never a full image/pass.
        var scene = new CpuPathScene(960, 540);
        var actual = new int[points.Length];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Parallel.For(0, points.Length, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = timeout.Token }, index =>
        {
            int x = points[index].GetProperty("x").GetInt32(), y = points[index].GetProperty("y").GetInt32();
            Assert.InRange(x, 0, 959); Assert.InRange(y, 0, 539);
            actual[index] = scene.RenderPixel(x, y, 960, 540, 1024, timeout.Token);
            Assert.Equal(points[index].GetProperty("bgra").GetInt32(), actual[index]);
        });
        var bytes = new byte[actual.Length * 4];
        for (int index = 0; index < actual.Length; index++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), actual[index]);
        Assert.Equal("a1b6d7aed6fad8729e62a09a4bf4a3111c9683f65ace706ba0ab2ccc57568622", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}
