using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Optimization;

public sealed class Recommendation
{
    public string Title { get; init; } = "";
    public string Reason { get; init; } = "";
}

/// <summary>
/// Turns a bottleneck analysis into concrete, prioritized setting suggestions.
/// With a game profile, it ranks that game's settings by efficiency
/// (performance gained per unit of visual quality lost); without one, it
/// falls back to advice that applies to any game.
/// </summary>
public static class RecommendationEngine
{
    private const int MaxProfileSuggestions = 3;

    public static IReadOnlyList<Recommendation> Recommend(
        BottleneckAnalysis analysis, GameProfile? game = null)
    {
        var recommendations = new List<Recommendation>();
        recommendations.AddRange(FromGameProfile(analysis.Type, game));
        recommendations.AddRange(GenericAdvice(analysis.Type));
        return recommendations;
    }

    private static IEnumerable<Recommendation> FromGameProfile(BottleneckType bottleneck, GameProfile? game)
    {
        if (game is null)
            yield break;

        var relevantImpacts = RelevantImpacts(bottleneck);
        var candidates = game.Settings
            .Where(s => relevantImpacts.Contains(s.Impact) && s.PerformanceGain > 0)
            .OrderByDescending(s => s.PerformanceGain - s.QualityLoss)
            .ThenByDescending(s => s.PerformanceGain)
            .Take(MaxProfileSuggestions);

        foreach (var setting in candidates)
        {
            yield return new Recommendation
            {
                Title = $"{game.Name}: lower \"{setting.Name}\" by one step",
                Reason = $"Typical gain {setting.PerformanceGain}/5 for a visual cost of {setting.QualityLoss}/5.",
            };
        }
    }

    private static HashSet<SettingImpact> RelevantImpacts(BottleneckType bottleneck) => bottleneck switch
    {
        BottleneckType.Gpu => [SettingImpact.Gpu, SettingImpact.Mixed],
        BottleneckType.Cpu => [SettingImpact.Cpu, SettingImpact.Mixed],
        BottleneckType.Vram => [SettingImpact.Vram],
        BottleneckType.Ram => [SettingImpact.Ram, SettingImpact.Vram],
        // Thermal load is almost always rendering load: shed GPU work first.
        BottleneckType.Thermal => [SettingImpact.Gpu],
        _ => [],
    };

    private static IEnumerable<Recommendation> GenericAdvice(BottleneckType bottleneck)
    {
        switch (bottleneck)
        {
            case BottleneckType.Gpu:
                yield return new Recommendation
                {
                    Title = "Enable upscaling (DLSS / FSR / XeSS) in Quality mode",
                    Reason = "Renders at a lower internal resolution for a large GPU-side gain with minimal visual cost.",
                };
                yield return new Recommendation
                {
                    Title = "Disable ray tracing if enabled",
                    Reason = "Ray tracing is usually the single most expensive GPU setting.",
                };
                break;

            case BottleneckType.Cpu:
                yield return new Recommendation
                {
                    Title = "Lower CPU-heavy settings: crowd density, draw distance, physics",
                    Reason = "These reduce simulation and draw-call work; resolution changes will not help a CPU limit.",
                };
                yield return new Recommendation
                {
                    Title = "Close background applications (browser, launchers, overlays)",
                    Reason = "Frees CPU cores for the game's main and worker threads.",
                };
                break;

            case BottleneckType.Vram:
                yield return new Recommendation
                {
                    Title = "Lower texture quality by one step",
                    Reason = "Textures dominate VRAM; one step usually fits the budget with little visible change.",
                };
                break;

            case BottleneckType.Ram:
                yield return new Recommendation
                {
                    Title = "Close memory-hungry applications before playing",
                    Reason = "Browsers and chat apps commonly hold several GiB that the game needs.",
                };
                break;

            case BottleneckType.Thermal:
                yield return new Recommendation
                {
                    Title = "Cap the frame rate slightly below your average FPS",
                    Reason = "Reduces sustained load and temperature with no perceived fluidity loss.",
                };
                yield return new Recommendation
                {
                    Title = "Check case airflow and dust filters",
                    Reason = "A throttling component loses more performance than any setting can recover.",
                };
                break;

            case BottleneckType.None:
                yield return new Recommendation
                {
                    Title = "Check V-Sync and frame-cap settings",
                    Reason = "Nothing is saturated: a configured limit is probably deciding your FPS.",
                };
                break;

            case BottleneckType.Unknown:
                yield return new Recommendation
                {
                    Title = "Run a longer monitoring session while playing",
                    Reason = "More samples during real gameplay are needed to identify the limiting component.",
                };
                break;
        }
    }
}
