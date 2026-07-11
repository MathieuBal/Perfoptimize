namespace Perfoptimize.Core.Monitoring;

/// <summary>
/// One system-wide measurement. Every value is nullable: sensors differ per
/// machine, and a missing reading must be distinguishable from a zero.
/// </summary>
public sealed class TelemetrySample
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>Whole-machine CPU usage across all cores.</summary>
    public double? CpuUsagePercent { get; init; }
    /// <summary>
    /// Usage of the busiest logical core. A game pinned to one thread shows
    /// ~100% here while the machine-wide average stays low — the classic
    /// single-thread bottleneck that a total-only reading misses.
    /// </summary>
    public double? MaxCoreUsagePercent { get; init; }
    /// <summary>CPU used by the game process, as a share of the whole machine (0-100).</summary>
    public double? GameCpuUsagePercent { get; init; }

    public double? RamUsagePercent { get; init; }

    /// <summary>GPU 3D-engine usage of the adapter the game actually runs on.</summary>
    public double? GpuUsagePercent { get; init; }
    /// <summary>GPU 3D-engine usage attributed to the game process only (Windows).</summary>
    public double? GameGpuUsagePercent { get; init; }
    public double? VramUsagePercent { get; init; }

    public double? CpuTemperatureC { get; init; }
    public double? GpuTemperatureC { get; init; }
    /// <summary>Current GPU core clock; a sustained drop under load suggests throttling.</summary>
    public double? GpuCoreClockMhz { get; init; }
    /// <summary>True when the driver itself reports an active thermal slowdown.</summary>
    public bool? GpuThermalThrottleActive { get; init; }
}

/// <summary>Produces telemetry samples for the current platform.</summary>
public interface ISystemTelemetrySource
{
    /// <summary>
    /// Takes one measurement. CPU usage is computed from the activity since the
    /// previous call, so samples should be spaced out (≥ 500 ms apart).
    /// </summary>
    /// <param name="gameProcessId">
    /// PID of the detected game, enabling per-process CPU/GPU attribution;
    /// null monitors the system as a whole.
    /// </param>
    TelemetrySample Sample(int? gameProcessId = null);
}
