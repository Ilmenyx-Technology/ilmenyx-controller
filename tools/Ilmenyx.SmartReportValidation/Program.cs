// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Globalization;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

if (args.Length != 4 || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int exit))
    throw new ArgumentException("Usage: report.json device observed-exit-code new-output.json");
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using var reader = File.OpenText(args[0]);
var buffer = new char[65537]; int length = 0, count;
while ((count = await reader.ReadAsync(buffer.AsMemory(length), stop.Token)) > 0)
{
    length += count;
    if (length > 65536) throw new IOException("Report exceeds 65536 characters.");
}
var result = SmartctlDiagnostic.Parse(args[1], new(exit, new string(buffer, 0, length)));
if (result.Status != "available") throw new InvalidDataException(result.Reason);
await SnapshotReport.ExportAsync(result, args[3], stop.Token);
Console.WriteLine($"PASS production C# parser: status={result.Status}, toolExitCode={result.ToolExitCode}, reportedPassed={result.ReportedPassed.Value}");
Console.WriteLine("No device query, privilege elevation or health guarantee. Filtered output: " + args[3]);
