// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Contracts;

/// <summary>Raw firmware-reported figure with unit and provenance; never a verdict.</summary>
public sealed record SmartMetric(string Name, double Value, string Unit, string Source);
public sealed record SmartDiagnostic(string SchemaVersion, DateTimeOffset CollectedAt, string Device,
    string Status, string? Reason, int? ToolExitCode, Reading<bool?> ReportedPassed, IReadOnlyList<string> Limitations,
    IReadOnlyList<SmartMetric>? Metrics = null);
