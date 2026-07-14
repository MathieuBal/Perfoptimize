using Perfoptimize.Core.Hardware;

namespace Perfoptimize.Core.Optimization;

public enum SystemAdviceSeverity
{
    /// <summary>Neutral observation or optional tweak.</summary>
    Info,
    /// <summary>Worth doing for a noticeable gain.</summary>
    Suggestion,
    /// <summary>Actively hurting performance or stability.</summary>
    Warning,
}

public sealed class SystemAdvice
{
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public SystemAdviceSeverity Severity { get; init; }
}

/// <summary>
/// PC-wide optimization advice derived from the hardware profile alone — the
/// things that help every game, not one. Pure and unit-tested: it takes a
/// <see cref="HardwareProfile"/> and returns findings, most severe first.
/// </summary>
public static class SystemAdvisor
{
    private const long GiB = 1024L * 1024 * 1024;
    private const double LowDiskFreeRatio = 0.10;
    private const long LowDiskFreeBytes = 20L * GiB;

    public static IReadOnlyList<SystemAdvice> Analyze(HardwareProfile profile)
    {
        var advice = new List<SystemAdvice>();

        AddMemoryAdvice(profile, advice);
        AddStorageAdvice(profile, advice);
        AddGpuAdvice(profile, advice);

        return advice
            .OrderByDescending(a => a.Severity)
            .ToList();
    }

    private static void AddMemoryAdvice(HardwareProfile profile, List<SystemAdvice> advice)
    {
        var memory = profile.Memory;

        // Single-channel memory: one stick halves memory bandwidth, which costs
        // real FPS in CPU-bound games. A concrete, high-value fix.
        if (memory.ModuleCount == 1)
        {
            advice.Add(new SystemAdvice
            {
                Category = "Mémoire",
                Severity = SystemAdviceSeverity.Warning,
                Title = "RAM en simple canal",
                Detail = "Une seule barrette détectée : la mémoire tourne en simple canal, ce qui peut " +
                         "coûter 10 à 20 % de FPS dans les jeux limités par le processeur. Ajoute une " +
                         "seconde barrette identique pour passer en double canal.",
            });
        }

        if (memory.TotalBytes > 0 && memory.TotalBytes <= 16L * GiB + GiB / 2)
        {
            advice.Add(new SystemAdvice
            {
                Category = "Mémoire",
                Severity = SystemAdviceSeverity.Suggestion,
                Title = "16 Go de RAM",
                Detail = "16 Go suffisent souvent, mais les jeux récents en monde ouvert plus un " +
                         "navigateur dépassent régulièrement cette limite (source de saccades). " +
                         "32 Go apportent une vraie marge.",
            });
        }

        // Honest, conditional XMP/EXPO hint: we know the running speed but not the
        // kit's rated speed, so the advice is phrased as a check.
        if (memory.SpeedMtPerSecond is > 0 and < 3000 && memory.ModuleCount is null or > 1)
        {
            advice.Add(new SystemAdvice
            {
                Category = "Mémoire",
                Severity = SystemAdviceSeverity.Suggestion,
                Title = "Vérifie le profil XMP / EXPO",
                Detail = $"Ta RAM tourne à {memory.SpeedMtPerSecond} MT/s. Si ton kit est certifié plus " +
                         "rapide (souvent 3200 MT/s ou plus), active le profil XMP (Intel) ou EXPO (AMD) " +
                         "dans le BIOS : c'est un gain gratuit.",
            });
        }
    }

    private static void AddStorageAdvice(HardwareProfile profile, List<SystemAdvice> advice)
    {
        foreach (var volume in profile.Volumes)
        {
            // Ignore tiny volumes: EFI/recovery partitions and OS special mounts
            // are not where games or the pagefile live, so a full one is normal.
            if (volume.TotalBytes < GiB)
                continue;
            var ratio = (double)volume.FreeBytes / volume.TotalBytes;
            if (volume.FreeBytes < LowDiskFreeBytes || ratio < LowDiskFreeRatio)
            {
                advice.Add(new SystemAdvice
                {
                    Category = "Stockage",
                    Severity = SystemAdviceSeverity.Warning,
                    Title = $"Disque {volume.MountPoint} presque plein",
                    Detail = $"Seulement {Format(volume.FreeBytes)} libres sur {volume.MountPoint}. " +
                             "Le fichier d'échange et les caches de shaders manquent de place, ce qui " +
                             "provoque des saccades — libère de l'espace ou déplace des données.",
                });
            }
        }

        var devices = profile.StorageDevices;
        if (devices.Count > 0 && devices.All(d => d.MediaType != StorageMediaType.Ssd &&
                                                  d.MediaType != StorageMediaType.Scm))
        {
            advice.Add(new SystemAdvice
            {
                Category = "Stockage",
                Severity = SystemAdviceSeverity.Suggestion,
                Title = "Aucun SSD détecté",
                Detail = "Installer tes jeux sur un SSD réduit fortement les temps de chargement et le " +
                         "stutter de streaming des textures. Un SSD NVMe est l'amélioration la plus " +
                         "sensible pour une machine encore sur disque dur.",
            });
        }
    }

    private static void AddGpuAdvice(HardwareProfile profile, List<SystemAdvice> advice)
    {
        var primary = profile.PrimaryGpu;
        if (primary is null || !primary.IsDiscrete)
        {
            advice.Add(new SystemAdvice
            {
                Category = "Carte graphique",
                Severity = SystemAdviceSeverity.Warning,
                Title = "Pas de carte graphique dédiée",
                Detail = "Aucun GPU dédié détecté : les performances en jeu seront limitées par le GPU " +
                         "intégré. Sur un portable, vérifie que le jeu tourne bien sur la carte dédiée " +
                         "et non sur l'iGPU.",
            });
        }
    }

    private static string Format(long bytes)
    {
        if (bytes >= GiB) return $"{bytes / (double)GiB:0.0} GiB";
        return $"{bytes / (1024.0 * 1024):0} MiB";
    }
}
