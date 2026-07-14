using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Library;

namespace Perfoptimize.Core.Tests;

public class SteamVdfTests
{
    [Fact]
    public void ParseLibraryFolders_reads_every_library_path()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                }
            }
            """;

        var paths = SteamVdf.ParseLibraryFolders(vdf);

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], paths);
    }

    [Fact]
    public void ParseAppManifest_reads_name_and_installdir()
    {
        const string acf = """
            "AppState"
            {
                "appid"		"292030"
                "name"		"The Witcher 3: Wild Hunt"
                "installdir"		"The Witcher 3"
                "StateFlags"		"4"
            }
            """;

        var app = SteamVdf.ParseAppManifest(acf);

        Assert.NotNull(app);
        Assert.Equal("292030", app.Value.AppId);
        Assert.Equal("The Witcher 3: Wild Hunt", app.Value.Name);
        Assert.Equal("The Witcher 3", app.Value.InstallDir);
    }

    [Fact]
    public void ParseAppManifest_of_incomplete_data_is_null()
    {
        Assert.Null(SteamVdf.ParseAppManifest("\"AppState\" { \"appid\" \"1\" }"));
    }
}

public class InstalledGameScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-steam-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string MakeSteamRoot(params (string appId, string name, string installDir)[] apps)
    {
        var steamRoot = Path.Combine(_root, "Steam");
        var steamapps = Path.Combine(steamRoot, "steamapps");
        Directory.CreateDirectory(steamapps);
        File.WriteAllText(Path.Combine(steamapps, "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{steamRoot.Replace(@"\", @"\\")}\" }} }}");
        foreach (var (appId, name, installDir) in apps)
        {
            Directory.CreateDirectory(Path.Combine(steamapps, "common", installDir));
            File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{appId}.acf"),
                $"\"AppState\" {{ \"appid\" \"{appId}\" \"name\" \"{name}\" \"installdir\" \"{installDir}\" }}");
        }
        return steamRoot;
    }

    [Fact]
    public void ScanSteam_discovers_installed_games_from_manifests()
    {
        var steamRoot = MakeSteamRoot(
            ("292030", "The Witcher 3: Wild Hunt", "The Witcher 3"),
            ("238960", "Path of Exile", "Path of Exile"));

        var games = new InstalledGameScanner().ScanSteam([steamRoot]);

        Assert.Equal(2, games.Count);
        Assert.Contains(games, g => g.Name == "Path of Exile" && g.Source == GameSource.Steam);
        var witcher = games.First(g => g.Name.StartsWith("The Witcher"));
        Assert.True(Directory.Exists(witcher.InstallDir));
        Assert.Equal("292030", witcher.AppId);
    }

    [Fact]
    public void ScanSteam_ignores_manifests_whose_install_folder_is_missing()
    {
        var steamRoot = MakeSteamRoot(("1", "Ghost Game", "Ghost"));
        // Remove the common/Ghost folder to simulate a partially-uninstalled game.
        Directory.Delete(Path.Combine(steamRoot, "steamapps", "common", "Ghost"), recursive: true);

        Assert.Empty(new InstalledGameScanner().ScanSteam([steamRoot]));
    }

    [Fact]
    public void ScanSteam_of_a_nonexistent_root_returns_empty()
    {
        Assert.Empty(new InstalledGameScanner().ScanSteam([Path.Combine(_root, "nope")]));
    }
}

public class GameConfigScannerLogicTests
{
    [Theory]
    [InlineData("The Witcher 3", "The Witcher 3: Wild Hunt", true)]
    [InlineData("Path of Exile", "Path of Exile", true)]
    [InlineData("CD Projekt Red", "The Witcher 3", false)]
    [InlineData("Cyberpunk 2077", "Cyberpunk 2077", true)]
    public void FolderMatchesGame_matches_on_normalized_names(string folder, string game, bool expected)
    {
        Assert.Equal(expected, GameConfigScanner.FolderMatchesGame(folder, game));
    }

    [Theory]
    [InlineData("user.settings", true)]
    [InlineData("GameUserSettings.ini", true)]
    [InlineData("config.ini", true)]
    [InlineData("settings.xml", true)]
    [InlineData("readme.txt", false)]
    [InlineData("game.exe", false)]
    [InlineData("save01.dat", false)]
    public void LooksLikeConfigFile_recognizes_config_files(string name, bool expected)
    {
        Assert.Equal(expected, GameConfigScanner.LooksLikeConfigFile(name));
    }

    [Fact]
    public void ExtractGraphicsSettings_keeps_only_graphics_keys()
    {
        var ini = IniFile.Parse("""
            [Rendering]
            ShadowQuality=3
            TextureQuality=2
            PlayerName=Bob
            [Audio]
            MasterVolume=80
            """);

        var settings = GameConfigScanner.ExtractGraphicsSettings(ini);

        Assert.Contains(settings, s => s.Key == "ShadowQuality" && s.Value == "3");
        Assert.Contains(settings, s => s.Key == "TextureQuality");
        Assert.DoesNotContain(settings, s => s.Key == "PlayerName");
        Assert.DoesNotContain(settings, s => s.Key == "MasterVolume");
    }
}

public class GameConfigScannerIoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-cfg-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FindConfigs_reads_an_ini_in_the_install_directory()
    {
        var installDir = Path.Combine(_root, "SomeGame");
        Directory.CreateDirectory(Path.Combine(installDir, "bin"));
        File.WriteAllText(Path.Combine(installDir, "bin", "GameUserSettings.ini"),
            "[ScalabilityGroups]\nsg.ShadowQuality=2\nsg.TextureQuality=3\nsg.ViewDistanceQuality=1");
        File.WriteAllText(Path.Combine(installDir, "readme.txt"), "not a config");

        var configs = new GameConfigScanner().FindConfigs("Some Game", installDir);

        var config = Assert.Single(configs);
        Assert.Equal("Ini", config.Format);
        Assert.Contains(config.GraphicsSettings, s => s.Key == "sg.ShadowQuality");
        Assert.Equal(3, config.TotalKeys);
    }

    [Fact]
    public void FindConfigs_of_a_game_without_config_files_returns_empty()
    {
        var installDir = Path.Combine(_root, "Empty");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "game.exe"), "binary");

        Assert.Empty(new GameConfigScanner().FindConfigs("Empty", installDir));
    }
}
