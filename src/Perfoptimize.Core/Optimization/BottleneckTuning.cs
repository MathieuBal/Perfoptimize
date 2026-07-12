namespace Perfoptimize.Core.Optimization;

/// <summary>
/// Which verdicts may drive an automatic tuning experiment. This gate exists in
/// Core so every entry point (CLI, future UI, fallbacks) shares the same rule:
/// a protection present in the advisor must also hold wherever files get written.
/// </summary>
public static class BottleneckTuning
{
    /// <summary>
    /// True only for verdicts where changing a game setting can plausibly help.
    /// External load, hardware/power limits, "nothing saturated" and unknown
    /// verdicts must never trigger a config write.
    /// </summary>
    public static bool IsTunable(BottleneckType type) => type switch
    {
        BottleneckType.Gpu or
        BottleneckType.Cpu or
        BottleneckType.Vram or
        BottleneckType.Ram or
        BottleneckType.Thermal => true,
        _ => false,
    };

    /// <summary>Why a verdict cannot be tuned, for user-facing messages.</summary>
    public static string RefusalReason(BottleneckType type) => type switch
    {
        BottleneckType.ExternalGpuLoad =>
            "the GPU load belongs to other applications — close them and re-run the analysis " +
            "instead of changing game settings.",
        BottleneckType.ExternalCpuLoad =>
            "the CPU load belongs to another program — close it and re-run the analysis " +
            "instead of changing game settings.",
        BottleneckType.GpuHardwareLimit =>
            "the GPU is held back by a board or power limit — check cables, power limits and " +
            "cooling; no game setting will change this.",
        BottleneckType.None =>
            "nothing is saturated — check V-Sync and frame caps; there is no bottleneck to tune away.",
        _ =>
            "the bottleneck could not be identified — run \"perfoptimize monitor\" while playing " +
            "to get a usable diagnosis first.",
    };
}
