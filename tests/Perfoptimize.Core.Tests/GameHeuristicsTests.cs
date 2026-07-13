using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Tests;

public class GameHeuristicsTests
{
    [Theory]
    [InlineData("EldenRing.exe", @"D:\SteamLibrary\steamapps\common\ELDEN RING\Game\eldenring.exe")]
    [InlineData("game.exe", @"C:\Program Files\Epic Games\SomeGame\game.exe")]
    [InlineData("rpg.exe", @"C:\GOG Galaxy\Games\Some RPG\rpg.exe")]
    public void An_executable_in_a_game_folder_is_detected(string name, string path)
    {
        var result = GameHeuristics.Evaluate(new ProcessInfo(name, path));

        Assert.True(result.IsLikelyGame, string.Join("; ", result.Reasons));
        Assert.True(result.Score >= GameHeuristics.LikelyThreshold);
    }

    [Theory]
    [InlineData("steam", @"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData("discord", @"C:\Users\me\AppData\Local\Discord\app-1.0\Discord.exe")]
    [InlineData("Code", @"C:\Program Files\Microsoft VS Code\Code.exe")]
    public void Launchers_browsers_and_tools_are_not_games(string name, string path)
    {
        Assert.False(GameHeuristics.Evaluate(new ProcessInfo(name, path)).IsLikelyGame);
    }

    [Fact]
    public void A_launcher_inside_a_game_folder_is_rejected()
    {
        // Score +3 (game folder) - 3 (launcher) = 0, below the threshold.
        var result = GameHeuristics.Evaluate(new ProcessInfo(
            "GameLauncher.exe", @"D:\steamapps\common\Some Game\GameLauncher.exe"));

        Assert.False(result.IsLikelyGame);
    }

    [Fact]
    public void System_executables_are_never_games()
    {
        var result = GameHeuristics.Evaluate(new ProcessInfo(
            "dwm.exe", @"C:\Windows\System32\dwm.exe"));

        Assert.False(result.IsLikelyGame);
    }

    [Fact]
    public void An_unknown_process_without_a_path_is_not_assumed_to_be_a_game()
    {
        var result = GameHeuristics.Evaluate(new ProcessInfo("mystery", null));

        Assert.False(result.IsLikelyGame);
    }

    [Fact]
    public void Detection_carries_transparent_reasons()
    {
        var result = GameHeuristics.Evaluate(new ProcessInfo(
            "witcher3.exe", @"D:\SteamLibrary\steamapps\common\The Witcher 3\bin\x64\witcher3.exe"));

        Assert.True(result.IsLikelyGame);
        Assert.NotEmpty(result.Reasons);
        Assert.Contains(result.Reasons, r => r.Contains("game folder", StringComparison.OrdinalIgnoreCase));
    }
}
