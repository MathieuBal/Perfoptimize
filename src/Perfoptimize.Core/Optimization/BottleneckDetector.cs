using Perfoptimize.Core.Monitoring;

namespace Perfoptimize.Core.Optimization;

public enum BottleneckType
{
    /// <summary>Not enough data to conclude (too few samples or missing sensors).</summary>
    Unknown,
    None,
    Cpu,
    Gpu,
    Ram,
    Vram,
    Thermal,
}

public sealed class BottleneckAnalysis
{
    public BottleneckType Type { get; init; }
    public string Explanation { get; init; } = "";
}

/// <summary>
/// Interprets an aggregated monitoring session to find the limiting component.
/// The rules follow severity order: an overheating part or exhausted memory
/// causes stutters that dominate any raw CPU/GPU utilization reading.
/// </summary>
public static class BottleneckDetector
{
    // Thresholds are deliberately conservative; they will be tuned with field data.
    private const double GpuThermalLimitC = 87;
    private const double CpuThermalLimitC = 95;
    private const double VramPressurePercent = 92;
    private const double RamPressurePercent = 90;
    private const double GpuBoundPercent = 92;
    private const double CpuBoundPercent = 85;
    private const double SingleCoreBoundPercent = 90;
    private const double GpuIdleWhileCpuBoundPercent = 80;

    public static BottleneckAnalysis Analyze(PerformanceSummary summary)
    {
        if (summary.SampleCount < 3)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Unknown,
                Explanation = "Not enough samples were collected to identify a bottleneck.",
            };
        }

        // Throttling confirmed by the driver beats any inference from temperature.
        if (summary.GpuThermalThrottleObserved)
        {
            var clock = summary.AverageGpuCoreClockMhz is { } mhz ? $" (average clock {mhz:0} MHz)" : "";
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Thermal,
                Explanation = "The GPU driver reported an active thermal slowdown during the session" +
                              $"{clock} — this is confirmed throttling, not a guess. Improve airflow " +
                              "or cap the frame rate before touching quality settings.",
            };
        }

        // A high temperature alone proves nothing: limits vary per model and
        // laptop profile. Report it as a suspicion, clearly labeled as such.
        if (summary.PeakGpuTemperatureC >= GpuThermalLimitC)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Thermal,
                Explanation = $"The GPU reached {summary.PeakGpuTemperatureC:0}°C. That is hot enough to " +
                              "suspect throttling, but it is NOT confirmed — the driver reported no " +
                              "slowdown (or could not be queried). Watch the core clock under load " +
                              "before concluding.",
            };
        }

        if (summary.PeakCpuTemperatureC >= CpuThermalLimitC)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Thermal,
                Explanation = $"The CPU reached {summary.PeakCpuTemperatureC:0}°C. That is hot enough to " +
                              "suspect throttling, but the sensor may not even be the CPU's own — " +
                              "verify frequencies under load before concluding.",
            };
        }

        if (summary.PeakVramUsagePercent >= VramPressurePercent)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Vram,
                Explanation = $"Video memory peaked at {summary.PeakVramUsagePercent:0}% — textures probably " +
                              "exceed the card's VRAM, which causes sudden stutters.",
            };
        }

        if (summary.PeakRamUsagePercent >= RamPressurePercent)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Ram,
                Explanation = $"System memory peaked at {summary.PeakRamUsagePercent:0}% — the OS is likely " +
                              "swapping to disk, which causes loading hitches and freezes.",
            };
        }

        if (summary.AverageGpuUsagePercent >= GpuBoundPercent)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Gpu,
                Explanation = $"The GPU averaged {summary.AverageGpuUsagePercent:0}% utilization — rendering " +
                              "load is the limiting factor (the healthy case; lower GPU-heavy settings to gain FPS).",
            };
        }

        if (summary.AverageCpuUsagePercent >= CpuBoundPercent &&
            (summary.AverageGpuUsagePercent is null ||
             summary.AverageGpuUsagePercent < GpuIdleWhileCpuBoundPercent))
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Cpu,
                Explanation = $"The CPU averaged {summary.AverageCpuUsagePercent:0}% while the GPU had headroom — " +
                              "simulation or draw-call load is the limiting factor.",
            };
        }

        // The classic trap: one core pinned at 100% while the machine-wide
        // average looks idle. Games with a dominant main/render thread live here.
        if (summary.AverageMaxCoreUsagePercent >= SingleCoreBoundPercent &&
            (summary.AverageGpuUsagePercent is null ||
             summary.AverageGpuUsagePercent < GpuIdleWhileCpuBoundPercent))
        {
            var total = summary.AverageCpuUsagePercent is { } t ? $" while total CPU shows only {t:0}%" : "";
            var game = summary.AverageGameCpuUsagePercent is { } g
                ? $" The game process alone accounts for {g:0}% of the machine."
                : "";
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Cpu,
                Explanation = $"One CPU core averaged {summary.AverageMaxCoreUsagePercent:0}%{total} — " +
                              $"the game is limited by a single thread.{game} Lowering resolution " +
                              "will not help; reduce simulation-heavy settings (crowds, draw distance).",
            };
        }

        if (summary.AverageCpuUsagePercent is null && summary.AverageGpuUsagePercent is null)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Unknown,
                Explanation = "CPU and GPU utilization could not be read on this system.",
            };
        }

        return new BottleneckAnalysis
        {
            Type = BottleneckType.None,
            Explanation = "No component is saturated — performance may be limited by a frame cap, " +
                          "V-Sync, or the game's engine itself.",
        };
    }
}
