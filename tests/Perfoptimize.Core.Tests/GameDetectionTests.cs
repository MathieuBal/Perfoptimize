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
