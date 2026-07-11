using Perfoptimize.Core.Monitoring;

namespace Perfoptimize.Core.Optimization;

public enum BottleneckType
{
    /// <summary>Not enough data to conclude (too few samples or missing sensors).</summary>
    Unknown,
    None,
    Cpu,
    Gpu,
    /// <summary>The GPU is saturated, but mostly by processes other than the game.</summary>
    ExternalGpuLoad,
    Ram,
    Vram,
    Thermal,
}

/// <summary>How solid the verdict is — a suspicion must never read like a proof.</summary>
public enum BottleneckConfidence
{
    Confirmed,
    Suspected,
}

public sealed class BottleneckAnalysis
{
    public BottleneckType Type { get; init; }
    public BottleneckConfidence Confidence { get; init; } = BottleneckConfidence.Confirmed;
    public string Explanation { get; init; } = "";
}

/// <summary>
/// Interprets an aggregated monitoring session to find the limiting component.
/// The rules follow severity order: an overheating part or exhausted memory
/// causes stutters that dominate any raw CPU/GPU utilization reading.
/// When per-process attribution is available, verdicts check that the load
/// actually belongs to the game before blaming it.
/// </summary>
public static class BottleneckDetector
{
    // Thresholds are deliberately conservative; they will be tuned with field data.
    private const double GpuThermalLimitC = 87;
    private const double CpuThermalLimitC = 95;
    private const double VramPressurePercent = 92;
    private const double RamPressurePercent = 90;
    private const double GpuBoundPercent = 92;
    private const double GameGpuBoundPercent = 90;
    private const double ExternalGpuGameSharePercent = 70;
    private const double CpuBoundPercent = 85;
    private const double SingleCoreBoundPercent = 90;
    private const double GpuIdleWhileCpuBoundPercent = 80;

    /// <param name="logicalCores">
    /// Logical core count of the machine, used to judge whether the game's CPU
    /// share is plausibly the saturated core; null when unknown.
    /// </param>
    public static BottleneckAnalysis Analyze(PerformanceSummary summary, int? logicalCores = null)
    {
        if (summary.SampleCount < 3)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Unknown,
                Confidence = BottleneckConfidence.Suspected,
                Explanation = "Not enough samples were collected to identify a bottleneck.",
            };
        }

        if (Thermal(summary) is { } thermal)
            return thermal;

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

        if (Gpu(summary) is { } gpu)
            return gpu;

        if (Cpu(summary, logicalCores) is { } cpu)
            return cpu;

        if (summary.AverageCpuUsagePercent is null && summary.AverageGpuUsagePercent is null)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Unknown,
                Confidence = BottleneckConfidence.Suspected,
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

    private static BottleneckAnalysis? Thermal(PerformanceSummary summary)
    {
        // A slowdown reported by the driver itself is the only real proof.
        if (summary.GpuThermalThrottleObserved)
        {
            var clock = summary.AverageGpuCoreClockMhz is { } mhz ? $" (average clock {mhz:0} MHz)" : "";
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Thermal,
                Confidence = BottleneckConfidence.Confirmed,
                Explanation = "The GPU driver reported an active thermal slowdown during the session" +
                              $"{clock} — this is confirmed throttling, not a guess. Improve airflow " +
                              "or cap the frame rate before touching quality settings.",
            };
        }

        // HW slowdown / power brake: real, but the cause may be power delivery
        // rather than heat — never label it "thermal confirmed".
        if (summary.GpuHardwareSlowdownObserved)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Thermal,
                Confidence = BottleneckConfidence.Suspected,
                Explanation = "The GPU driver reported a hardware slowdown (HW slowdown or power brake). " +
                              "The card is being held back, but the cause may be power delivery or a " +
                              "board limit rather than temperature — check PSU cables and power limits " +
                              "as well as cooling.",
            };
        }

        // A high temperature alone proves nothing: limits vary per model and
        // laptop profile. Report it as a suspicion, clearly labeled as such.
        if (summary.PeakGpuTemperatureC >= GpuThermalLimitC)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Thermal,
                Confidence = BottleneckConfidence.Suspected,
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
                Confidence = BottleneckConfidence.Suspected,
                Explanation = $"The CPU reached {summary.PeakCpuTemperatureC:0}°C. That is hot enough to " +
                              "suspect throttling, but the sensor may not even be the CPU's own — " +
                              "verify frequencies under load before concluding.",
            };
        }

        return null;
    }

    private static BottleneckAnalysis? Gpu(PerformanceSummary summary)
    {
        var adapter = summary.AverageGpuUsagePercent;
        var game = summary.AverageGameGpuUsagePercent;

        // Best case: per-process attribution proves the game itself is the load.
        if (game >= GameGpuBoundPercent)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Gpu,
                Confidence = BottleneckConfidence.Confirmed,
                Explanation = $"The game's own rendering averaged {game:0}% of the GPU — the game is " +
                              "genuinely GPU-bound (the healthy case; lower GPU-heavy settings to gain FPS).",
            };
        }

        // Careful with nullables: "null < threshold" is false, so test explicitly.
        if (adapter is null || adapter < GpuBoundPercent)
            return null;

        // The adapter is saturated but the game only accounts for part of it:
        // something else (browser, OBS, another app) is eating the same card.
        if (game is { } gameShare && gameShare < ExternalGpuGameSharePercent)
        {
            return new BottleneckAnalysis
            {
                Type = BottleneckType.ExternalGpuLoad,
                Confidence = BottleneckConfidence.Confirmed,
                Explanation = $"The GPU averaged {adapter:0}% but the game only accounts for {gameShare:0}% — " +
                              "other applications (browser, capture software, overlays) are competing for " +
                              "the same card. Close them before lowering any quality setting.",
            };
        }

        // Saturated adapter, game share high or unknown.
        var confidence = game is null ? BottleneckConfidence.Suspected : BottleneckConfidence.Confirmed;
        var attribution = game is { } g
            ? $" (game share {g:0}%)"
            : " — per-process attribution was unavailable, so other GPU applications could contribute";
        return new BottleneckAnalysis
        {
            Type = BottleneckType.Gpu,
            Confidence = confidence,
            Explanation = $"The GPU averaged {adapter:0}% utilization{attribution}. Rendering load is " +
                          "the limiting factor; lower GPU-heavy settings to gain FPS.",
        };
    }

    private static BottleneckAnalysis? Cpu(PerformanceSummary summary, int? logicalCores)
    {
        var gpuHasHeadroom = summary.AverageGpuUsagePercent is null ||
                             summary.AverageGpuUsagePercent < GpuIdleWhileCpuBoundPercent;
        if (!gpuHasHeadroom)
            return null;

        if (summary.AverageCpuUsagePercent >= CpuBoundPercent)
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
        if (summary.AverageMaxCoreUsagePercent >= SingleCoreBoundPercent)
        {
            var total = summary.AverageCpuUsagePercent is { } t ? $" while total CPU shows only {t:0}%" : "";

            // Sanity check: is the game even using a core's worth of CPU? If its
            // machine-wide share is far below one core, the pinned core most
            // likely belongs to another program.
            if (summary.AverageGameCpuUsagePercent is { } gameShare && logicalCores is > 0)
            {
                var coresWorth = gameShare * logicalCores.Value / 100.0;
                if (coresWorth < 0.6)
                {
                    return new BottleneckAnalysis
                    {
                        Type = BottleneckType.Cpu,
                        Confidence = BottleneckConfidence.Suspected,
                        Explanation = $"One CPU core averaged {summary.AverageMaxCoreUsagePercent:0}%{total}, " +
                                      $"but the game process only uses ~{coresWorth:0.0} core's worth of CPU — " +
                                      "the saturated core probably belongs to another program (antivirus scan, " +
                                      "background task). Check running processes before changing game settings.",
                    };
                }
            }

            var game = summary.AverageGameCpuUsagePercent is { } g
                ? $" The game process alone accounts for {g:0}% of the machine."
                : " Per-process CPU attribution was unavailable, so confirm the load belongs to the game.";
            return new BottleneckAnalysis
            {
                Type = BottleneckType.Cpu,
                Confidence = summary.AverageGameCpuUsagePercent is null
                    ? BottleneckConfidence.Suspected
                    : BottleneckConfidence.Confirmed,
                Explanation = $"One CPU core averaged {summary.AverageMaxCoreUsagePercent:0}%{total} — " +
                              $"the game is limited by a single thread.{game} Lowering resolution " +
                              "will not help; reduce simulation-heavy settings (crowds, draw distance).",
            };
        }

        return null;
    }
}
