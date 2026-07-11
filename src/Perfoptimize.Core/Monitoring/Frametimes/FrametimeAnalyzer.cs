namespace Perfoptimize.Core.Monitoring.Frametimes;

/// <summary>
/// FPS statistics computed from raw frame times. Averages hide stutter, so the
/// percentile lows and the stutter count matter as much as the average:
/// a game at 70 average FPS with heavy hitches feels worse than a stable 60.
/// </summary>
public sealed class FrametimeStatistics
{
    public int FrameCount { get; init; }
    public double AverageFps { get; init; }
    /// <summary>Average FPS over the worst 1% of frames — the "1% low" gamers know.</summary>
    public double OnePercentLowFps { get; init; }
    public double WorstFrametimeMs { get; init; }
    public double AverageFrametimeMs { get; init; }
    /// <summary>Frames that took at least twice the median time: perceived hitches.</summary>
    public int StutterCount { get; init; }
    /// <summary>Share of frames that are stutters, from 0 to 100.</summary>
    public double StutterPercent { get; init; }
}

public static class FrametimeAnalyzer
{
    private const double StutterFactor = 2.0;

    /// <summary>Computes statistics from frame times in milliseconds; null when empty.</summary>
    public static FrametimeStatistics? Analyze(IReadOnlyList<double> frametimesMs)
    {
        var valid = frametimesMs.Where(t => t > 0 && !double.IsNaN(t) && !double.IsInfinity(t)).ToList();
        if (valid.Count == 0)
            return null;

        var sortedDescending = valid.OrderByDescending(t => t).ToList();
        var average = valid.Average();

        // "1% low": the average frame rate inside the slowest 1% of frames.
        var worstCount = Math.Max(1, valid.Count / 100);
        var onePercentLowMs = sortedDescending.Take(worstCount).Average();

        var median = Median(valid);
        var stutterThreshold = median * StutterFactor;
        var stutters = valid.Count(t => t >= stutterThreshold);

        return new FrametimeStatistics
        {
            FrameCount = valid.Count,
            AverageFps = Math.Round(1000.0 / average, 1),
            OnePercentLowFps = Math.Round(1000.0 / onePercentLowMs, 1),
            WorstFrametimeMs = Math.Round(sortedDescending[0], 2),
            AverageFrametimeMs = Math.Round(average, 2),
            StutterCount = stutters,
            StutterPercent = Math.Round(100.0 * stutters / valid.Count, 2),
        };
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}
