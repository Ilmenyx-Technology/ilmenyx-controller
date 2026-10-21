namespace Silex.Audit.Core.Benchmarks;

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
