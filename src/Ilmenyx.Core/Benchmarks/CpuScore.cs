// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core.Benchmarks;

public static class CpuScore
{
    // Original arithmetic; publishing a value still requires a completed full profile.
    public static double Points(long completedSamples, double seconds)
    {
        if (completedSamples < 0) throw new ArgumentOutOfRangeException(nameof(completedSamples));
        if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        return completedSamples / seconds / 10_000;
    }
}
