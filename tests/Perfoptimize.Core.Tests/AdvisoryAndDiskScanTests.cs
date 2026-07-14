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
                Id = "test-hairworks", Setting = "HairWorks", Kind = AdvisoryKind.PoorValue,
                Conditions = [new AdvisoryCondition { Key = "HairWorksLevel", Operator = ConditionOperator.IsOn }],
                RecommendationText = "Disable", Detail = "Expensive for little gain.",
            },
        ],
        Settings =
        [
            new GameSetting { Name = "Shadows", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 4, EstimatedGainPercent = 12 },
        ],
    };

    private static AdvicePlan Advise(BottleneckType type, Dictionary<string, string>? config) =>
        RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = type },
            Game = GameWithAdvisory(),
            ConfigValues = config,
        });

    [Fact]
    public void An_active_problematic_setting_is_triggered()
    {
        var plan = Advise(BottleneckType.Gpu, new() { ["HairWorksLevel"] = "2" });

        var a = Assert.Single(plan.Advisories);
        Assert.Equal(AdvisoryStatus.Triggered, a.Status);
        Assert.Equal("2", a.CurrentValue);
    }

    [Fact]
    public void A_disabled_problematic_setting_reads_as_already_ok()
    {
        var plan = Advise(BottleneckType.Gpu, new() { ["HairWorksLevel"] = "0" });

        Assert.Equal(AdvisoryStatus.AlreadyOk, Assert.Single(plan.Advisories).Status);
    }

    [Fact]
    public void An_unreadable_setting_reads_as_cannot_verify()
    {
        var plan = Advise(BottleneckType.Gpu, new()); // no HairWorksLevel key

        Assert.Equal(AdvisoryStatus.CannotVerify, Assert.Single(plan.Advisories).Status);
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
    public void The_games_config_resolution_is_preferred_over_the_screen()
    {
        // Screen is 1080p but the game renders at 1440p (config says so): an MSAA
        // advisory gated on resolutionWidth>=2560 must fire on the GAME resolution.
        var game = new GameProfile
        {
            Name = "Res Test", Executables = ["res.exe"], Upscalers = [],
            Advisories =
            [
                new SettingAdvisory
                {
                    Id = "msaa", Setting = "MSAA", Kind = AdvisoryKind.PoorValue,
                    Conditions =
                    [
                        new AdvisoryCondition { Key = "MSAA", Operator = ConditionOperator.GreaterThan, Value = "0" },
                        new AdvisoryCondition { System = "resolutionWidth", Operator = ConditionOperator.GreaterThanOrEqual, Value = "2560" },
                    ],
                },
            ],
            Settings = [new GameSetting { Name = "Shadows", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 4 }],
        };
        var hardware = new Perfoptimize.Core.Hardware.HardwareProfile
        {
            Displays = [new Perfoptimize.Core.Hardware.DisplayInfo { WidthPixels = 1920, HeightPixels = 1080, IsPrimary = true }],
        };

        var plan = RecommendationEngine.Advise(new AdviceRequest
        {
            Analysis = new BottleneckAnalysis { Type = BottleneckType.Gpu },
            Game = game,
            Hardware = hardware,
            ConfigValues = new Dictionary<string, string>
            {
                ["MSAA"] = "4", ["resolution_width"] = "2560", ["resolution_height"] = "1440",
            },
        });

        Assert.Equal(AdvisoryStatus.Triggered, Assert.Single(plan.Advisories).Status);
    }

    [Fact]
    public void Built_in_profiles_carry_structured_advisories()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var witcher = profiles.First(p => p.Name.StartsWith("The Witcher"));
        var hairworks = witcher.Advisories.First(a => a.Setting.Contains("HairWorks"));
        Assert.NotEmpty(hairworks.Conditions);
        Assert.False(string.IsNullOrWhiteSpace(hairworks.RecommendationText));
        Assert.Equal("2026-07-14", hairworks.Evidence.LastVerified);
        // Honest default: single-curator claims are not "community validated".
        Assert.Equal(AdvisoryEvidenceLevel.CuratedCommunityClaim, hairworks.Evidence.Level);
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
