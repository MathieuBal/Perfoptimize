using Perfoptimize.Core.Games;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

/// <summary>
/// The core correctness fix: not every setting is "lower for FPS". Settings
/// marked KeepEnabled (engine multithreading) must never be recommended to
/// lower, and gains must read as theoretical until measured.
/// </summary>
public class SettingSemanticsTests
{
    private static GameProfile PoeLike() => new()
    {
        Name = "Test ARPG",
        Executables = ["arpg.exe"],
        Upscalers = [],
        Settings =
        [
            new GameSetting
            {
                Name = "Engine Multithreading", Impact = SettingImpact.Cpu,
                Action = SettingAction.KeepEnabled, PerformanceGain = 0, QualityLoss = 0,
            },
            new GameSetting
            {
                Name = "Renderer", Impact = SettingImpact.Mixed,
                Action = SettingAction.TestAlternatives, PerformanceGain = 0, QualityLoss = 0,
            },
            new GameSetting
            {
                Name = "Shadows", Impact = SettingImpact.Gpu, Action = SettingAction.Lower,
                QualityLoss = 2, PerformanceGain = 4, EstimatedGainPercent = 12,
            },
        ],
    };

    private static IReadOnlyList<Recommendation> Advise(GameProfile game, BottleneckType type) =>
        RecommendationEngine.Recommend(new BottleneckAnalysis { Type = type }, game);

    [Fact]
    public void A_keep_enabled_setting_is_never_recommended_to_lower()
    {
        var recs = Advise(PoeLike(), BottleneckType.Cpu);

        Assert.DoesNotContain(recs, r =>
            r.Title.Contains("Engine Multithreading") &&
            (r.Title.Contains("lower", StringComparison.OrdinalIgnoreCase) ||
             r.Title.Contains("disable", StringComparison.OrdinalIgnoreCase)));
        // Instead it is explicitly recommended to leave on.
        Assert.Contains(recs, r => r.Title.Contains("leave \"Engine Multithreading\" enabled"));
    }

    [Fact]
    public void A_cpu_bound_game_without_lowerable_cpu_settings_says_so_honestly()
    {
        var recs = Advise(PoeLike(), BottleneckType.Cpu);

        // No invented "crowd density / draw distance" advice for a game that has none.
        Assert.DoesNotContain(recs, r => r.Title.Contains("crowd density"));
        // An honest "nothing validated" statement instead.
        Assert.Contains(recs, r => r.Title.Contains("No validated Cpu setting is documented"));
    }

    [Fact]
    public void A_renderer_setting_is_offered_as_a_measured_ab_test_not_a_lowering()
    {
        var recs = Advise(PoeLike(), BottleneckType.Cpu);

        Assert.Contains(recs, r => r.Title.Contains("A/B-test") && r.Title.Contains("Renderer"));
    }

    [Fact]
    public void Lowerable_settings_still_work_for_a_gpu_bottleneck()
    {
        var recs = Advise(PoeLike(), BottleneckType.Gpu);

        Assert.Contains(recs, r => r.Title.Contains("lower \"Shadows\""));
    }

    [Fact]
    public void Known_game_advice_does_not_leak_generic_setting_suggestions()
    {
        // A GPU-bound known game must not get the generic "disable ray tracing"
        // line — its profile is authoritative for settings.
        var recs = Advise(PoeLike(), BottleneckType.Gpu);

        Assert.DoesNotContain(recs, r => r.Title == "Disable ray tracing if enabled");
    }

    [Fact]
    public void Estimated_gains_are_flagged_as_unverified_in_the_reason()
    {
        var recs = Advise(PoeLike(), BottleneckType.Gpu);

        var shadows = recs.First(r => r.Title.Contains("Shadows"));
        Assert.Equal(EvidenceLevel.Estimated, shadows.Evidence);
        Assert.Contains("unverified", shadows.Reason);
    }

    [Fact]
    public void The_builtin_path_of_exile_profile_keeps_multithreading_on()
    {
        var poe = GameProfileRepository.LoadBuiltIn()
            .First(p => p.Name == "Path of Exile");

        var mt = poe.Settings.First(s => s.Name.Contains("Multithreading"));
        Assert.Equal(SettingAction.KeepEnabled, mt.Action);

        // And the engine, given a CPU bottleneck, never tells you to lower it.
        var recs = Advise(poe, BottleneckType.Cpu);
        Assert.DoesNotContain(recs, r =>
            r.Setting?.Name.Contains("Multithreading") == true &&
            r.Title.Contains("lower", StringComparison.OrdinalIgnoreCase));
    }
}
