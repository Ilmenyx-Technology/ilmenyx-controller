// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Contracts;

/// <summary>Offline analysis. ImportedAt is not the original device-query timestamp.</summary>
public sealed record SmartImportReport(string SchemaVersion, string ReportKind, DateTimeOffset ImportedAt,
    SmartDiagnostic Diagnostic, IReadOnlyList<string> Limitations);
