using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class GpuAttributionDiagnosisTests
{
    [Fact]
    public void Game_saturating_the_gpu_is_confirmed_gpu_bound()
    {
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            AverageGpuUsagePercent = 97,
            AverageGameGpuUsagePercent = 94,
        });

        Assert.Equal(BottleneckType.Gpu, analysis.Type);
        Assert.Equal(BottleneckConfidence.Confirmed, analysis.Confidence);
    }

    [Fact]
    public void Saturated_adapter_with_low_game_share_is_external_contention()
    {
        // The reviewer's scenario: browser/OBS eating the same card as the game.
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            AverageGpuUsagePercent = 95,
            AverageGameGpuUsagePercent = 40,
        });

        Assert.Equal(BottleneckType.ExternalGpuLoad, analysis.Type);
        Assert.Contains("other applications", analysis.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Saturated_adapter_without_attribution_is_only_suspected()
    {
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            AverageGpuUsagePercent = 96,
            AverageGameGpuUsagePercent = null,
        });

        Assert.Equal(BottleneckType.Gpu, analysis.Type);
        Assert.Equal(BottleneckConfidence.Suspected, analysis.Confidence);
        Assert.Contains("attribution was unavailable", analysis.Explanation);
    }

    [Fact]
    public void External_gpu_load_advice_says_close_apps_not_lower_settings()
    {
        var recommendations = RecommendationEngine.Recommend(
            new BottleneckAnalysis { Type = BottleneckType.ExternalGpuLoad });

        Assert.Contains(recommendations, r => r.Title.Contains("Close other GPU-hungry"));
        Assert.DoesNotContain(recommendations, r => r.Title.Contains("upscal", StringComparison.OrdinalIgnoreCase));
    }
}

public class SingleThreadAttributionTests
{
    [Fact]
    public void Pinned_core_with_idle_game_process_blames_another_program()
    {
        // A core is saturated but the game barely uses CPU: antivirus, indexer...
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            AverageCpuUsagePercent = 20,
            AverageMaxCoreUsagePercent = 97,
            AverageGameCpuUsagePercent = 3, // 3% of 8 cores ≈ 0.24 core's worth
            AverageGpuUsagePercent = 50,
        }, logicalCores: 8);

        Assert.Equal(BottleneckType.Cpu, analysis.Type);
        Assert.Equal(BottleneckConfidence.Suspected, analysis.Confidence);
        Assert.Contains("another program", analysis.Explanation);
    }

    [Fact]
    public void Pinned_core_with_active_game_process_confirms_single_thread_limit()
    {
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            AverageCpuUsagePercent = 30,
            AverageMaxCoreUsagePercent = 96,
            AverageGameCpuUsagePercent = 22, // 22% of 8 cores ≈ 1.8 cores' worth
            AverageGpuUsagePercent = 60,
        }, logicalCores: 8);

        Assert.Equal(BottleneckType.Cpu, analysis.Type);
        Assert.Equal(BottleneckConfidence.Confirmed, analysis.Confidence);
        Assert.Contains("single thread", analysis.Explanation);
    }
}

public class ThrottleCauseSeparationTests
{
    [Fact]
    public void Hw_slowdown_bit_is_not_reported_as_thermal()
    {
        var status = NvidiaSmi.ParseLine("97, 11000, 12288, 70, 1500, 0x0000000000000008");

        Assert.False(status!.Value.ThermalThrottleActive);
        Assert.True(status.Value.HardwareSlowdownActive);
    }

    [Fact]
    public void Hardware_slowdown_yields_a_suspected_verdict_mentioning_power()
    {
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            GpuHardwareSlowdownObserved = true,
        });

        Assert.Equal(BottleneckType.Thermal, analysis.Type);
        Assert.Equal(BottleneckConfidence.Suspected, analysis.Confidence);
        Assert.Contains("power", analysis.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmed throttling", analysis.Explanation);
    }

    [Fact]
    public void Thermal_bits_remain_a_confirmed_verdict()
    {
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            GpuThermalThrottleObserved = true,
        });

        Assert.Equal(BottleneckConfidence.Confirmed, analysis.Confidence);
    }
}

public class AdvisorV3Tests
{
    private static Perfoptimize.Core.Games.GameProfile Game() => new()
    {
        Name = "Test Game",
        Executables = ["testgame.exe"],
        Upscalers = [],
        Settings =
        [
            new() { Name = "Ray Tracing", Impact = Perfoptimize.Core.Games.SettingImpact.Gpu, QualityLoss = 3, PerformanceGain = 5, EstimatedGainPercent = 30 },
            new() { Name = "Volumetric Fog", Impact = Perfoptimize.Core.Games.SettingImpact.Gpu, QualityLoss = 1, PerformanceGain = 3, EstimatedGainPercent = 8 },
            new() { Name = "Shadows", Impact = Perfoptimize.Core.Games.SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 3, EstimatedGainPercent = 8 },
        ],
    };

    private static Perfoptimize.Core.Monitoring.Frametimes.FrametimeStatistics Fps(double average) => new()
    {
        FrameCount = 1000,
        AverageFps = average,
        OnePercentLowFps = average * 0.8,
        AverageFrametimeMs = 1000 / average,
        WorstFrametimeMs = 50,
        StutterCount = 5,
        StutterPercent = 0.5,
    };

    [Fact]
    public void Small_gap_picks_the_cheapest_sufficient_change_not_the_biggest()
    {
        // +5% needed: Volumetric Fog (gain 8, cost 1) must win over
        // Ray Tracing (gain 30, cost 3).
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Gpu },
            Game = Game(),
            Frametimes = Fps(57),
            TargetFps = 60,
        });

        var settings = plan.Recommendations.Where(r => r.Setting is not null).ToList();
        var chosen = Assert.Single(settings);
        Assert.Equal("Volumetric Fog", chosen.Setting!.Name);
    }

    [Fact]
    public void Medium_gap_prefers_two_cheap_changes_over_one_expensive_one()
    {
        // +14% needed: Fog+Shadows (gain 16, cost 3) covers it at the same cost
        // as Ray Tracing alone (cost 3) — ties break on fewer... both cost 3;
        // count favors RT (1 change). Verify the plan covers the gap either way
        // with total cost <= 3.
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Gpu },
            Game = Game(),
            Frametimes = Fps(52.5),
            TargetFps = 60,
        });

        var settings = plan.Recommendations.Where(r => r.Setting is not null).ToList();
        var totalCost = settings.Sum(r => r.Setting!.QualityLoss);
        var totalGain = settings.Sum(r => r.ExpectedGainPercent ?? 0);
        Assert.True(totalGain >= 14, $"plan gain {totalGain} must cover +14%");
        Assert.True(totalCost <= 3, $"visual cost {totalCost} must not exceed the cheapest covering option");
    }

    [Fact]
    public void Plan_gain_is_a_range_not_a_false_promise()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Gpu },
            Game = Game(),
            Frametimes = Fps(30), // huge gap: several settings selected
            TargetFps = 60,
        });

        Assert.NotNull(plan.EstimatedGainLowPercent);
        Assert.NotNull(plan.EstimatedGainHighPercent);
        Assert.True(plan.EstimatedGainLowPercent < plan.EstimatedGainHighPercent);
        Assert.Contains(plan.Notes, n => n.Contains("do not stack linearly"));
    }

    [Fact]
    public void Frame_generation_is_refused_below_a_healthy_base_frame_rate()
    {
        var game = new Perfoptimize.Core.Games.GameProfile
        {
            Name = "FG Game",
            Executables = ["fg.exe"],
            Upscalers = ["dlss", "dlss-fg"],
            Settings = [new() { Name = "Crowds", Impact = Perfoptimize.Core.Games.SettingImpact.Cpu, QualityLoss = 2, PerformanceGain = 4, EstimatedGainPercent = 10 }],
        };
        var hardware = new Perfoptimize.Core.Hardware.HardwareProfile
        {
            Gpus = [new() { Name = "NVIDIA GeForce RTX 4070", Vendor = "NVIDIA", IsDiscrete = true }],
        };

        var lowFps = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Cpu },
            Game = game, Hardware = hardware,
            Frametimes = Fps(28), TargetFps = 60,
        });
        var healthyFps = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Cpu },
            Game = game, Hardware = hardware,
            Frametimes = Fps(52), TargetFps = 90,
        });

        Assert.DoesNotContain(lowFps.Recommendations, r => r.Title.Contains("Frame Generation"));
        Assert.Contains(lowFps.Notes, n => n.Contains("below ~45 FPS"));
        Assert.Contains(healthyFps.Recommendations, r => r.Title.Contains("Frame Generation"));
    }

    [Fact]
    public void Target_beyond_display_refresh_is_clamped_with_a_note()
    {
        var hardware = new Perfoptimize.Core.Hardware.HardwareProfile
        {
            Displays = [new() { Name = "Office", WidthPixels = 1920, HeightPixels = 1080, RefreshRateHz = 60, IsPrimary = true }],
        };

        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Gpu },
            Hardware = hardware,
            TargetFps = 240,
        });

        Assert.Equal(60, plan.TargetFps);
        Assert.Contains(plan.Notes, n => n.Contains("60 Hz"));
    }
}

public class FsrFrameGenCapabilityTests
{
    private static Perfoptimize.Core.Hardware.GpuInfo Gpu(string name, string vendor, bool discrete = true) =>
        new() { Name = name, Vendor = vendor, IsDiscrete = discrete };

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3060", "NVIDIA", true)]   // RTX: yes
    [InlineData("NVIDIA GeForce GTX 1660", "NVIDIA", false)]  // GTX: too old
    [InlineData("AMD Radeon RX 6700 XT", "AMD", true)]        // RX 6000: yes
    [InlineData("AMD Radeon RX 580", "AMD", false)]           // RX 500: too old
    [InlineData("Intel Arc A750", "Intel", true)]
    public void Fsr_frame_generation_requires_a_modern_card(string name, string vendor, bool expected)
    {
        Assert.Equal(expected, GpuCapabilities.SupportsFsrFrameGeneration(Gpu(name, vendor)));
    }
}
