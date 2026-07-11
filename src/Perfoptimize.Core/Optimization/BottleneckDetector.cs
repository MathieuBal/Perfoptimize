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

        if (summary.PeakGpuTemperatureC >= GpuThermalLimitC)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Thermal,
                Explanation = $"The GPU reached {summary.PeakGpuTemperatureC:0}°C and is likely throttling. " +
                              "Improve airflow or cap the frame rate before touching quality settings.",
            };
        }

        if (summary.PeakCpuTemperatureC >= CpuThermalLimitC)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Thermal,
                Explanation = $"The CPU reached {summary.PeakCpuTemperatureC:0}°C and is likely throttling. " +
                              "Check cooling before touching quality settings.",
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
