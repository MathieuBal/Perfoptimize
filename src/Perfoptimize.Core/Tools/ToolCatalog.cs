namespace Perfoptimize.Core.Tools;

/// <summary>A performance tool the assistant can help the user obtain.</summary>
public sealed record PerfTool(
    string Id,
    string Name,
    string Purpose,
    string Url,
    /// <summary>True when Perfoptimize can download/install it itself (no browser trip).</summary>
    bool HasBuiltInInstaller);

/// <summary>
/// The curated set of tools worth having alongside Perfoptimize. PresentMon is
/// installed in-app; the others are linked to their OFFICIAL download page (we
/// open the page rather than silently fetching third-party binaries). Keeping
/// this as data means new tools are one entry away.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<PerfTool> All { get; } =
    [
        new("presentmon", "PresentMon",
            "Capture les vrais FPS et frametimes (1% low, saccades) — la base d'un diagnostic mesuré.",
            "https://github.com/GameTechDev/PresentMon/releases", HasBuiltInInstaller: true),
        new("rtss", "RivaTuner Statistics Server (RTSS)",
            "Limiteur de FPS précis et overlay — idéal pour lisser le frame pacing.",
            "https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/", HasBuiltInInstaller: false),
        new("capframex", "CapFrameX",
            "Capture et compare les frametimes avant/après un réglage, avec statistiques détaillées.",
            "https://www.capframex.com/", HasBuiltInInstaller: false),
        new("ddu", "Display Driver Uninstaller (DDU)",
            "Nettoie complètement un pilote GPU avant réinstallation — contre les instabilités et crashs.",
            "https://www.guru3d.com/download/display-driver-uninstaller-download/", HasBuiltInInstaller: false),
        new("hwinfo", "HWiNFO",
            "Capteurs matériels détaillés (températures, horloges, throttling) pour confirmer une limite hardware.",
            "https://www.hwinfo.com/download/", HasBuiltInInstaller: false),
    ];

    public static PerfTool? Find(string id) =>
        All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
