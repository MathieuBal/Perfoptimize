using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Tests;

public class PathOfExile2DetectionTests
{
    [Fact]
    public void Built_in_profile_is_present_and_uses_the_PoE2_config_path()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var poe2 = Assert.Single(profiles, p => p.Name == "Path of Exile 2");
        Assert.Equal("Path of Exile 2", poe2.PathHint);
        Assert.Contains(poe2.ConfigFiles, c =>
            c.Path.Contains("Path of Exile 2", StringComparison.OrdinalIgnoreCase) &&
            c.Path.Contains("poe2_production_Config.ini", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(poe2.Settings, s => s.Name == "Engine Multithreading" && s.Action == SettingAction.KeepEnabled);
    }

    [Theory]
    [InlineData("PathOfExile")]
    [InlineData("PathOfExile.exe")]
    [InlineData("PathOfExileSteam")]
    [InlineData("PathOfExile_x64Steam.exe")]
    public void Shared_executable_is_disambiguated_by_the_install_path(string executable)
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var poe2 = RunningGameDetector.Match(
            executable,
            @"D:\SteamLibrary\steamapps\common\Path of Exile 2\PathOfExile.exe",
            profiles);
        var poe1 = RunningGameDetector.Match(
            executable,
            @"C:\Program Files\Grinding Gear Games\Path of Exile\PathOfExile.exe",
            profiles);

        Assert.Equal("Path of Exile 2", poe2?.Name);
        Assert.Equal("Path of Exile", poe1?.Name);
    }

    [Fact]
    public void Ambiguous_executable_without_path_falls_back_to_the_base_game()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var match = RunningGameDetector.Match("PathOfExile", executablePath: null, profiles);

        Assert.Equal("Path of Exile", match?.Name);
    }

    [Fact]
    public void Unrelated_games_keep_name_only_matching()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var match = RunningGameDetector.Match("Cyberpunk2077", executablePath: null, profiles);

        Assert.Equal("Cyberpunk 2077", match?.Name);
    }
}
