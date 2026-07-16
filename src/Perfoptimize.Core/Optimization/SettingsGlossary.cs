using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Optimization;

/// <summary>A rendering API (renderer) option the player can pick in a game.</summary>
public sealed record RendererOption(string Id, string Name, string Summary, string BestWhen);

/// <summary>The tailored renderer recommendation for the current situation.</summary>
public sealed record RendererAdvice(string OptionId, string Text);

/// <summary>
/// A plain-language explanation of one graphics setting: what it actually
/// changes on screen, which component pays for it, and how to think about it.
/// Aliases let it be matched to a game's own setting names.
/// </summary>
public sealed class GlossaryEntry
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>What the setting visibly changes, in everyday words.</summary>
    public string WhatItChanges { get; init; } = "";
    /// <summary>Which resource it mostly costs.</summary>
    public SettingImpact Cost { get; init; }
    /// <summary>How to decide — when to lower it and what you give up.</summary>
    public string Guidance { get; init; } = "";
    /// <summary>Lower-case keywords used to match a game's setting name.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

/// <summary>
/// A small, game-agnostic knowledge base that answers "what does this setting
/// actually do?" — the gap the profiles' numbers don't fill. Includes the
/// DirectX 11 / DirectX 12 / Vulkan choice, whose best answer depends on the
/// measured bottleneck. Pure and unit-tested.
/// </summary>
public static class SettingsGlossary
{
    public static readonly IReadOnlyList<RendererOption> Renderers =
    [
        new("dx11", "DirectX 11",
            "API mature et très stable, souvent moins de stutter de compilation de shaders.",
            "Si DirectX 12 / Vulkan plantent ou saccadent chez toi, ou sur une config plus ancienne."),
        new("dx12", "DirectX 12",
            "API moderne : répartit mieux le travail sur plusieurs cœurs et gère mieux le ray tracing.",
            "CPU multi-cœurs et/ou ray tracing — au prix d'un possible stutter au tout premier passage."),
        new("vulkan", "Vulkan",
            "API moderne multiplateforme, souvent la plus fluide sur cartes AMD, Linux et Steam Deck.",
            "Cartes AMD, Linux / Steam Deck, ou quand DirectX saccade et que le jeu propose Vulkan."),
    ];

    /// <summary>Picks a renderer recommendation tailored to the measured bottleneck.</summary>
    public static RendererAdvice RecommendRenderer(BottleneckType bottleneck) => bottleneck switch
    {
        BottleneckType.Cpu or BottleneckType.ExternalCpuLoad => new RendererAdvice("dx12",
            "Ton analyse pointe un goulot côté processeur : privilégie DirectX 12 ou Vulkan, qui " +
            "répartissent le travail sur plusieurs cœurs au lieu d'en saturer un seul. Teste les deux et " +
            "mesure — Perfoptimize compare l'avant/après."),
        BottleneckType.Gpu or BottleneckType.ExternalGpuLoad => new RendererAdvice("dx12",
            "Tu es limité par la carte graphique : l'API ne change presque pas la charge GPU. Garde la plus " +
            "stable (souvent DirectX 12 ou Vulkan si tu utilises le ray tracing, DirectX 11 en cas de " +
            "plantages). Le vrai levier ici, ce sont les réglages GPU ci-dessous."),
        _ => new RendererAdvice("dx12",
            "Règle simple : essaie DirectX 12 (ou Vulkan si le jeu le propose). Reviens à DirectX 11 si tu as " +
            "des plantages ou un gros stutter de compilation de shaders au lancement. Puis mesure l'écart."),
    };

    public static readonly IReadOnlyList<GlossaryEntry> Entries =
    [
        new()
        {
            Id = "resolution", Title = "Résolution / mise à l'échelle", Cost = SettingImpact.Gpu,
            WhatItChanges = "Le nombre de pixels calculés. C'est le réglage qui pèse le plus sur le GPU.",
            Guidance = "Plutôt que baisser la résolution (image floue), active un upscaler (DLSS/FSR/XeSS) en " +
                       "mode Qualité : le jeu rend en interne plus bas puis reconstruit — gros gain, quasi invisible.",
            Aliases = ["resolution", "résolution", "render scale", "mise à l'échelle", "upscal", "dlss", "fsr", "xess"],
        },
        new()
        {
            Id = "antialiasing", Title = "Anticrénelage (Anti-Aliasing)", Cost = SettingImpact.Gpu,
            WhatItChanges = "Lisse les bords en escalier des objets.",
            Guidance = "TAA coûte peu et suffit dans la plupart des cas. MSAA/SSAA sont beaucoup plus lourds — " +
                       "à réserver aux vieux jeux où le GPU s'ennuie.",
            Aliases = ["anti-alias", "antialias", "anticrénel", "aa ", "taa", "msaa", "smaa", "fxaa"],
        },
        new()
        {
            Id = "shadows", Title = "Qualité des ombres", Cost = SettingImpact.Gpu,
            WhatItChanges = "La netteté, la portée et la douceur des ombres.",
            Guidance = "Coût GPU notable. Au-delà de « Élevé », le gain visuel est faible : passer d'Ultra à Élevé " +
                       "rend souvent quelques % de FPS sans différence visible en jeu.",
            Aliases = ["shadow", "ombre"],
        },
        new()
        {
            Id = "raytracing", Title = "Ray Tracing / Path Tracing", Cost = SettingImpact.Gpu,
            WhatItChanges = "Calcule l'éclairage, les reflets et les ombres de façon physiquement réaliste.",
            Guidance = "De loin le plus lourd pour le GPU. Le désactiver (ou baisser d'un cran) donne le plus gros " +
                       "gain de FPS. À réserver aux cartes puissantes, idéalement avec un upscaler.",
            Aliases = ["ray tracing", "raytracing", "path tracing", "rtx", "lumen", " rt "],
        },
        new()
        {
            Id = "ssr", Title = "Réflexions en espace écran (SSR)", Cost = SettingImpact.Gpu,
            WhatItChanges = "Les reflets sur les sols mouillés, vitres et flaques.",
            Guidance = "Les modes Ultra / Psycho coûtent énormément pour une différence à peine perceptible en " +
                       "mouvement. « Élevé » est le bon compromis.",
            Aliases = ["screen space reflection", "réflexions", "reflections", "ssr", "reflets"],
        },
        new()
        {
            Id = "textures", Title = "Qualité des textures", Cost = SettingImpact.Vram,
            WhatItChanges = "La netteté des surfaces (murs, sol, vêtements).",
            Guidance = "Coûte de la VRAM, presque pas de FPS tant que la mémoire vidéo suffit. Garde-la haute si ta " +
                       "VRAM tient ; ne la baisse que si la VRAM sature (source de gros à-coups).",
            Aliases = ["texture"],
        },
        new()
        {
            Id = "volumetric", Title = "Brouillard / éclairage volumétrique", Cost = SettingImpact.Gpu,
            WhatItChanges = "La qualité de la brume, des rayons de lumière (« god rays ») et des nuages.",
            Guidance = "Coût GPU réel pour un gain d'ambiance discret. Moyen/Élevé suffit ; Ultra est rarement " +
                       "rentable.",
            Aliases = ["volumetric", "volumétrique", "fog", "brouillard", "god ray"],
        },
        new()
        {
            Id = "lod", Title = "Distance d'affichage / niveau de détail (LOD)", Cost = SettingImpact.Mixed,
            WhatItChanges = "À quelle distance les détails, objets et végétation apparaissent.",
            Guidance = "Charge à la fois le GPU et le CPU (plus d'objets à dessiner). Baisser d'un cran réduit le " +
                       "« popping » lointain visible mais soulage bien les deux dans les grands mondes ouverts.",
            Aliases = ["draw distance", "distance", "lod", "détail des objets", "view distance", "foliage", "végétation", "vegetation"],
        },
        new()
        {
            Id = "crowd", Title = "Densité de foule / PNJ", Cost = SettingImpact.Cpu,
            WhatItChanges = "Le nombre de personnages et de passants affichés.",
            Guidance = "Charge surtout le CPU : dans les zones denses (villes), une foule élevée provoque des à-coups " +
                       "sur les processeurs à cœurs limités. Moyenne lisse l'expérience.",
            Aliases = ["crowd", "foule", "npc", "pnj", "population"],
        },
        new()
        {
            Id = "ao", Title = "Occlusion ambiante (AO / SSAO / HBAO)", Cost = SettingImpact.Gpu,
            WhatItChanges = "Les ombres douces de contact dans les recoins et sous les objets.",
            Guidance = "Coût GPU modéré, apporte de la profondeur. Les variantes « HBAO+/GTAO » sont plus jolies mais " +
                       "un peu plus lourdes que le SSAO de base.",
            Aliases = ["ambient occlusion", "occlusion", "ssao", "hbao", "gtao"],
        },
        new()
        {
            Id = "vsync", Title = "Synchronisation verticale (V-Sync)", Cost = SettingImpact.Gpu,
            WhatItChanges = "Empêche le déchirement de l'image (« tearing ») en calant les images sur l'écran.",
            Guidance = "Ajoute de la latence. Si tu as un écran G-Sync / FreeSync, laisse V-Sync désactivé et active " +
                       "plutôt la synchro adaptative, ou limite les FPS juste sous la fréquence de l'écran.",
            Aliases = ["vsync", "v-sync", "synchronisation verticale", "vertical sync"],
        },
        new()
        {
            Id = "framecap", Title = "Limite de FPS (frame cap)", Cost = SettingImpact.Mixed,
            WhatItChanges = "Plafonne le nombre d'images par seconde.",
            Guidance = "Un frametime plus régulier = moins d'à-coups, moins de chaleur et de bruit. Utile pour rester " +
                       "sous la fréquence de ton écran et stabiliser la fluidité, surtout avec G-Sync/FreeSync.",
            Aliases = ["frame cap", "fps limit", "limite de fps", "frame limit", "framerate cap"],
        },
        new()
        {
            Id = "postfx", Title = "Post-traitement (flou de mouvement, profondeur de champ)", Cost = SettingImpact.Gpu,
            WhatItChanges = "Des effets d'image : flou de mouvement, profondeur de champ, aberration, grain.",
            Guidance = "Surtout une affaire de goût, coût faible à modéré. Le flou de mouvement peut masquer la " +
                       "fluidité — beaucoup de joueurs le coupent pour une image plus nette et réactive.",
            Aliases = ["post", "motion blur", "flou de mouvement", "depth of field", "profondeur de champ", "bloom", "grain"],
        },
    ];

    /// <summary>Finds the glossary entry that best matches a game's setting name; null if none.</summary>
    public static GlossaryEntry? Explain(string settingName)
    {
        if (string.IsNullOrWhiteSpace(settingName))
            return null;
        var needle = " " + settingName.ToLowerInvariant() + " ";
        return Entries.FirstOrDefault(e => e.Aliases.Any(alias => needle.Contains(alias)));
    }
}
