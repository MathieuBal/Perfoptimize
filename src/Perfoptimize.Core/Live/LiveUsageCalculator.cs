namespace Perfoptimize.Core.Live;

/// <summary>
/// Turns two per-process readings into a live snapshot: CPU% is the CPU time
/// consumed between the readings, normalized by the elapsed wall time and the
/// core count. Pure and fully unit-tested — the sampler only feeds it readings.
/// </summary>
public static class LiveUsageCalculator
{
    // Attribution thresholds. A background process above HogCpuPercent is worth
    // flagging while a game runs; near-saturation of CPU or RAM is always worth it.
    private const double HogCpuPercent = 25;
    private const double HighSystemCpuPercent = 90;
    private const double HighRamPercent = 90;

    public static LiveSnapshot Compute(
        IReadOnlyList<ProcessTimes> before,
        IReadOnlyList<ProcessTimes> after,
        TimeSpan elapsed,
        int cpuCount,
        long? ramTotalBytes,
        long? ramUsedBytes,
        int? gamePid,
        string? gameName,
        int topN = 6)
    {
        var previousCpu = new Dictionary<int, TimeSpan>(before.Count);
        foreach (var b in before)
            previousCpu[b.Pid] = b.CpuTime;

        // One core fully busy for the whole window = 100% of one core; we express
        // usage against the whole machine, so divide by the core count too.
        var denominatorMs = Math.Max(1.0, elapsed.TotalMilliseconds) * Math.Max(1, cpuCount);

        var usages = new List<ProcessUsage>(after.Count);
        double totalCpu = 0;
        foreach (var a in after)
        {
            double cpu = 0;
            if (previousCpu.TryGetValue(a.Pid, out var prev))
            {
                var deltaMs = (a.CpuTime - prev).TotalMilliseconds;
                if (deltaMs > 0)
                    cpu = 100.0 * deltaMs / denominatorMs;
            }
            cpu = Math.Clamp(cpu, 0, 100);
            totalCpu += cpu;

            usages.Add(new ProcessUsage
            {
                Pid = a.Pid,
                Name = a.Name,
                CpuPercent = Math.Round(cpu, 1),
                MemoryBytes = a.WorkingSetBytes,
                MemoryPercent = ramTotalBytes is > 0
                    ? Math.Round(100.0 * a.WorkingSetBytes / ramTotalBytes.Value, 1)
                    : null,
                IsGame = gamePid is { } gp && gp == a.Pid,
            });
        }

        var systemCpu = Math.Round(Math.Clamp(totalCpu, 0, 100), 1);
        double? ramPercent = ramTotalBytes is > 0 && ramUsedBytes is { } used
            ? Math.Round(100.0 * used / ramTotalBytes.Value, 1)
            : null;

        var topByCpu = usages
            .Where(u => u.CpuPercent > 0)
            .OrderByDescending(u => u.CpuPercent)
            .Take(topN)
            .ToList();
        var topByMemory = usages
            .OrderByDescending(u => u.MemoryBytes)
            .Take(topN)
            .ToList();

        return new LiveSnapshot
        {
            SystemCpuPercent = systemCpu,
            RamTotalBytes = ramTotalBytes,
            RamUsedBytes = ramUsedBytes,
            RamUsedPercent = ramPercent,
            ProcessCount = after.Count,
            GamePid = gamePid,
            GameName = gameName,
            TopByCpu = topByCpu,
            TopByMemory = topByMemory,
            Alerts = BuildAlerts(usages, systemCpu, ramPercent, gameName),
        };
    }

    private static IReadOnlyList<LiveAlert> BuildAlerts(
        IReadOnlyList<ProcessUsage> usages, double systemCpu, double? ramPercent, string? gameName)
    {
        var alerts = new List<LiveAlert>();

        var hog = usages
            .Where(u => !u.IsGame)
            .OrderByDescending(u => u.CpuPercent)
            .FirstOrDefault();
        if (hog is not null && hog.CpuPercent >= HogCpuPercent)
        {
            alerts.Add(new LiveAlert
            {
                Kind = LiveAlertKind.ProcessHog,
                Level = "warn",
                Title = $"{hog.Name} consomme {hog.CpuPercent:0} % du CPU",
                Detail = gameName is { } game
                    ? $"Ce processus se dispute le processeur avec {game}. Le fermer peut réduire les saccades."
                    : "Processus gourmand en arrière-plan. Le fermer libère des ressources pour le jeu.",
            });
        }

        if (systemCpu >= HighSystemCpuPercent)
        {
            alerts.Add(new LiveAlert
            {
                Kind = LiveAlertKind.CpuContention,
                Level = "bad",
                Title = $"CPU quasi saturé ({systemCpu:0} %)",
                Detail = "Le processeur est proche de 100 %. Ferme les applications lourdes pour laisser " +
                         "respirer le jeu — un CPU saturé provoque des freezes.",
            });
        }

        if (ramPercent is { } rp && rp >= HighRamPercent)
        {
            alerts.Add(new LiveAlert
            {
                Kind = LiveAlertKind.MemoryPressure,
                Level = "bad",
                Title = $"Mémoire quasi pleine ({rp:0} %)",
                Detail = "La RAM sature : Windows va paginer sur le disque, source de gros à-coups. " +
                         "Ferme des applications en arrière-plan.",
            });
        }

        return alerts;
    }
}
