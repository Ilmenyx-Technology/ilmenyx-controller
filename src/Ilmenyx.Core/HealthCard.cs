// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions, adapted from the verdict model of the original DiskHealthCard (MIT, see NOTICE): see LICENSING.md.
namespace Ilmenyx.Core;

public enum HealthVerdict { Good, Warning, Critical, Unknown }

/// <summary>Ready-to-render card (disks and batteries): title, meta line, verdict badge, reasons and metric pairs.</summary>
public sealed class HealthCard
{
    public string Title { get; init; } = "";
    public string Meta { get; init; } = "";
    public string Device { get; init; } = "";
    public HealthVerdict Verdict { get; init; } = HealthVerdict.Unknown;
    /// <summary>Badge text; when empty the disk wording is used.</summary>
    public string? Label { get; init; }
    public string VerdictText => Label ?? Verdict switch { HealthVerdict.Good => "SAUDÁVEL", HealthVerdict.Warning => "ATENÇÃO", HealthVerdict.Critical => "CRÍTICO", _ => "SEM DADOS" };
    public bool IsAlert => Verdict != HealthVerdict.Good;
    public string Reasons { get; init; } = "";
    public List<KeyValuePair<string, string>> Metrics { get; init; } = [];
}
