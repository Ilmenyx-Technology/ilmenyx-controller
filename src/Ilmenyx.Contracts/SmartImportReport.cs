// New Ilmenyx additions: see LICENSING.md.
namespace Ilmenyx.Contracts;

/// <summary>Offline analysis. ImportedAt is not the original device-query timestamp.</summary>
public sealed record SmartImportReport(string SchemaVersion, string ReportKind, DateTimeOffset ImportedAt,
    SmartDiagnostic Diagnostic, IReadOnlyList<string> Limitations);
