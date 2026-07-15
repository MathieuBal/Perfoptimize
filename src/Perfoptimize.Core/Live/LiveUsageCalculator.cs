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
    private const double HotGpuC = 87;
    private const double HotCpuC = 95;
    // % of CPU time in DPC or interrupts above which driver latency is likely
    // to be felt as micro-stutter (LatencyMon territory, at a coarse level).
    private const double HighDpcPercent = 8;

    public static LiveSnapshot Compute(
        IReadOnlyList<ProcessTimes> before,
        IReadOnlyList<ProcessTimes> after,
        TimeSpan elapsed,
        int cpuCount,
        long? ramTotalBytes,
        long? ramUsedBytes,
        int? gamePid,
        string? gameName,
        double? gpuPercent = null,
        double? vramPercent = null,
        double? cpuTemperatureC = null,
        double? gpuTemperatureC = null,
        bool? gpuThrottling = null,
        double? dpcPercent = null,
        double? interruptPercent = null,
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
            GpuPercent = gpuPercent,
            VramPercent = vramPercent,
            CpuTemperatureC = cpuTemperatureC,
            GpuTemperatureC = gpuTemperatureC,
            GpuThrottling = gpuThrottling,
            DpcPercent = dpcPercent,
            InterruptPercent = interruptPercent,
            ProcessCount = after.Count,
            GamePid = gamePid,
            GameName = gameName,
            TopByCpu = topByCpu,
            TopByMemory = topByMemory,
            Alerts = BuildAlerts(usages, systemCpu, ramPercent, gameName,
                cpuTemperatureC, gpuTemperatureC, gpuThrottling, dpcPercent, interruptPercent),
        };
    }

    private static IReadOnlyList<LiveAlert> BuildAlerts(
        IReadOnlyList<ProcessUsage> usages, double systemCpu, double? ramPercent, string? gameName,
        double? cpuTemperatureC, double? gpuTemperatureC, bool? gpuThrottling,
        double? dpcPercent, double? interruptPercent)
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

        // Thermals: a throttling or very hot GPU directly costs FPS and causes
        // micro-freezes; a very hot CPU throttles and can trigger shutdowns.
        if (gpuThrottling == true || gpuTemperatureC >= HotGpuC)
        {
            alerts.Add(new LiveAlert
            {
                Kind = LiveAlertKind.Thermal,
                Level = "bad",
                Title = gpuThrottling == true
                    ? $"GPU en throttling thermique ({gpuTemperatureC:0} °C)"
                    : $"GPU très chaud ({gpuTemperatureC:0} °C)",
                Detail = "Au-delà du seuil, le GPU baisse ses fréquences pour se protéger — perte de FPS " +
                         "et micro-freezes. Dépoussière ventilateurs et radiateur, améliore le flux d'air " +
                         "du boîtier, et envisage de refaire la pâte thermique si les températures restent hautes.",
            });
        }
        if (cpuTemperatureC >= HotCpuC)
        {
            alerts.Add(new LiveAlert
            {
                Kind = LiveAlertKind.Thermal,
                Level = "bad",
                Title = $"CPU très chaud ({cpuTemperatureC:0} °C)",
                Detail = "Le processeur approche de sa limite et va réduire ses fréquences (throttling), " +
                         "voire déclencher un arrêt de protection. Vérifie le ventirad et la pâte thermique, " +
                         "et nettoie les entrées d'air.",
            });
        }

        // Driver latency: high DPC/ISR time is the classic cause of micro-freezes
        // with no CPU/GPU saturation — the signal LatencyMon is built around.
        var latency = Math.Max(dpcPercent ?? 0, interruptPercent ?? 0);
        if (latency >= HighDpcPercent)
        {
            alerts.Add(new LiveAlert
            {
                Kind = LiveAlertKind.DriverLatency,
                Level = "warn",
                Title = $"Latence pilote élevée (DPC/ISR {latency:0} %)",
                Detail = "Beaucoup de temps passé dans les routines de pilotes — souvent la cause des " +
                         "micro-freezes sans que le CPU ou le GPU ne saturent. Mets à jour les pilotes " +
                         "chipset, GPU, réseau (Killer/Realtek) et audio ; l'outil LatencyMon (catalogue) " +
                         "identifie précisément le pilote fautif.",
            });
        }

        return alerts;
    }
}
