using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Tests;

public class GameDetectionTests
{
    [Fact]
    public void BuiltIn_profiles_load_from_embedded_resources()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        Assert.True(profiles.Count >= 5);
        Assert.All(profiles, p =>
        {
            Assert.NotEqual("Unknown", p.Name);
            Assert.NotEmpty(p.Executables);
            Assert.NotEmpty(p.Settings);
        });
        Assert.Contains(profiles, p => p.Name == "Cyberpunk 2077");
    }

    [Theory]
    [InlineData("Cyberpunk2077", "Cyberpunk 2077")]     // Process.ProcessName has no extension
    [InlineData("cyberpunk2077.exe", "Cyberpunk 2077")] // case-insensitive, extension tolerated
    [InlineData("eldenring", "Elden Ring")]
    [InlineData("PathOfExile_x64Steam", "Path of Exile")]
    public void Match_recognizes_known_processes(string processName, string expectedGame)
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var match = RunningGameDetector.Match(processName, profiles);

        Assert.Equal(expectedGame, match?.Name);
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("cyberpunk2078")]
    [InlineData("")]
    public void Match_returns_null_for_unknown_processes(string processName)
    {
        Assert.Null(RunningGameDetector.Match(processName, GameProfileRepository.LoadBuiltIn()));
    }

    [Fact]
    public void PathOfExile2_profile_is_present_and_carries_advisories()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var poe2 = Assert.Single(profiles, p => p.Name == "Path of Exile 2");
        Assert.Equal("Path of Exile 2", poe2.PathHint);
        Assert.Contains(poe2.ConfigFiles, c => c.Path.Contains("poe2_production_Config.ini"));
        Assert.NotEmpty(poe2.Advisories);
        Assert.All(poe2.Advisories, a => Assert.Equal("2026-07-14", a.Evidence.LastVerified));
    }

    [Fact]
    public void Match_uses_the_install_path_to_tell_PoE2_from_PoE1()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        // Same executable name — only the folder differs.
        var poe2 = RunningGameDetector.Match(
            "PathOfExile", @"D:\SteamLibrary\steamapps\common\Path of Exile 2\PathOfExile.exe", profiles);
        var poe1 = RunningGameDetector.Match(
            "PathOfExile", @"C:\Program Files\Grinding Gear Games\Path of Exile\PathOfExile.exe", profiles);

        Assert.Equal("Path of Exile 2", poe2?.Name);
        Assert.Equal("Path of Exile", poe1?.Name);
    }

    [Fact]
    public void Match_without_a_path_falls_back_to_the_base_game()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        // No path evidence (protected process): default to the base game, not a variant.
        var match = RunningGameDetector.Match("PathOfExile", executablePath: null, profiles);

        Assert.Equal("Path of Exile", match?.Name);
    }

    [Fact]
    public void Parse_reads_the_documented_json_schema()
    {
        const string json = """
            {
              "name": "Example Game",
              "executables": ["examplegame.exe"],
              "settings": [
                { "name": "Shadows", "impact": "gpu", "qualityLoss": 3, "performanceGain": 4 }
              ]
            }
            """;

        var profile = GameProfileRepository.Parse(json);

        Assert.NotNull(profile);
        Assert.Equal("Example Game", profile.Name);
        Assert.Equal(SettingImpact.Gpu, profile.Settings[0].Impact);
        Assert.Equal(4, profile.Settings[0].PerformanceGain);
    }

    [Fact]
    public void Parse_returns_null_for_invalid_json()
    {
        Assert.Null(GameProfileRepository.Parse("{ not json"));
    }
}
