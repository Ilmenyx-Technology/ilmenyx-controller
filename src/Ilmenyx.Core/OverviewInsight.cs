// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

public enum InsightLevel { Fine, Watch, Critical }

/// <summary>Plain verdicts behind the simple Overview ("Seu PC agora"): pure rules over what the live monitor already samples.</summary>
public static class OverviewInsight
{
    /// <summary>A quarter of the RAM pushed to swap means the programs no longer fit, even when "used" looks moderate (zram counts as free).</summary>
    public static bool HeavySwap(long total, long swapUsed) => total > 0 && swapUsed >= total / 4;

    /// <summary>Memory: critical from 95% in use, tight from 85% or when a lot went to swap.</summary>
    public static InsightLevel Memory(long total, long used, long swapUsed) =>
        total <= 0 ? InsightLevel.Fine : used >= total * 0.95 ? InsightLevel.Critical : used >= total * 0.85 || HeavySwap(total, swapUsed) ? InsightLevel.Watch : InsightLevel.Fine;

    /// <summary>The worst thing first, in a few words (notification-area menu and tooltip). <paramref name="diskFull"/> is 0–1.</summary>
    public static (InsightLevel Level, string Text) Headline(double? cpuTempC, double? diskFull, InsightLevel memory) =>
        cpuTempC >= 95 ? (InsightLevel.Critical, L.T("Processador muito quente"))
        : diskFull >= 0.9 ? (InsightLevel.Critical, L.T("Disco quase cheio"))
        : memory == InsightLevel.Critical ? (InsightLevel.Critical, L.T("Memória no fim"))
        : memory == InsightLevel.Watch ? (InsightLevel.Watch, L.T("Memória apertada"))
        : cpuTempC >= 80 ? (InsightLevel.Watch, L.T("Processador quente"))
        : (InsightLevel.Fine, L.T("Está tudo bem"));

    /// <summary>The app worth closing first: the biggest one sitting idle (under 1% CPU) with at least 500 MiB; null when none.</summary>
    public static AppGroup? IdleHog(IEnumerable<AppGroup> apps) =>
        apps.Where(a => a.CpuPercent is < 1 && a.ResidentBytes >= 500L << 20).OrderByDescending(a => a.ResidentBytes).FirstOrDefault();
}
