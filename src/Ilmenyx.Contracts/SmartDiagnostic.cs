namespace Ilmenyx.Contracts;

public sealed record SmartDiagnostic(string SchemaVersion, DateTimeOffset CollectedAt, string Device,
    string Status, string? Reason, int? ToolExitCode, Reading<bool?> ReportedPassed, IReadOnlyList<string> Limitations);
