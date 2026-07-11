namespace Perfoptimize.Core.Monitoring;

/// <summary>Aggregated view of a monitoring session, consumed by the bottleneck detector.</summary>
public sealed class PerformanceSummary
{
    public int SampleCount { get; init; }
    public TimeSpan Duration { get; init; }
    public double? AverageCpuUsagePercent { get; init; }
    public double? PeakCpuUsagePercent { get; init; }
    public double? AverageGpuUsagePercent { get; init; }
    public double? PeakGpuUsagePercent { get; init; }
    public double? AverageRamUsagePercent { get; init; }
    public double? PeakRamUsagePercent { get; init; }
    public double? AverageVramUsagePercent { get; init; }
    public double? PeakVramUsagePercent { get; init; }
    public double? PeakCpuTemperatureC { get; init; }
    public double? PeakGpuTemperatureC { get; init; }
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
            AverageGpuUsagePercent = Average(samples, s => s.GpuUsagePercent),
            PeakGpuUsagePercent = Max(samples, s => s.GpuUsagePercent),
            AverageRamUsagePercent = Average(samples, s => s.RamUsagePercent),
            PeakRamUsagePercent = Max(samples, s => s.RamUsagePercent),
            AverageVramUsagePercent = Average(samples, s => s.VramUsagePercent),
            PeakVramUsagePercent = Max(samples, s => s.VramUsagePercent),
            PeakCpuTemperatureC = Max(samples, s => s.CpuTemperatureC),
            PeakGpuTemperatureC = Max(samples, s => s.GpuTemperatureC),
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
