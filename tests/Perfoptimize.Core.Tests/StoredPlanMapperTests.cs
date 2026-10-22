using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;

namespace Perfoptimize.Core.Tests;

public class StoredPlanSnapshotTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"perfoptimize-plan-{Guid.NewGuid():N}");

    public StoredPlanSnapshotTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Snapshot_records_the_exact_ready_edit_seen_during_analysis()
    {
        var configPath = Path.Combine(_root, "user.ini");
        File.WriteAllText(configPath, "[Rendering]\nShadowQuality=4");
        var setting = new GameSetting
        {
            Name = "Shadow Quality",
            Impact = SettingImpact.Gpu,
            QualityLoss = 1,
            PerformanceGain = 3,
            EstimatedGainPercent = 8,
            Config = new SettingBinding
            {
                File = "user.ini",
                Section = "Rendering",
                Key = "ShadowQuality",
                Levels = ["0", "1", "2", "3", "4"],
            },
        };
        var game = new GameProfile
        {
            Name = "Snapshot Game",
            Executables = ["snapshot.exe"],
            ConfigFiles = [new GameConfigFile { Path = configPath, Format = ConfigFormat.Ini }],
            Settings = [setting],
        };
        var plan = new AdvicePlan
        {
            Recommendations =
            [
                new Recommendation
                {
                    Title = "Lower shadows",
                    Reason = "Test recommendation",
                    Setting = setting,
                    ExpectedGainPercent = 8,
                },
            ],
        };

        var stored = Assert.Single(StoredPlanMapper.Snapshot(plan, game));

        Assert.Equal("user.ini", stored.ConfigFile);
        Assert.Equal("Rendering", stored.ConfigSection);
        Assert.Equal("ShadowQuality", stored.ConfigKey);
        Assert.Equal("4", stored.ObservedValue);
        Assert.Equal("3", stored.ProposedValue);

        var exact = Assert.Single(StoredPlanMapper.ToExactSettings([stored]));
        Assert.Equal(new[] { "3", "4" }, exact.Config!.Levels);
    }

    [Fact]
    public void Snapshot_never_invents_an_exact_edit_when_config_cannot_be_verified()
    {
        var configPath = Path.Combine(_root, "missing-key.ini");
        File.WriteAllText(configPath, "[Rendering]\nOtherSetting=4");
        var setting = new GameSetting
        {
            Name = "Shadow Quality",
            Impact = SettingImpact.Gpu,
            QualityLoss = 1,
            PerformanceGain = 3,
            Config = new SettingBinding
            {
                File = "missing-key.ini",
                Section = "Rendering",
                Key = "ShadowQuality",
                Levels = ["0", "1", "2", "3", "4"],
            },
        };
        var game = new GameProfile
        {
            Name = "Unverified Game",
            Executables = ["unverified.exe"],
            ConfigFiles = [new GameConfigFile { Path = configPath, Format = ConfigFormat.Ini }],
            Settings = [setting],
        };
        var plan = new AdvicePlan
        {
            Recommendations =
            [
                new Recommendation
                {
                    Title = "Lower shadows",
                    Reason = "Test recommendation",
                    Setting = setting,
                },
            ],
        };

        var stored = Assert.Single(StoredPlanMapper.Snapshot(plan, game));

        Assert.Null(stored.ObservedValue);
        Assert.Null(stored.ProposedValue);
        Assert.Empty(StoredPlanMapper.ToExactSettings([stored]));
    }

    [Fact]
    public void Exact_mapper_ignores_menu_only_recommendations_in_a_mixed_plan()
    {
        var exact = new StoredRecommendation
        {
            Title = "Exact",
            SettingName = "Shadows",
            ConfigFile = "user.ini",
            ConfigSection = "Rendering",
            ConfigKey = "ShadowQuality",
            ObservedValue = "4",
            ProposedValue = "3",
        };
        var menuOnly = new StoredRecommendation
        {
            Title = "Use upscaling",
            SettingName = "Upscaler",
        };

        var settings = StoredPlanMapper.ToExactSettings([exact, menuOnly]);

        Assert.Single(settings);
        Assert.Equal("Shadows", settings[0].Name);
    }
}
