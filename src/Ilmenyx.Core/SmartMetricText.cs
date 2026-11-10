// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

/// <summary>Text for raw SMART figures. Presentation only: no thresholds, verdicts or health wording.</summary>
public static class SmartMetricText
{
    public static string Describe(SmartMetric m)
    {
        string number = m.Value == Math.Floor(m.Value) && Math.Abs(m.Value) < 1e15 ? m.Value.ToString("0", CultureInfo.InvariantCulture) : m.Value.ToString("0.##", CultureInfo.InvariantCulture);
        return m.Unit switch
        {
            "B" => m.Value >= 1e12 ? (m.Value / 1e12).ToString("0.00", CultureInfo.InvariantCulture) + " TB" : (m.Value / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + " GB",
            "%" => number + "%",
            _ => number + " " + m.Unit
        };
    }
    public static IReadOnlyList<string> Lines(SmartDiagnostic d) => (d.Metrics ?? []).Select(m => $"{m.Name}: {Describe(m)}").ToArray();
}
