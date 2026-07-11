namespace Perfoptimize.Core.Monitoring;

/// <summary>Aggregated view of a monitoring session, consumed by the bottleneck detector.</summary>
public sealed class PerformanceSummary
{
    public int SampleCount { get; init; }
    public TimeSpan Duration { get; init; }
    public double? AverageCpuUsagePercent { get; init; }
    public double? PeakCpuUsagePercent { get; init; }
    /// <summary>Average usage of the busiest logical core — exposes single-thread limits.</summary>
    public double? AverageMaxCoreUsagePercent { get; init; }
    public double? PeakMaxCoreUsagePercent { get; init; }
    /// <summary>CPU used by the game process, as a share of the whole machine.</summary>
    public double? AverageGameCpuUsagePercent { get; init; }
    public double? AverageGpuUsagePercent { get; init; }
    public double? PeakGpuUsagePercent { get; init; }
    /// <summary>GPU usage attributed to the game process (Windows GPU Engine counters).</summary>
    public double? AverageGameGpuUsagePercent { get; init; }
    public double? AverageRamUsagePercent { get; init; }
    public double? PeakRamUsagePercent { get; init; }
    public double? AverageVramUsagePercent { get; init; }
    public double? PeakVramUsagePercent { get; init; }
    public double? PeakCpuTemperatureC { get; init; }
    public double? PeakGpuTemperatureC { get; init; }
    /// <summary>Average GPU core clock over the session, for throttle analysis.</summary>
    public double? AverageGpuCoreClockMhz { get; init; }
    /// <summary>True when the driver reported an active thermal slowdown at least once.</summary>
    public bool GpuThermalThrottleObserved { get; init; }
    /// <summary>True when the driver reported a non-thermal hardware slowdown at least once.</summary>
    public bool GpuHardwareSlowdownObserved { get; init; }
}

public static class TelemetryAggregator
{
    public static PerformanceSummary Summarize(IReadOnlyList<TelemetrySample> samples)
    {
        if (samples.Count == 0)
            return new PerformanceSummary();

        return new PerformanceSummary
        {
            SampleCount = samples.Count,
            Duration = samples[^1].Timestamp - samples[0].Timestamp,
            AverageCpuUsagePercent = Average(samples, s => s.CpuUsagePercent),
            PeakCpuUsagePercent = Max(samples, s => s.CpuUsagePercent),
            AverageMaxCoreUsagePercent = Average(samples, s => s.MaxCoreUsagePercent),
            PeakMaxCoreUsagePercent = Max(samples, s => s.MaxCoreUsagePercent),
            AverageGameCpuUsagePercent = Average(samples, s => s.GameCpuUsagePercent),
            AverageGpuUsagePercent = Average(samples, s => s.GpuUsagePercent),
            PeakGpuUsagePercent = Max(samples, s => s.GpuUsagePercent),
            AverageGameGpuUsagePercent = Average(samples, s => s.GameGpuUsagePercent),
            AverageRamUsagePercent = Average(samples, s => s.RamUsagePercent),
            PeakRamUsagePercent = Max(samples, s => s.RamUsagePercent),
            AverageVramUsagePercent = Average(samples, s => s.VramUsagePercent),
            PeakVramUsagePercent = Max(samples, s => s.VramUsagePercent),
            PeakCpuTemperatureC = Max(samples, s => s.CpuTemperatureC),
            PeakGpuTemperatureC = Max(samples, s => s.GpuTemperatureC),
            AverageGpuCoreClockMhz = Average(samples, s => s.GpuCoreClockMhz),
            GpuThermalThrottleObserved = samples.Any(s => s.GpuThermalThrottleActive == true),
            GpuHardwareSlowdownObserved = samples.Any(s => s.GpuHardwareSlowdownActive == true),
        };
    }

    private static double? Average(IReadOnlyList<TelemetrySample> samples, Func<TelemetrySample, double?> selector)
    {
        var values = samples.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count > 0 ? Math.Round(values.Average(), 1) : null;
    }

    private static double? Max(IReadOnlyList<TelemetrySample> samples, Func<TelemetrySample, double?> selector)
    {
        var values = samples.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count > 0 ? Math.Round(values.Max(), 1) : null;
    }
}
