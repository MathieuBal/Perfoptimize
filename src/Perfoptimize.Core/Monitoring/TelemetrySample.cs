namespace Perfoptimize.Core.Monitoring;

/// <summary>
/// One system-wide measurement. Every value is nullable: sensors differ per
/// machine, and a missing reading must be distinguishable from a zero.
/// </summary>
public sealed class TelemetrySample
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public double? CpuUsagePercent { get; init; }
    public double? RamUsagePercent { get; init; }
    public double? GpuUsagePercent { get; init; }
    public double? VramUsagePercent { get; init; }
    public double? CpuTemperatureC { get; init; }
    public double? GpuTemperatureC { get; init; }
}

/// <summary>Produces telemetry samples for the current platform.</summary>
public interface ISystemTelemetrySource
{
    /// <summary>
    /// Takes one measurement. CPU usage is computed from the activity since the
    /// previous call, so samples should be spaced out (≥ 500 ms apart).
    /// </summary>
    TelemetrySample Sample();
}
