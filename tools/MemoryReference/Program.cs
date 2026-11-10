// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Test-only bounded oracle derived at generation time from MIT upstream MemoryBench.cs.
// Original copyright (c) 2026 Gustavo (GustavoReset), MIT. No source copied into product projects.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 1 || ReferenceMemory.LatencyBytes != 262144 || ReferenceMemory.BandwidthBytes != 262144)
    throw new ArgumentException("Only the fixed bounded oracle is permitted.");
var cases = new List<object>();
foreach (int threads in new[] { 1, 2, 4 })
{
    ReferenceMemory.Threads = threads;
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    ReferenceMemory.MeasureLatencyNs(stop.Token);
    ReferenceMemory.MeasureBandwidth(stop.Token);
    stop.Token.ThrowIfCancellationRequested();
    cases.Add(new { threads, finalIndex = ReferenceMemory.FinalIndex, ringSha256 = ReferenceMemory.RingHash,
        readSum = ReferenceMemory.ReadSum, copySum = ReferenceMemory.CopySum, copySha256 = ReferenceMemory.CopyHash });
}
var report = new { source = "MemoryBench.cs RAM methods, MIT GustavoReset", referenceCommit = "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6",
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    workingSetBytes = 262144, warmupSteps = 1024, traversalSteps = 32768, bandwidthRepetitions = 1, cases };
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("PASS bounded upstream RAM oracle: 1/2/4 workers, no full buffers, no disk methods, no performance metrics retained.");
static class ReferenceMemory
{
    public const long LatencyBytes = 262144, BandwidthBytes = 262144;
    public static int Threads, FinalIndex;
    public static long ReadSum, CopySum;
    public static string RingHash = "", CopyHash = "";
    private static string Hash(long[] values)
    {
        var bytes = new byte[values.Length * sizeof(long)];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8), values[i]);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    // INJECT_RAM_METHODS
}
