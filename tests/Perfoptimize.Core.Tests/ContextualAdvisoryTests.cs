using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class ContextualAdvisoryTests
{
    [Fact]
    public void Config_reader_keeps_qualified_keys_and_drops_ambiguous_bare_aliases()
    {
        const string ini = """
            [DISPLAY]
            upscale=None
            shared=1
            [GENERAL]
            shared=2
            engine_multithreading_mode=enabled
            """;

        var values = ConfigValueReader.Read(ini, "ini");

        Assert.Equal("None", values["DISPLAY.upscale"]);
        Assert.Equal("None", values["upscale"]);
        Assert.Equal("1", values["DISPLAY.shared"]);
        Assert.Equal("2", values["GENERAL.shared"]);
        Assert.False(values.ContainsKey("shared"));
        Assert.Equal("enabled", values["engine_multithreading_mode"]);
    }

    [Fact]
    public void PoE2_upscaling_advisory_triggers_only_for_an_unmet_gpu_target()
    {
        var advisory = Poe2Advisory();
        var context = new AdvisoryContext
        {
            ConfigValues = Values(("upscale", "None")),
            Bottleneck = BottleneckType.Gpu,
            TargetMet = false,
        };

        var result = AdvisoryEvaluator.Evaluate(advisory, context);

        Assert.Equal(AdvisoryStatus.Triggered, result.Status);
        Assert.True(result.HighPriority);
        Assert.Equal("None", result.CurrentValue);
    }

    [Theory]
    [InlineData("DLSS")]
    [InlineData("FSR")]
    [InlineData("XeSS")]
    public void PoE2_upscaling_advisory_is_already_ok_when_an_upscaler_is_active(string value)
    {
        var result = AdvisoryEvaluator.Evaluate(Poe2Advisory(), new AdvisoryContext
        {
            ConfigValues = Values(("upscale", value)),
            Bottleneck = BottleneckType.Gpu,
            TargetMet = false,
        });

        Assert.Equal(AdvisoryStatus.AlreadyOk, result.Status);
    }

    [Fact]
    public void PoE2_upscaling_advisory_does_not_fire_for_a_cpu_bottleneck()
    {
        var result = AdvisoryEvaluator.Evaluate(Poe2Advisory(), new AdvisoryContext
        {
            ConfigValues = Values(("upscale", "None")),
            Bottleneck = BottleneckType.Cpu,
            TargetMet = false,
        });

        Assert.Equal(AdvisoryStatus.AlreadyOk, result.Status);
    }

    [Fact]
    public void PoE2_upscaling_advisory_does_not_fire_when_the_target_is_already_met()
    {
        var result = AdvisoryEvaluator.Evaluate(Poe2Advisory(), new AdvisoryContext
        {
            ConfigValues = Values(("upscale", "None")),
            Bottleneck = BottleneckType.Gpu,
            TargetMet = true,
        });

        Assert.Equal(AdvisoryStatus.AlreadyOk, result.Status);
    }

    [Fact]
    public void Missing_config_value_is_cannot_verify_not_a_warning()
    {
        var result = AdvisoryEvaluator.Evaluate(Poe2Advisory(), new AdvisoryContext
        {
            ConfigValues = Values(),
            Bottleneck = BottleneckType.Gpu,
            TargetMet = false,
        });

        Assert.Equal(AdvisoryStatus.CannotVerify, result.Status);
    }

    [Fact]
    public void Legacy_advisory_without_conditions_is_never_claimed_as_config_verified()
    {
        var legacy = new SettingAdvisory
        {
            Setting = "Expensive setting",
            Kind = AdvisoryKind.PoorValue,
            Recommendation = "Lower it",
        };

        var result = AdvisoryEvaluator.Evaluate(legacy, new AdvisoryContext());

        Assert.Equal(AdvisoryStatus.CannotVerify, result.Status);
        Assert.Contains("général", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Built_in_PoE2_profile_contains_the_contextual_rule_and_provenance()
    {
        var profile = Assert.Single(GameProfileRepository.LoadBuiltIn(), p => p.Name == "Path of Exile 2");
        var advisory = Assert.Single(profile.Advisories);

        Assert.Equal("poe2-upscaling-off-while-gpu-limited", advisory.Id);
        Assert.Equal(3, advisory.Conditions.Count);
        Assert.False(advisory.AutomaticApplication);
        Assert.Equal("2026-10-22", advisory.LastVerified);
        Assert.True(advisory.Sources.Count >= 2);
    }

    private static SettingAdvisory Poe2Advisory() => new()
    {
        Id = "poe2-upscaling-off-while-gpu-limited",
        Setting = "Upscaling",
        Kind = AdvisoryKind.PoorValue,
        Conditions =
        [
            new AdvisoryCondition { Key = "upscale", Operator = ConditionOperator.Equals, Value = "None" },
            new AdvisoryCondition { System = "bottleneck", Operator = ConditionOperator.Equals, Value = "Gpu" },
            new AdvisoryCondition { System = "targetMet", Operator = ConditionOperator.Equals, Value = "false" },
        ],
        PriorityWhen =
        [
            new AdvisoryCondition { System = "bottleneck", Operator = ConditionOperator.Equals, Value = "Gpu" },
        ],
        Recommendation = "Test an upscaler",
    };

    private static IReadOnlyDictionary<string, string> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
}
