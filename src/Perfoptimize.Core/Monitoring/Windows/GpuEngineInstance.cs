using System.Text.RegularExpressions;

namespace Perfoptimize.Core.Monitoring.Windows;

/// <summary>
/// Decoded name of a "GPU Engine" performance-counter instance, e.g.
/// "pid_1234_luid_0x00000000_0x0000C852_phys_0_engtype_3D".
/// The PID ties the reading to a process and the LUID to a specific adapter —
/// without both, a laptop iGPU, a browser or an overlay pollutes the numbers.
/// </summary>
public readonly record struct GpuEngineInstance(int Pid, string Luid, string EngineType);

public static partial class GpuEngineInstanceParser
{
    /// <summary>Parses a counter instance name; null when the shape is unknown.</summary>
    public static GpuEngineInstance? Parse(string instanceName)
    {
        var match = InstanceRegex().Match(instanceName);
        if (!match.Success)
            return null;
        return new GpuEngineInstance(
            int.Parse(match.Groups["pid"].Value),
            match.Groups["luid"].Value,
            match.Groups["engine"].Value);
    }

    /// <summary>Rendering engines: what "the game is using the GPU" actually means.</summary>
    public static bool IsRenderEngine(string engineType) =>
        engineType.Equals("3D", StringComparison.OrdinalIgnoreCase) ||
        engineType.Equals("Graphics", StringComparison.OrdinalIgnoreCase) ||
        engineType.StartsWith("Graphics_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Aggregates parsed engine readings into what the advisor needs:
    /// the game's own GPU usage, the total 3D load of the adapter the game
    /// runs on (or the busiest adapter when no game PID is known), and that
    /// adapter's LUID so VRAM counters can be scoped to the same card.
    /// </summary>
    public static (double? GameGpuPercent, double? AdapterGpuPercent, string? AdapterLuid) Aggregate(
        IReadOnlyList<(GpuEngineInstance Instance, double UtilizationPercent)> readings,
        int? gameProcessId)
    {
        var render = readings.Where(r => IsRenderEngine(r.Instance.EngineType)).ToList();
        if (render.Count == 0)
            return (null, null, null);

        double? gamePercent = null;
        string? gameAdapterLuid = null;
        if (gameProcessId is { } pid)
        {
            var gameReadings = render.Where(r => r.Instance.Pid == pid).ToList();
            if (gameReadings.Count > 0)
            {
                gamePercent = Math.Clamp(gameReadings.Sum(r => r.UtilizationPercent), 0, 100);
                // The adapter the game submits work to — not a heuristic guess.
                gameAdapterLuid = gameReadings
                    .GroupBy(r => r.Instance.Luid)
                    .OrderByDescending(g => g.Sum(r => r.UtilizationPercent))
                    .First().Key;
            }
        }

        var byAdapter = render
            .GroupBy(r => r.Instance.Luid)
            .ToDictionary(g => g.Key, g => Math.Clamp(g.Sum(r => r.UtilizationPercent), 0, 100));

        var adapterLuid = gameAdapterLuid ??
                          byAdapter.OrderByDescending(a => a.Value).First().Key;
        return (gamePercent, byAdapter.GetValueOrDefault(adapterLuid), adapterLuid);
    }

    [GeneratedRegex(
        @"^pid_(?<pid>\d+)_luid_(?<luid>0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_\d+_engtype_(?<engine>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex InstanceRegex();
}
