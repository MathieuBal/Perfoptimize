using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class SettingsGlossaryTests
{
    [Fact]
    public void A_cpu_bottleneck_steers_towards_dx12_or_vulkan()
    {
        var advice = SettingsGlossary.RecommendRenderer(BottleneckType.Cpu);

        Assert.Equal("dx12", advice.OptionId);
        Assert.Contains("processeur", advice.Text);
    }

    [Fact]
    public void A_gpu_bottleneck_says_the_api_barely_changes_gpu_load()
    {
        var advice = SettingsGlossary.RecommendRenderer(BottleneckType.Gpu);
        Assert.Contains("graphique", advice.Text);
    }

    [Fact]
    public void The_three_renderers_are_all_described()
    {
        var ids = SettingsGlossary.Renderers.Select(r => r.Id).ToList();
        Assert.Contains("dx11", ids);
        Assert.Contains("dx12", ids);
        Assert.Contains("vulkan", ids);
    }

    [Theory]
    [InlineData("Ray Tracing", "raytracing")]
    [InlineData("Screen Space Reflections", "ssr")]
    [InlineData("Crowd Density", "crowd")]
    [InlineData("Texture Quality", "textures")]
    [InlineData("Shadows", "shadows")]
    public void A_game_setting_name_matches_the_right_glossary_entry(string settingName, string expectedId)
    {
        var entry = SettingsGlossary.Explain(settingName);
        Assert.NotNull(entry);
        Assert.Equal(expectedId, entry.Id);
    }

    [Fact]
    public void An_unknown_setting_has_no_explanation()
    {
        Assert.Null(SettingsGlossary.Explain("Some Proprietary Toggle XYZ"));
    }
}
