namespace Perfoptimize.Core.Live;

/// <summary>One live FPS reading at a point in time.</summary>
public readonly record struct FpsReading(DateTimeOffset At, double Fps);

/// <summary>A detected step change in FPS: the level before and after, and the delta.</summary>
public sealed record FpsChange(double Before, double After, double DeltaPercent);

/// <summary>
/// Watches a rolling series of FPS readings and answers two questions without
/// the user having to launch anything: what is the current stable FPS, and did
/// it just step up or down (because a setting was changed)? Pure and
/// unit-tested. The change is intentionally transient — once the new level has
/// been steady long enough to become the baseline, it stops being reported.
/// </summary>
public static class FpsChangeDetector
{
    public static (double? StableFps, FpsChange? Change) Analyze(
        IReadOnlyList<FpsReading> readings,
        int recentCount = 3,
        int baselineCount = 5,
        double minDeltaPercent = 5,
        double minDeltaFps = 3,
        double maxRecentSpreadFraction = 0.18)
    {
        if (readings.Count == 0)
            return (null, null);
        if (readings.Count < recentCount)
            return (Median(readings.Select(r => r.Fps)), null);

        var recent = readings.Skip(readings.Count - recentCount).Select(r => r.Fps).ToList();
        var stable = Median(recent);

        if (readings.Count < recentCount + baselineCount)
            return (stable, null);

        // The recent window must be steady; firing mid-transition would show a
        // bogus "change" every step of the way.
        var spread = recent.Max() - recent.Min();
        if (stable > 0 && spread / stable > maxRecentSpreadFraction)
            return (stable, null);

        var baseline = readings
            .Skip(readings.Count - recentCount - baselineCount)
            .Take(baselineCount)
            .Select(r => r.Fps)
            .ToList();
        var baseMedian = Median(baseline);

        var deltaFps = stable - baseMedian;
        var deltaPercent = baseMedian > 0 ? 100.0 * deltaFps / baseMedian : 0;
        if (Math.Abs(deltaFps) >= minDeltaFps && Math.Abs(deltaPercent) >= minDeltaPercent)
            return (stable, new FpsChange(Math.Round(baseMedian), Math.Round(stable), Math.Round(deltaPercent, 1)));

        return (stable, null);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
