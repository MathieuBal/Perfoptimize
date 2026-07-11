using Perfoptimize.Core.Games;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Monitoring.Frametimes;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class GpuCapabilitiesTests
{
    private static GpuInfo Gpu(string name, string vendor, bool discrete = true) =>
        new() { Name = name, Vendor = vendor, IsDiscrete = discrete };

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3060", "NVIDIA", true)]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", "NVIDIA", false)]
    [InlineData("AMD Radeon RX 6700 XT", "AMD", false)]
    public void Dlss_needs_an_rtx_card(string name, string vendor, bool expected)
    {
        Assert.Equal(expected, GpuCapabilities.SupportsDlss(Gpu(name, vendor)));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 Ti", true)]
    [InlineData("NVIDIA GeForce RTX 5080", true)]
    [InlineData("NVIDIA GeForce RTX 3060", false)]
    public void Dlss_frame_generation_needs_rtx_40_or_newer(string name, bool expected)
    {
        Assert.Equal(expected, GpuCapabilities.SupportsDlssFrameGeneration(Gpu(name, "NVIDIA")));
    }

    [Fact]
    public void PreferredUpscaler_matches_gpu_vendor_to_game_offering()
    {
        string[] all = ["dlss", "fsr", "xess"];

        Assert.Equal("DLSS", GpuCapabilities.PreferredUpscaler(Gpu("NVIDIA GeForce RTX 3060", "NVIDIA"), all));
        Assert.Equal("FSR", GpuCapabilities.PreferredUpscaler(Gpu("NVIDIA GeForce GTX 1660", "NVIDIA"), all));
        Assert.Equal("FSR", GpuCapabilities.PreferredUpscaler(Gpu("AMD Radeon RX 6700 XT", "AMD"), all));
        Assert.Equal("XeSS", GpuCapabilities.PreferredUpscaler(Gpu("Intel Arc A750", "Intel"), all));
        // Game only ships XeSS: it runs everywhere, so recommend it even on AMD.
        Assert.Equal("XeSS", GpuCapabilities.PreferredUpscaler(Gpu("AMD Radeon RX 6700 XT", "AMD"), ["xess"]));
        Assert.Null(GpuCapabilities.PreferredUpscaler(Gpu("NVIDIA GeForce RTX 3060", "NVIDIA"), []));
    }
}

public class AdvisorTests
{
    private static GameProfile Game(
        IReadOnlyList<string>? upscalers = null, int? frameCap = null) => new()
    {
        Name = "Test Game",
        Executables = ["testgame.exe"],
        Upscalers = upscalers,
        FrameCapFps = frameCap,
        Settings =
        [
            new GameSetting { Name = "Ray Tracing", Impact = SettingImpact.Gpu, QualityLoss = 3, PerformanceGain = 5, EstimatedGainPercent = 30 },
            new GameSetting { Name = "Volumetric Fog", Impact = SettingImpact.Gpu, QualityLoss = 1, PerformanceGain = 3, EstimatedGainPercent = 8 },
            new GameSetting { Name = "Shadows", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 3, EstimatedGainPercent = 8 },
            new GameSetting
            {
                Name = "Texture Quality", Impact = SettingImpact.Vram, QualityLoss = 2, PerformanceGain = 1,
                VramGiBByLevel = new Dictionary<string, double> { ["Low"] = 4, ["Medium"] = 5, ["High"] = 7, ["Ultra"] = 10 },
            },
        ],
    };

    private static HardwareProfile Hardware(
        string gpuName = "NVIDIA GeForce RTX 3060", string vendor = "NVIDIA",
        long vramGiB = 12, int displayWidth = 2560) => new()
    {
        Gpus = [new GpuInfo { Name = gpuName, Vendor = vendor, IsDiscrete = true, VramBytes = vramGiB << 30 }],
        Displays = [new DisplayInfo { Name = "Main", WidthPixels = displayWidth, HeightPixels = 1440, IsPrimary = true }],
    };

    private static FrametimeStatistics Fps(double average, double onePercentLow, double stutterPercent = 2) => new()
    {
        FrameCount = 1000,
        AverageFps = average,
        OnePercentLowFps = onePercentLow,
        AverageFrametimeMs = 1000 / average,
        WorstFrametimeMs = 1000 / onePercentLow,
        StutterCount = 20,
        StutterPercent = stutterPercent,
    };

    private static BottleneckAnalysis Gpu() => new() { Type = BottleneckType.Gpu };

    [Fact]
    public void Required_gain_is_computed_from_measured_fps_and_target()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(),
            Frametimes = Fps(average: 48, onePercentLow: 40),
            TargetFps = 60,
        });

        Assert.Equal(48, plan.MeasuredAverageFps);
        Assert.Equal(25, plan.RequiredGainPercent); // 60/48 - 1 = 25%
    }

    [Fact]
    public void Plan_size_matches_the_gap_small_gap_small_plan()
    {
        var smallGap = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(), Game = Game(upscalers: []),
            Frametimes = Fps(57, 50), TargetFps = 60, // +5% needed
        });
        var bigGap = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(), Game = Game(upscalers: []),
            Frametimes = Fps(35, 30), TargetFps = 60, // +71% needed
        });

        var smallSettings = smallGap.Recommendations.Count(r => r.Setting is not null);
        var bigSettings = bigGap.Recommendations.Count(r => r.Setting is not null);
        Assert.Equal(1, smallSettings); // Ray Tracing alone (+30%) covers +5%
        Assert.True(bigSettings > smallSettings);
    }

    [Fact]
    public void Engine_frame_cap_clamps_the_target_with_a_note()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(),
            Game = Game(upscalers: [], frameCap: 60),
            TargetFps = 120,
        });

        Assert.Equal(60, plan.TargetFps);
        Assert.Contains(plan.Notes, n => n.Contains("capped at 60 FPS"));
    }

    [Fact]
    public void Upscaler_is_chosen_for_the_actual_gpu()
    {
        var rtx = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(), Game = Game(upscalers: ["dlss", "fsr", "xess"]),
            Hardware = Hardware("NVIDIA GeForce RTX 3060", "NVIDIA"),
        });
        var gtx = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(), Game = Game(upscalers: ["dlss", "fsr", "xess"]),
            Hardware = Hardware("NVIDIA GeForce GTX 1660", "NVIDIA"),
        });

        Assert.Contains(rtx.Recommendations, r => r.Title.Contains("enable DLSS"));
        Assert.Contains(gtx.Recommendations, r => r.Title.Contains("enable FSR"));
        Assert.DoesNotContain(gtx.Recommendations, r => r.Title.Contains("DLSS"));
    }

    [Fact]
    public void No_upscaling_advice_when_the_game_has_none()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(), Game = Game(upscalers: []),
            Hardware = Hardware(),
        });

        Assert.DoesNotContain(plan.Recommendations,
            r => r.Title.Contains("DLSS") || r.Title.Contains("FSR") || r.Title.Contains("XeSS") ||
                 r.Title.Contains("upscal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Notes, n => n.Contains("no built-in upscaler"));
    }

    [Fact]
    public void At_1080p_the_upscaler_mode_is_capped_at_quality()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(), Game = Game(upscalers: ["dlss"]),
            Hardware = Hardware(displayWidth: 1920),
            Frametimes = Fps(35, 30), TargetFps = 60, // big gap would normally mean Performance mode
        });

        var upscaling = plan.Recommendations.Single(r => r.Title.Contains("DLSS"));
        Assert.Contains("Quality mode", upscaling.Title);
        Assert.Contains(plan.Notes, n => n.Contains("1080p"));
    }

    [Fact]
    public void Big_gap_at_1440p_selects_a_more_aggressive_mode()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(), Game = Game(upscalers: ["dlss"]),
            Hardware = Hardware(displayWidth: 2560),
            Frametimes = Fps(35, 30), TargetFps = 60,
        });

        var upscaling = plan.Recommendations.Single(r => r.Title.Contains("DLSS") && !r.Title.Contains("Frame"));
        Assert.Contains("Performance mode", upscaling.Title);
    }

    [Fact]
    public void Frame_generation_is_offered_to_cpu_bound_games_on_capable_gpus()
    {
        var capable = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Cpu },
            Game = Game(upscalers: ["dlss", "dlss-fg"]),
            Hardware = Hardware("NVIDIA GeForce RTX 4070", "NVIDIA"),
        });
        var incapable = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Cpu },
            Game = Game(upscalers: ["dlss", "dlss-fg"]),
            Hardware = Hardware("NVIDIA GeForce RTX 3060", "NVIDIA"),
        });

        Assert.Contains(capable.Recommendations, r => r.Title.Contains("Frame Generation"));
        Assert.DoesNotContain(incapable.Recommendations, r => r.Title.Contains("Frame Generation"));
    }

    [Fact]
    public void Vram_bottleneck_picks_the_texture_tier_that_fits_the_card()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Vram },
            Game = Game(),
            Hardware = Hardware(vramGiB: 8), // usable ≈ 7.2 GiB → High (7), not Ultra (10)
            Summary = new PerformanceSummary { SampleCount = 30, PeakVramUsagePercent = 96 },
        });

        var texture = plan.Recommendations.Single(r => r.Setting?.Name == "Texture Quality");
        Assert.Contains("to High", texture.Title);
        Assert.Contains("8 GiB", texture.Reason);
        Assert.Contains("96%", texture.Reason);
    }

    [Fact]
    public void Target_met_but_stuttery_yields_smoothness_advice_not_quality_cuts()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(), Game = Game(upscalers: ["dlss"]),
            Hardware = Hardware(),
            Summary = new PerformanceSummary { SampleCount = 30, PeakVramUsagePercent = 90 },
            Frametimes = Fps(average: 72, onePercentLow: 31), TargetFps = 60,
        });

        Assert.Equal(0, plan.RequiredGainPercent);
        Assert.Contains(plan.Recommendations, r => r.Title.Contains("Cap the frame rate at 60"));
        Assert.Contains(plan.Recommendations, r => r.Title.Contains("texture quality"));
        Assert.DoesNotContain(plan.Recommendations, r => r.Title.Contains("Ray Tracing"));
        Assert.Contains(plan.Notes, n => n.Contains("stutter"));
    }

    [Fact]
    public void Smooth_and_on_target_means_no_change_needed()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(),
            Frametimes = Fps(average: 72, onePercentLow: 60, stutterPercent: 0.1),
            TargetFps = 60,
        });

        Assert.Empty(plan.Recommendations);
        Assert.Contains(plan.Notes, n => n.Contains("no change needed"));
    }

    [Fact]
    public void Unreachable_target_is_flagged_honestly()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = Gpu(),
            Game = Game(upscalers: []),
            Frametimes = Fps(average: 30, onePercentLow: 25),
            TargetFps = 144, // +380% needed, settings offer ~+46%
        });

        Assert.Contains(plan.Notes, n => n.Contains("out of reach"));
    }
}
