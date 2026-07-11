using Perfoptimize.Core.Games;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Monitoring.Frametimes;

namespace Perfoptimize.Core.Optimization;

public sealed class Recommendation
{
    public string Title { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>The game setting behind this advice; null for generic advice.</summary>
    public GameSetting? Setting { get; init; }
    /// <summary>Estimated FPS gain in percent; null when unquantified.</summary>
    public double? ExpectedGainPercent { get; init; }
}

/// <summary>What the player wants to preserve when trading quality for FPS.</summary>
public enum OptimizationGoal
{
    /// <summary>Sacrifice as little visual quality as possible.</summary>
    Quality,
    Balanced,
    /// <summary>Get the most FPS, visual cost is secondary.</summary>
    Performance,
}

/// <summary>Everything the advisor may use to tailor its advice. All context is optional.</summary>
public sealed class AdviceRequest
{
    public required BottleneckAnalysis Analysis { get; init; }
    public GameProfile? Game { get; init; }
    public OptimizationGoal Goal { get; init; } = OptimizationGoal.Balanced;
    /// <summary>Hardware profile: enables GPU-aware upscaler and VRAM-aware texture advice.</summary>
    public HardwareProfile? Hardware { get; init; }
    /// <summary>Aggregated telemetry of the monitored session.</summary>
    public PerformanceSummary? Summary { get; init; }
    /// <summary>Real FPS statistics (PresentMon capture) taken during the session.</summary>
    public FrametimeStatistics? Frametimes { get; init; }
    /// <summary>The FPS the player wants to reach; defaults to 60.</summary>
    public int? TargetFps { get; init; }
}

public sealed class AdvicePlan
{
    public int TargetFps { get; init; }
    public double? MeasuredAverageFps { get; init; }
    /// <summary>FPS gain (percent) needed to reach the target; null without FPS data.</summary>
    public double? RequiredGainPercent { get; init; }
    /// <summary>Sum of the quantified gains of the recommendations below.</summary>
    public double? EstimatedPlanGainPercent { get; init; }
    public IReadOnlyList<Recommendation> Recommendations { get; init; } = [];
    /// <summary>Context the player should know (frame caps, out-of-reach targets, ...).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Turns a bottleneck analysis into concrete, prioritized suggestions. The more
/// context it receives (hardware, telemetry, real FPS, target), the more the
/// plan is sized to the actual gap instead of generic "lower everything" advice.
/// </summary>
public static class RecommendationEngine
{
    private const int DefaultTargetFps = 60;
    /// <summary>Rough % gain assumed per 0-5 gain point when a setting is unquantified.</summary>
    private const int PercentPerGainPoint = 3;
    /// <summary>VRAM kept free for the OS, overlays and capture tools (GiB).</summary>
    private const double VramOverheadGiB = 0.8;

    /// <summary>Compatibility facade over <see cref="Advise"/> for context-free callers.</summary>
    public static IReadOnlyList<Recommendation> Recommend(
        BottleneckAnalysis analysis, GameProfile? game = null,
        OptimizationGoal goal = OptimizationGoal.Balanced)
    {
        return Advise(new AdviceRequest { Analysis = analysis, Game = game, Goal = goal })
            .Recommendations;
    }

    public static AdvicePlan Advise(AdviceRequest request)
    {
        var notes = new List<string>();
        var recommendations = new List<Recommendation>();

        // 1. Resolve the FPS target against reality (engine caps, display refresh).
        var targetFps = request.TargetFps ?? DefaultTargetFps;
        if (request.Game?.FrameCapFps is { } cap && targetFps > cap)
        {
            notes.Add($"{request.Game.Name} is capped at {cap} FPS by its engine — " +
                      $"targeting {targetFps} FPS is pointless, so the target was lowered to {cap}.");
            targetFps = cap;
        }

        var measuredFps = request.Frametimes?.AverageFps;
        double? requiredGain = measuredFps is > 0
            ? Math.Max(0, Math.Round((targetFps / measuredFps.Value - 1) * 100, 1))
            : null;

        // 2. Target already met on average: the problem, if any, is smoothness.
        if (requiredGain is 0)
        {
            recommendations.AddRange(StutterAdvice(request, targetFps, notes));
            return BuildPlan(targetFps, measuredFps, requiredGain, recommendations, notes);
        }

        // 3. Setting suggestions from the game profile, sized to the gap when known.
        recommendations.AddRange(FromGameProfile(request, requiredGain));

        // 4. Upscaling: only for GPU-style loads, only when the game has it and this GPU can run it.
        if (request.Analysis.Type is BottleneckType.Gpu or BottleneckType.Thermal)
            recommendations.AddRange(UpscalingAdvice(request, requiredGain, notes));

        // 5. Frame generation can rescue CPU-bound games — with a latency caveat.
        if (request.Analysis.Type == BottleneckType.Cpu)
            recommendations.AddRange(FrameGenerationAdvice(request));

        // 6. Generic advice for whatever the specific paths did not cover.
        recommendations.AddRange(GenericAdvice(request, measuredFps));

        var estimated = recommendations.Sum(r => r.ExpectedGainPercent ?? 0);
        if (requiredGain is { } needed && needed > 0 && estimated > 0 && estimated < needed * 0.8)
        {
            notes.Add($"Even with every change applied (~+{estimated:0}%), reaching {targetFps} FPS " +
                      $"(+{needed:0}% needed) looks out of reach — consider lowering the target, " +
                      "the resolution, or upgrading the limiting component.");
        }

        return BuildPlan(targetFps, measuredFps, requiredGain, recommendations, notes);
    }

    private static AdvicePlan BuildPlan(
        int targetFps, double? measuredFps, double? requiredGain,
        List<Recommendation> recommendations, List<string> notes)
    {
        var estimated = recommendations.Sum(r => r.ExpectedGainPercent ?? 0);
        return new AdvicePlan
        {
            TargetFps = targetFps,
            MeasuredAverageFps = measuredFps,
            RequiredGainPercent = requiredGain,
            EstimatedPlanGainPercent = estimated > 0 ? Math.Round(estimated, 1) : null,
            Recommendations = recommendations,
            Notes = notes,
        };
    }

    // ----- Game profile settings -------------------------------------------------

    private static IEnumerable<Recommendation> FromGameProfile(
        AdviceRequest request, double? requiredGain)
    {
        var game = request.Game;
        if (game is null)
            yield break;

        // A VRAM bottleneck gets dedicated, budget-based texture advice instead.
        if (request.Analysis.Type == BottleneckType.Vram &&
            VramTextureAdvice(request) is { } vramAdvice)
        {
            yield return vramAdvice;
            yield break;
        }

        var relevantImpacts = RelevantImpacts(request.Analysis.Type);
        var candidates = game.Settings
            .Where(s => relevantImpacts.Contains(s.Impact) && s.PerformanceGain > 0)
            .Where(s => request.Goal != OptimizationGoal.Quality || s.QualityLoss <= 2)
            .OrderByDescending(s => Score(s, request.Goal))
            .ThenByDescending(s => s.PerformanceGain)
            .ToList();

        // With a measured gap, take settings until the cumulative estimate covers
        // it; a small gap then yields a small plan. Without data, fixed counts.
        var maxCount = MaxSuggestions(request.Goal);
        double cumulative = 0;
        var taken = 0;
        foreach (var setting in candidates)
        {
            if (taken >= maxCount)
                break;
            if (requiredGain is { } needed && cumulative >= needed)
                break;

            var gain = GainPercent(setting);
            cumulative += gain;
            taken++;
            yield return new Recommendation
            {
                Title = $"{game.Name}: lower \"{setting.Name}\" by one step",
                Reason = $"Typical gain {setting.PerformanceGain}/5 for a visual cost of {setting.QualityLoss}/5.",
                Setting = setting,
                ExpectedGainPercent = setting.EstimatedGainPercent > 0 ? setting.EstimatedGainPercent : null,
            };
        }
    }

    private static double GainPercent(GameSetting setting) =>
        setting.EstimatedGainPercent > 0
            ? setting.EstimatedGainPercent
            : setting.PerformanceGain * PercentPerGainPoint;

    /// <summary>Ranking score: how much the goal weighs visual cost against FPS gained.</summary>
    private static int Score(GameSetting setting, OptimizationGoal goal) => goal switch
    {
        OptimizationGoal.Quality => setting.PerformanceGain - 2 * setting.QualityLoss,
        OptimizationGoal.Performance => 2 * setting.PerformanceGain - setting.QualityLoss,
        _ => setting.PerformanceGain - setting.QualityLoss,
    };

    private static int MaxSuggestions(OptimizationGoal goal) =>
        goal == OptimizationGoal.Performance ? 5 : 3;

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

    // ----- VRAM-aware texture advice ----------------------------------------------

    private static Recommendation? VramTextureAdvice(AdviceRequest request)
    {
        var setting = request.Game?.Settings.FirstOrDefault(s => s.VramGiBByLevel is { Count: > 0 });
        var vramBytes = request.Hardware?.PrimaryGpu?.VramBytes;
        if (setting?.VramGiBByLevel is not { } budgets || vramBytes is not > 0)
            return null;

        var vramGiB = vramBytes.Value / 1024.0 / 1024.0 / 1024.0;
        var usable = vramGiB - VramOverheadGiB;
        var fit = budgets
            .Where(level => level.Value <= usable)
            .OrderByDescending(level => level.Value)
            .Select(level => (string?)level.Key)
            .FirstOrDefault();
        if (fit is null)
            return null;

        var peak = request.Summary?.PeakVramUsagePercent;
        var measured = peak is { } p ? $" VRAM peaked at {p:0}% during the session." : "";
        return new Recommendation
        {
            Title = $"{request.Game!.Name}: set \"{setting!.Name}\" to {fit}",
            Reason = $"That tier needs ~{budgets[fit]:0.#} GiB and your GPU has {vramGiB:0.#} GiB " +
                     $"(~{usable:0.#} GiB usable once the OS and overlays take their share).{measured}",
            Setting = setting,
        };
    }

    // ----- Upscaling and frame generation -----------------------------------------

    private static IEnumerable<Recommendation> UpscalingAdvice(
        AdviceRequest request, double? requiredGain, List<string> notes)
    {
        var game = request.Game;
        var gpu = request.Hardware?.PrimaryGpu;

        // The game explicitly ships no upscaler: recommending one would be wrong.
        if (game?.Upscalers is { Count: 0 })
        {
            notes.Add($"{game.Name} has no built-in upscaler (DLSS/FSR/XeSS), " +
                      "so resolution and settings are the only levers.");
            yield break;
        }

        // Without hardware or upscaler knowledge, fall back to honest generic advice.
        if (gpu is null || game?.Upscalers is null)
        {
            var genericMode = request.Goal == OptimizationGoal.Performance ? "Balanced or Performance" : "Quality";
            yield return new Recommendation
            {
                Title = $"Enable upscaling (DLSS / FSR / XeSS) in {genericMode} mode",
                Reason = "Renders at a lower internal resolution for a large GPU-side gain with minimal visual cost.",
            };
            yield break;
        }

        var upscaler = GpuCapabilities.PreferredUpscaler(gpu, game.Upscalers);
        if (upscaler is null)
            yield break;

        var width = PrimaryDisplayWidth(request.Hardware);
        var (mode, gain) = UpscalerModeFor(requiredGain, width, request.Goal);
        if (width is > 0 and < 2200 && mode != "Quality")
        {
            mode = "Quality";
            gain = 12;
            notes.Add("At 1080p, upscaling below Quality mode renders from under 720p and gets " +
                      "visibly blurry — the mode was capped at Quality.");
        }

        yield return new Recommendation
        {
            Title = $"{game.Name}: enable {upscaler} in {mode} mode",
            Reason = ReasonForUpscaler(upscaler, gpu),
            ExpectedGainPercent = gain,
        };

        if (requiredGain is > 40 &&
            GpuCapabilities.AvailableFrameGeneration(gpu, game.Upscalers) is { } frameGen)
        {
            yield return new Recommendation
            {
                Title = $"{game.Name}: consider {frameGen}",
                Reason = "Roughly doubles displayed FPS. It adds input latency and does not fix " +
                         "stutter, so enable it on top of a healthy base frame rate (~60 FPS), " +
                         "not instead of one.",
            };
        }
    }

    private static string ReasonForUpscaler(string upscaler, GpuInfo gpu) => upscaler switch
    {
        "DLSS" => $"Your {gpu.Name} has the Tensor cores DLSS needs — it is the best-looking option on NVIDIA RTX.",
        "XeSS" => $"XeSS runs best on your {gpu.Name} (native XMX path on Intel Arc).",
        _ => $"FSR is the highest-quality upscaler available to your {gpu.Name} in this game.",
    };

    /// <summary>Picks the upscaler mode from the gap to close and the display resolution.</summary>
    private static (string Mode, double GainPercent) UpscalerModeFor(
        double? requiredGain, int? displayWidth, OptimizationGoal goal)
    {
        var is4K = displayWidth >= 3400;
        if (goal == OptimizationGoal.Quality)
            return ("Quality", is4K ? 30 : 20);
        return requiredGain switch
        {
            null => goal == OptimizationGoal.Performance ? ("Balanced", is4K ? 40 : 30) : ("Quality", is4K ? 30 : 20),
            <= 18 => ("Quality", is4K ? 30 : 20),
            <= 32 => ("Balanced", is4K ? 40 : 30),
            _ => ("Performance", is4K ? 55 : 40),
        };
    }

    private static IEnumerable<Recommendation> FrameGenerationAdvice(AdviceRequest request)
    {
        var gpu = request.Hardware?.PrimaryGpu;
        if (gpu is null || request.Game?.Upscalers is not { } upscalers)
            yield break;
        if (GpuCapabilities.AvailableFrameGeneration(gpu, upscalers) is not { } frameGen)
            yield break;

        yield return new Recommendation
        {
            Title = $"{request.Game.Name}: enable {frameGen}",
            Reason = "Generated frames do not need the CPU, so frame generation is one of the few " +
                     "things that helps a CPU-bound game — at the cost of some input latency.",
        };
    }

    private static int? PrimaryDisplayWidth(HardwareProfile? hardware) =>
        hardware?.Displays.FirstOrDefault(d => d.IsPrimary)?.WidthPixels
        ?? hardware?.Displays.FirstOrDefault()?.WidthPixels;

    // ----- Smoothness (target met on average) -------------------------------------

    private static IEnumerable<Recommendation> StutterAdvice(
        AdviceRequest request, int targetFps, List<string> notes)
    {
        var stats = request.Frametimes!;
        if (stats.OnePercentLowFps >= targetFps * 0.8 && stats.StutterPercent <= 1)
        {
            notes.Add($"Average FPS ({stats.AverageFps:0}) already meets the {targetFps} FPS target " +
                      "and frame delivery is smooth — no change needed.");
            yield break;
        }

        notes.Add($"Average FPS ({stats.AverageFps:0}) meets the {targetFps} FPS target but the " +
                  $"1% low is {stats.OnePercentLowFps:0} — the problem is stutter, not raw speed.");

        yield return new Recommendation
        {
            Title = $"Cap the frame rate at {targetFps} FPS",
            Reason = "A cap evens out frame pacing: the GPU stops sprinting then stalling, " +
                     "which is what your frametime spikes feel like.",
        };

        if (request.Summary?.PeakVramUsagePercent is > 85)
        {
            yield return new Recommendation
            {
                Title = "Lower texture quality by one step",
                Reason = $"VRAM peaked at {request.Summary.PeakVramUsagePercent:0}% — texture swapping " +
                         "is a classic stutter source even when average FPS looks fine.",
            };
        }

        if (request.Summary?.PeakRamUsagePercent is > 85)
        {
            yield return new Recommendation
            {
                Title = "Close memory-hungry background applications",
                Reason = $"RAM peaked at {request.Summary.PeakRamUsagePercent:0}% — paging causes " +
                         "exactly the hitches your capture shows.",
            };
        }
    }

    // ----- Generic fallbacks -------------------------------------------------------

    private static IEnumerable<Recommendation> GenericAdvice(AdviceRequest request, double? measuredFps)
    {
        switch (request.Analysis.Type)
        {
            case BottleneckType.Gpu:
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
                    Reason = "These reduce simulation and draw-call work; resolution changes and " +
                             "upscaling will not help a CPU limit.",
                };
                yield return new Recommendation
                {
                    Title = "Close background applications (browser, launchers, overlays)",
                    Reason = "Frees CPU cores for the game's main and worker threads.",
                };
                break;

            case BottleneckType.Vram when request.Game is null || VramTextureAdvice(request) is null:
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
                if (request.Hardware?.Memory.TotalBytes is > 0 and <= 17L << 30)
                {
                    yield return new Recommendation
                    {
                        Title = "Consider moving to 32 GiB of RAM",
                        Reason = "Modern open-world games plus a browser regularly exceed 16 GiB.",
                    };
                }
                break;

            case BottleneckType.Thermal:
                var capText = measuredFps is { } fps
                    ? $"{Math.Floor(fps * 0.9 / 5) * 5:0} FPS (slightly under your measured average)"
                    : "slightly below your average FPS";
                yield return new Recommendation
                {
                    Title = $"Cap the frame rate at {capText}",
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
