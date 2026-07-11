using Perfoptimize.Core.Games;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class RecommendationEngineTests
{
    private static readonly GameProfile Game = new()
    {
        Name = "Test Game",
        Executables = ["testgame.exe"],
        Settings =
        [
            new GameSetting { Name = "Ray Tracing", Impact = SettingImpact.Gpu, QualityLoss = 3, PerformanceGain = 5 },
            new GameSetting { Name = "Volumetric Fog", Impact = SettingImpact.Gpu, QualityLoss = 1, PerformanceGain = 3 },
            new GameSetting { Name = "Shadows", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 3 },
            new GameSetting { Name = "Textures", Impact = SettingImpact.Vram, QualityLoss = 2, PerformanceGain = 1 },
            new GameSetting { Name = "Crowd Density", Impact = SettingImpact.Cpu, QualityLoss = 2, PerformanceGain = 4 },
            new GameSetting { Name = "Draw Distance", Impact = SettingImpact.Mixed, QualityLoss = 3, PerformanceGain = 3 },
        ],
    };

    private static BottleneckAnalysis Analysis(BottleneckType type) => new() { Type = type };

    [Fact]
    public void Gpu_bottleneck_suggests_the_most_efficient_gpu_settings_first()
    {
        var recommendations = RecommendationEngine.Recommend(Analysis(BottleneckType.Gpu), Game);

        // Best efficiency (gain - visual cost): Ray Tracing (+2) and Volumetric Fog (+2), then Shadows (+1).
        Assert.Contains("Ray Tracing", recommendations[0].Title + recommendations[1].Title);
        Assert.Contains("Volumetric Fog", recommendations[0].Title + recommendations[1].Title);
        Assert.DoesNotContain(recommendations, r => r.Title.Contains("Crowd Density"));
        Assert.DoesNotContain(recommendations, r => r.Title.Contains("Textures"));
    }

    [Fact]
    public void Cpu_bottleneck_targets_cpu_and_mixed_settings()
    {
        var recommendations = RecommendationEngine.Recommend(Analysis(BottleneckType.Cpu), Game);

        Assert.Contains(recommendations, r => r.Title.Contains("Crowd Density"));
        Assert.Contains(recommendations, r => r.Title.Contains("Draw Distance"));
        Assert.DoesNotContain(recommendations, r => r.Title.Contains("Ray Tracing"));
    }

    [Fact]
    public void Vram_bottleneck_targets_textures()
    {
        var recommendations = RecommendationEngine.Recommend(Analysis(BottleneckType.Vram), Game);

        Assert.Contains(recommendations, r => r.Title.Contains("Textures"));
        Assert.DoesNotContain(recommendations, r => r.Title.Contains("Shadows"));
    }

    [Fact]
    public void Without_game_profile_generic_advice_is_still_given()
    {
        var recommendations = RecommendationEngine.Recommend(Analysis(BottleneckType.Gpu));

        Assert.NotEmpty(recommendations);
        Assert.Contains(recommendations, r => r.Title.Contains("DLSS / FSR / XeSS"));
    }

    [Fact]
    public void Every_bottleneck_type_produces_at_least_one_recommendation()
    {
        foreach (var type in Enum.GetValues<BottleneckType>())
        {
            var recommendations = RecommendationEngine.Recommend(Analysis(type), Game);
            Assert.True(recommendations.Count > 0, $"No recommendation for {type}");
        }
    }
}
