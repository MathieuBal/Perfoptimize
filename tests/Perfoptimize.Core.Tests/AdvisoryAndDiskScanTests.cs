using Perfoptimize.Core.Games;
using Perfoptimize.Core.Library;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class AdvisoryTests
{
    private static GameProfile GameWithAdvisory() => new()
    {
        Name = "Test Game",
        Executables = ["test.exe"],
        Upscalers = [],
        Advisories =
        [
            new SettingAdvisory
            {
                Setting = "HairWorks", Kind = AdvisoryKind.PoorValue,
                Recommendation = "Disable", Detail = "Expensive for little gain.",
                Evidence = EvidenceLevel.CommunityValidated,
            },
        ],
        Settings =
        [
            new GameSetting { Name = "Shadows", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 4, EstimatedGainPercent = 12 },
        ],
    };

    [Fact]
    public void Advisories_are_surfaced_regardless_of_the_bottleneck()
    {
        foreach (var type in new[] { BottleneckType.Gpu, BottleneckType.Cpu, BottleneckType.None })
        {
            var plan = RecommendationEngine.Advise(new AdviceRequest
            {
                Analysis = new BottleneckAnalysis { Type = type },
                Game = GameWithAdvisory(),
            });
            Assert.Contains(plan.Advisories, a => a.Setting == "HairWorks");
        }
    }

    [Fact]
    public void Advisories_appear_even_when_the_fps_target_is_already_met()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Gpu },
            Game = GameWithAdvisory(),
            Frametimes = new Perfoptimize.Core.Monitoring.Frametimes.FrametimeStatistics
            {
                FrameCount = 500, AverageFps = 120, OnePercentLowFps = 100,
                AverageFrametimeMs = 8.3, WorstFrametimeMs = 12, StutterCount = 0, StutterPercent = 0,
            },
            TargetFps = 60,
        });

        Assert.Equal(0, plan.RequiredGainPercent);
        Assert.Contains(plan.Advisories, a => a.Setting == "HairWorks");
    }

    [Fact]
    public void A_game_without_advisories_has_none()
    {
        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Gpu },
        });

        Assert.Empty(plan.Advisories);
    }

    [Fact]
    public void Built_in_profiles_carry_community_advisories()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var witcher = profiles.First(p => p.Name.StartsWith("The Witcher"));
        Assert.Contains(witcher.Advisories, a => a.Setting.Contains("HairWorks"));
        Assert.All(witcher.Advisories, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Recommendation));
            Assert.False(string.IsNullOrWhiteSpace(a.Detail));
        });
    }
}

public class MultiDiskScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-drives-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SteamLibraryRootsUnderDrives_finds_libraries_on_any_drive()
    {
        // Simulate two "drives", one with a SteamLibrary, one without.
        var driveC = Path.Combine(_root, "C");
        var driveD = Path.Combine(_root, "D");
        Directory.CreateDirectory(Path.Combine(driveD, "SteamLibrary", "steamapps"));
        Directory.CreateDirectory(Path.Combine(driveC, "Users")); // no steam library

        var roots = InstalledGameScanner.SteamLibraryRootsUnderDrives([driveC, driveD]);

        var found = Assert.Single(roots);
        Assert.Equal(Path.Combine(driveD, "SteamLibrary"), found);
    }

    [Fact]
    public void ScanSteam_reads_games_from_a_secondary_drive_library()
    {
        var driveD = Path.Combine(_root, "D");
        var library = Path.Combine(driveD, "SteamLibrary");
        var steamapps = Path.Combine(library, "steamapps");
        Directory.CreateDirectory(Path.Combine(steamapps, "common", "Elden Ring"));
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_1245620.acf"),
            "\"AppState\" { \"appid\" \"1245620\" \"name\" \"ELDEN RING\" \"installdir\" \"Elden Ring\" }");

        var found = InstalledGameScanner.SteamLibraryRootsUnderDrives([driveD]);
        var games = new InstalledGameScanner().ScanSteam(found);

        Assert.Contains(games, g => g.Name == "ELDEN RING");
    }
}

public class EpicManifestTests
{
    [Fact]
    public void Parse_reads_display_name_and_install_location()
    {
        const string json = """
            { "FormatVersion": 0, "DisplayName": "Fortnite",
              "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite",
              "AppName": "Fortnite" }
            """;

        var manifest = EpicManifest.Parse(json);

        Assert.NotNull(manifest);
        Assert.Equal("Fortnite", manifest.Value.DisplayName);
        Assert.Equal(@"C:\Program Files\Epic Games\Fortnite", manifest.Value.InstallLocation);
    }

    [Fact]
    public void Parse_of_incomplete_manifest_is_null()
    {
        Assert.Null(EpicManifest.Parse("{ \"DisplayName\": \"X\" }"));
        Assert.Null(EpicManifest.Parse("{ not json"));
    }

    [Fact]
    public void ScanEpic_discovers_games_from_manifest_items()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"perfoptimize-epic-{Guid.NewGuid():N}");
        var install = Path.Combine(dir, "SomeGame");
        Directory.CreateDirectory(install);
        try
        {
            File.WriteAllText(Path.Combine(dir, "abc.item"),
                $"{{ \"DisplayName\": \"Some Game\", \"InstallLocation\": \"{install.Replace(@"\", @"\\")}\" }}");

            var games = new InstalledGameScanner().ScanEpic(dir);

            var game = Assert.Single(games);
            Assert.Equal("Some Game", game.Name);
            Assert.Equal(GameSource.Epic, game.Source);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
