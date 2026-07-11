using Perfoptimize.Core.Games;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Monitoring.Linux;
using Perfoptimize.Core.Monitoring.Windows;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class GpuEngineInstanceParserTests
{
    [Fact]
    public void Parse_decodes_pid_luid_and_engine_type()
    {
        var instance = GpuEngineInstanceParser.Parse(
            "pid_1234_luid_0x00000000_0x0000C852_phys_0_engtype_3D");

        Assert.NotNull(instance);
        Assert.Equal(1234, instance.Value.Pid);
        Assert.Equal("0x00000000_0x0000C852", instance.Value.Luid);
        Assert.Equal("3D", instance.Value.EngineType);
    }

    [Theory]
    [InlineData("_Total")]
    [InlineData("pid_abc_luid_x_phys_0_engtype_3D")]
    [InlineData("")]
    public void Parse_rejects_unknown_shapes(string name)
    {
        Assert.Null(GpuEngineInstanceParser.Parse(name));
    }

    [Fact]
    public void Aggregate_attributes_gpu_usage_to_the_game_and_its_adapter()
    {
        var igpu = "0x00000000_0x0000AAAA";
        var dgpu = "0x00000000_0x0000C852";
        List<(GpuEngineInstance, double)> readings =
        [
            // The game renders on the discrete GPU.
            (new GpuEngineInstance(1234, dgpu, "3D"), 68),
            // A browser renders on the iGPU — must not pollute the game's numbers.
            (new GpuEngineInstance(999, igpu, "3D"), 45),
            // An overlay also uses the discrete GPU.
            (new GpuEngineInstance(777, dgpu, "3D"), 5),
            // Video decode is not rendering load.
            (new GpuEngineInstance(1234, dgpu, "VideoDecode"), 90),
        ];

        var (gameGpu, adapterGpu, adapterLuid) = GpuEngineInstanceParser.Aggregate(readings, 1234);

        Assert.Equal(68, gameGpu);
        Assert.Equal(73, adapterGpu); // game + overlay on the same card, browser excluded
        Assert.Equal(dgpu, adapterLuid);
    }

    [Fact]
    public void Aggregate_without_game_pid_reports_the_busiest_adapter()
    {
        List<(GpuEngineInstance, double)> readings =
        [
            (new GpuEngineInstance(1, "luidA", "3D"), 20),
            (new GpuEngineInstance(2, "luidB", "3D"), 80),
        ];

        var (gameGpu, adapterGpu, adapterLuid) = GpuEngineInstanceParser.Aggregate(readings, null);

        Assert.Null(gameGpu);
        Assert.Equal(80, adapterGpu);
        Assert.Equal("luidB", adapterLuid);
    }
}

public class PerCoreCpuTests
{
    [Fact]
    public void ParsePerCoreTicks_reads_each_core_line()
    {
        const string procStat = """
            cpu  100 0 100 800 0 0 0 0 0 0
            cpu0 90 0 90 20 0 0 0 0 0 0
            cpu1 10 0 10 780 0 0 0 0 0 0
            intr 12345
            """;

        var cores = LinuxTelemetryParser.ParsePerCoreTicks(procStat);

        Assert.Equal(2, cores.Count);
        Assert.Equal(20, cores["cpu0"].IdleTicks);
        Assert.Equal(780, cores["cpu1"].IdleTicks);
    }

    [Fact]
    public void ParseProcessTicks_survives_parentheses_in_the_process_name()
    {
        // comm can contain spaces and parentheses: "(my (weird) game)".
        const string stat = "1234 (my (weird) game) S 1 1234 1234 0 -1 4194560 " +
                            "500 0 0 0 700 300 0 0 20 0 8 0 12345 1000000 500 18446744073709551615";

        Assert.Equal(1000, LinuxTelemetryParser.ParseProcessTicks(stat)); // utime 700 + stime 300
    }

    [Fact]
    public void ProcessCpuUsage_is_a_share_of_the_whole_machine()
    {
        var previous = new CpuTicks(0, 1000);
        var current = new CpuTicks(0, 2000); // 1000 machine jiffies elapsed

        var usage = LinuxTelemetryParser.ProcessCpuUsageBetween(100, 350, previous, current);

        Assert.Equal(25, usage); // the process consumed 250 of them
    }
}

public class ThrottleDetectionTests
{
    [Fact]
    public void ParseLine_reads_clock_and_throttle_mask()
    {
        var status = NvidiaSmi.ParseLine("97, 11000, 12288, 84, 1450, 0x0000000000000040");

        Assert.NotNull(status);
        Assert.Equal(1450, status.Value.CoreClockMhz);
        Assert.True(status.Value.ThermalThrottleActive); // HwThermalSlowdown bit
    }

    [Fact]
    public void Power_cap_alone_is_not_thermal_throttling()
    {
        var status = NvidiaSmi.ParseLine("97, 11000, 12288, 70, 1900, 0x0000000000000004");

        Assert.False(status!.Value.ThermalThrottleActive); // SwPowerCap is normal boost behavior
    }

    [Fact]
    public void Old_drivers_without_the_extra_fields_still_parse()
    {
        var status = NvidiaSmi.ParseLine("45, 6144, 12288, 65");

        Assert.NotNull(status);
        Assert.Null(status.Value.CoreClockMhz);
        Assert.Null(status.Value.ThermalThrottleActive);
    }

    [Fact]
    public void Driver_reported_throttle_is_announced_as_confirmed()
    {
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            AverageGpuUsagePercent = 99,
            PeakGpuTemperatureC = 84, // below the naive threshold!
            GpuThermalThrottleObserved = true,
        });

        Assert.Equal(BottleneckType.Thermal, analysis.Type);
        Assert.Contains("confirmed", analysis.Explanation);
    }

    [Fact]
    public void High_temperature_alone_is_reported_as_suspicion_only()
    {
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            PeakGpuTemperatureC = 89,
            GpuThermalThrottleObserved = false,
        });

        Assert.Equal(BottleneckType.Thermal, analysis.Type);
        Assert.Contains("NOT confirmed", analysis.Explanation);
    }
}

public class SingleThreadBottleneckTests
{
    [Fact]
    public void One_pinned_core_with_low_total_cpu_is_cpu_bound()
    {
        // The exact scenario a total-only reading misses:
        // main thread at 96%, machine-wide CPU at 35%, GPU waiting at 65%.
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            AverageCpuUsagePercent = 35,
            AverageMaxCoreUsagePercent = 96,
            AverageGameCpuUsagePercent = 30,
            AverageGpuUsagePercent = 65,
        });

        Assert.Equal(BottleneckType.Cpu, analysis.Type);
        Assert.Contains("single thread", analysis.Explanation);
        Assert.Contains("96%", analysis.Explanation);
    }

    [Fact]
    public void Busy_single_core_with_saturated_gpu_stays_gpu_bound()
    {
        var analysis = BottleneckDetector.Analyze(new PerformanceSummary
        {
            SampleCount = 30,
            AverageCpuUsagePercent = 40,
            AverageMaxCoreUsagePercent = 95,
            AverageGpuUsagePercent = 97,
        });

        Assert.Equal(BottleneckType.Gpu, analysis.Type);
    }
}

public class ProfileValidationTests
{
    [Fact]
    public void All_built_in_profiles_are_valid()
    {
        foreach (var profile in GameProfileRepository.LoadBuiltIn())
        {
            var errors = GameProfileValidator.Validate(profile);
            Assert.True(errors.Count == 0,
                $"{profile.Name}: {string.Join("; ", errors)}");
        }
        Assert.True(GameProfileRepository.LoadBuiltIn().Count >= 5);
    }

    [Fact]
    public void Out_of_range_scores_and_bad_ladders_are_rejected()
    {
        var profile = new GameProfile
        {
            Name = "Broken Game",
            Executables = ["broken.exe"],
            Upscalers = ["dlss", "magic-upscaler"],
            Settings =
            [
                new GameSetting { Name = "Shadows", QualityLoss = 9, PerformanceGain = -1, EstimatedGainPercent = 250 },
                new GameSetting
                {
                    Name = "Textures",
                    Config = new SettingBinding { File = "cfg.ini", Key = "tex", Levels = ["1"] },
                },
            ],
        };

        var errors = GameProfileValidator.Validate(profile);

        Assert.Contains(errors, e => e.Contains("qualityLoss"));
        Assert.Contains(errors, e => e.Contains("performanceGain"));
        Assert.Contains(errors, e => e.Contains("estimatedGainPercent"));
        Assert.Contains(errors, e => e.Contains("at least 2 levels"));
        Assert.Contains(errors, e => e.Contains("magic-upscaler"));
        Assert.Contains(errors, e => e.Contains("no config files"));
    }

    [Fact]
    public void Invalid_user_profile_files_are_skipped_on_load()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"perfoptimize-profiles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "good.json"),
                """{ "name": "Good", "executables": ["good.exe"], "settings": [{ "name": "Shadows", "impact": "gpu", "qualityLoss": 2, "performanceGain": 3 }] }""");
            File.WriteAllText(Path.Combine(directory, "bad.json"),
                """{ "name": "Bad", "executables": [], "settings": [] }""");

            var profiles = GameProfileRepository.LoadFromDirectory(directory);

            var profile = Assert.Single(profiles);
            Assert.Equal("Good", profile.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
