using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class AdvisoryEvaluatorTests
{
    private static SettingAdvisory Msaa() => new()
    {
        Id = "gta5-msaa", Setting = "MSAA", Kind = AdvisoryKind.PoorValue,
        Conditions =
        [
            new AdvisoryCondition { Key = "MSAA", Operator = ConditionOperator.GreaterThan, Value = "0" },
            new AdvisoryCondition { System = "resolutionWidth", Operator = ConditionOperator.GreaterThanOrEqual, Value = "2560" },
        ],
        PriorityWhen = [new AdvisoryCondition { System = "bottleneck", Operator = ConditionOperator.Equals, Value = "Gpu" }],
    };

    private static SettingAdvisory CrowdDensity() => new()
    {
        Id = "cp-crowd", Setting = "Crowd Density", Kind = AdvisoryKind.Stutter,
        Conditions = [new AdvisoryCondition { Key = "CrowdDensity", Operator = ConditionOperator.GreaterThanOrEqual, Value = "2" }],
        PriorityWhen = [new AdvisoryCondition { System = "bottleneck", Operator = ConditionOperator.Equals, Value = "Cpu" }],
    };

    private static AdvisoryContext Ctx(
        Dictionary<string, string>? config = null, int? width = null, BottleneckType bottleneck = BottleneckType.Unknown)
        => new()
        {
            ConfigValues = config ?? new Dictionary<string, string>(),
            ResolutionWidth = width,
            Bottleneck = bottleneck,
        };

    [Fact]
    public void Msaa_on_at_1440p_is_triggered()
    {
        var e = AdvisoryEvaluator.Evaluate(Msaa(), Ctx(new() { ["MSAA"] = "4" }, width: 2560));
        Assert.Equal(AdvisoryStatus.Triggered, e.Status);
    }

    [Fact]
    public void Msaa_on_at_1080p_is_not_triggered()
    {
        // The setting IS on, but harmless at 1080p — no alert.
        var e = AdvisoryEvaluator.Evaluate(Msaa(), Ctx(new() { ["MSAA"] = "4" }, width: 1920));
        Assert.Equal(AdvisoryStatus.AlreadyOk, e.Status);
    }

    [Fact]
    public void Msaa_off_is_already_ok()
    {
        var e = AdvisoryEvaluator.Evaluate(Msaa(), Ctx(new() { ["MSAA"] = "0" }, width: 2560));
        Assert.Equal(AdvisoryStatus.AlreadyOk, e.Status);
    }

    [Fact]
    public void Msaa_triggered_and_gpu_bound_is_high_priority()
    {
        var e = AdvisoryEvaluator.Evaluate(Msaa(),
            Ctx(new() { ["MSAA"] = "4" }, width: 2560, bottleneck: BottleneckType.Gpu));
        Assert.Equal(AdvisoryStatus.Triggered, e.Status);
        Assert.True(e.HighPriority);
    }

    [Fact]
    public void Crowd_density_high_with_cpu_bottleneck_is_high_priority()
    {
        var e = AdvisoryEvaluator.Evaluate(CrowdDensity(),
            Ctx(new() { ["CrowdDensity"] = "2" }, bottleneck: BottleneckType.Cpu));
        Assert.Equal(AdvisoryStatus.Triggered, e.Status);
        Assert.True(e.HighPriority);
    }

    [Fact]
    public void Crowd_density_high_with_gpu_bottleneck_is_info_only()
    {
        var e = AdvisoryEvaluator.Evaluate(CrowdDensity(),
            Ctx(new() { ["CrowdDensity"] = "2" }, bottleneck: BottleneckType.Gpu));
        Assert.Equal(AdvisoryStatus.Triggered, e.Status);
        Assert.False(e.HighPriority);
    }

    [Fact]
    public void Missing_config_value_cannot_be_verified()
    {
        var e = AdvisoryEvaluator.Evaluate(Msaa(), Ctx(new(), width: 2560));
        Assert.Equal(AdvisoryStatus.CannotVerify, e.Status);
    }

    [Fact]
    public void An_active_setting_with_unknown_resolution_is_not_claimed_already_ok()
    {
        // MSAA is ON, but we don't know the game's resolution: we must NOT be
        // optimistic and say "already ok" — it is genuinely unverifiable.
        var e = AdvisoryEvaluator.Evaluate(Msaa(), Ctx(new() { ["MSAA"] = "4" }, width: null));

        Assert.Equal(AdvisoryStatus.CannotVerify, e.Status);
    }

    [Fact]
    public void Already_ok_requires_positive_proof_a_setting_is_off()
    {
        // Setting positively OFF is proof, even with resolution unknown.
        var e = AdvisoryEvaluator.Evaluate(Msaa(), Ctx(new() { ["MSAA"] = "0" }, width: null));

        Assert.Equal(AdvisoryStatus.AlreadyOk, e.Status);
    }

    [Fact]
    public void A_section_qualified_condition_reads_the_right_key()
    {
        var advisory = new SettingAdvisory
        {
            Id = "q", Setting = "Shadow Quality", Kind = AdvisoryKind.PoorValue,
            Conditions = [new AdvisoryCondition
            {
                Section = "Shadows", Key = "quality",
                Operator = ConditionOperator.GreaterThanOrEqual, Value = "3",
            }],
        };
        // Two sections carry "quality"; only the qualified one is exposed.
        var config = ConfigValueReader.Read("[Shadows]\nquality=3\n[Textures]\nquality=1", "ini");

        var e = AdvisoryEvaluator.Evaluate(advisory,
            new AdvisoryContext { ConfigValues = config });

        Assert.Equal(AdvisoryStatus.Triggered, e.Status);
        Assert.Equal("3", e.CurrentValue);
    }

    [Fact]
    public void EvaluateAll_orders_triggered_and_priority_first()
    {
        var config = new Dictionary<string, string> { ["MSAA"] = "4", ["CrowdDensity"] = "0" };
        var results = AdvisoryEvaluator.EvaluateAll(
            [CrowdDensity(), Msaa()],
            Ctx(config, width: 2560, bottleneck: BottleneckType.Gpu));

        // MSAA triggered+priority should sort before the already-ok crowd density.
        Assert.Equal("MSAA", results[0].Advisory.Setting);
        Assert.Equal(AdvisoryStatus.Triggered, results[0].Status);
    }
}

public class ConfigValueReaderTests
{
    [Fact]
    public void Ini_values_are_exposed_by_key_and_by_section_key()
    {
        var values = ConfigValueReader.Read("[Rendering]\nHairWorksLevel=2\nShadowQuality=3", "ini");

        Assert.Equal("2", values["HairWorksLevel"]);
        Assert.Equal("2", values["Rendering.HairWorksLevel"]);
        Assert.Equal("3", values["ShadowQuality"]);
    }

    [Fact]
    public void Json_values_are_exposed_by_leaf_and_dotted_path()
    {
        var values = ConfigValueReader.Read(
            """{ "graphics": { "CrowdDensity": 2, "DLSS": true } }""", "json");

        Assert.Equal("2", values["CrowdDensity"]);
        Assert.Equal("2", values["graphics.CrowdDensity"]);
        Assert.Equal("true", values["DLSS"]);
    }

    [Fact]
    public void Xml_value_attributes_are_exposed_by_element_name()
    {
        // GTA-style settings.xml
        var values = ConfigValueReader.Read(
            "<Settings><graphics><MSAA value=\"4\" /><FXAA value=\"true\" /></graphics></Settings>", "xml");

        Assert.Equal("4", values["MSAA"]);
        Assert.Equal("true", values["FXAA"]);
    }

    [Fact]
    public void Malformed_config_does_not_throw()
    {
        Assert.Empty(ConfigValueReader.Read("{ not json", "json"));
        Assert.Empty(ConfigValueReader.Read("<broken", "xml"));
    }

    [Fact]
    public void An_ambiguous_bare_key_is_dropped_but_qualified_keys_remain()
    {
        // "quality" means different things in two sections: the bare alias is unsafe.
        var values = ConfigValueReader.Read(
            "[Shadows]\nquality=1\n[Textures]\nquality=3", "ini");

        Assert.False(values.ContainsKey("quality"));   // ambiguous → not exposed bare
        Assert.Equal("1", values["Shadows.quality"]);  // qualified forms always safe
        Assert.Equal("3", values["Textures.quality"]);
    }

    [Fact]
    public void A_bare_key_with_one_consistent_value_survives()
    {
        var values = ConfigValueReader.Read(
            "[A]\nvsync=1\n[B]\nvsync=1", "ini");

        Assert.Equal("1", values["vsync"]);
    }
}