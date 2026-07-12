using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Monitoring.Frametimes;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;

namespace Perfoptimize.Core.Tests;

public class BottleneckTuningTests
{
    [Theory]
    [InlineData(BottleneckType.Gpu, true)]
    [InlineData(BottleneckType.Cpu, true)]
    [InlineData(BottleneckType.Vram, true)]
    [InlineData(BottleneckType.Ram, true)]
    [InlineData(BottleneckType.Thermal, true)]
    [InlineData(BottleneckType.ExternalGpuLoad, false)]
    [InlineData(BottleneckType.ExternalCpuLoad, false)]
    [InlineData(BottleneckType.GpuHardwareLimit, false)]
    [InlineData(BottleneckType.None, false)]
    [InlineData(BottleneckType.Unknown, false)]
    public void Only_game_addressable_verdicts_are_tunable(BottleneckType type, bool expected)
    {
        Assert.Equal(expected, BottleneckTuning.IsTunable(type));
        if (!expected)
            Assert.False(string.IsNullOrWhiteSpace(BottleneckTuning.RefusalReason(type)));
    }
}

public class SurgicalRollbackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-surgical-{Guid.NewGuid():N}");
    private readonly string _configPath;
    private readonly GameProfile _game;
    private readonly GameSetting _setting;

    public SurgicalRollbackTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "user.settings");
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=3\nResolution=1920x1080");
        _setting = new GameSetting
        {
            Name = "Shadow Quality", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 3,
            Config = new SettingBinding
                { File = "user.settings", Section = "Rendering", Key = "ShadowQuality", Levels = ["0", "1", "2", "3", "4"] },
        };
        _game = new GameProfile
        {
            Name = "Surgical Game",
            Executables = ["surgical.exe"],
            ConfigFiles = [new GameConfigFile { Path = _configPath, Format = ConfigFormat.Ini }],
            Settings = [_setting],
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static FrametimeStatistics Fps(double average) => new()
    {
        FrameCount = 500, AverageFps = average, OnePercentLowFps = average * 0.85,
        AverageFrametimeMs = 1000 / average, WorstFrametimeMs = 50, StutterCount = 0, StutterPercent = 0,
    };

    [Fact]
    public async Task Rollback_reverts_only_the_tested_key_and_keeps_the_games_own_edits()
    {
        var loop = new OptimizationLoop(
            new SettingsApplier(new ConfigBackupService(Path.Combine(_root, "backups"))));

        var outcome = await loop.RunOnceAsync(_game, _setting, Fps(50), () =>
        {
            // Between apply and re-measure the game rewrote its config: it
            // changed the resolution. A full-backup restore would wipe that.
            var text = File.ReadAllText(_configPath);
            text = IniFile.SetValue(text, "Rendering", "Resolution", "2560x1440");
            File.WriteAllText(_configPath, text);
            return Task.FromResult<FrametimeStatistics?>(Fps(50.2)); // no benefit → rollback
        });

        Assert.NotNull(outcome);
        Assert.False(outcome.Kept);
        var ini = IniFile.Parse(File.ReadAllText(_configPath));
        Assert.Equal("3", ini["Rendering"]["ShadowQuality"]);        // tested key reverted
        Assert.Equal("2560x1440", ini["Rendering"]["Resolution"]);   // game's edit preserved
    }

    [Fact]
    public void TryRestoreKey_returns_false_for_a_missing_file()
    {
        Assert.False(SurgicalRestore.TryRestoreKey(
            Path.Combine(_root, "missing.ini"), "A", "x", "1"));
    }
}

public class StoredPlanMapperTests
{
    private static readonly GameProfile Game = new()
    {
        Name = "Mapper Game",
        Executables = ["mapper.exe"],
        ConfigFiles = [new GameConfigFile { Path = "/tmp/mapper.ini", Format = ConfigFormat.Ini }],
        Settings =
        [
            new GameSetting
            {
                Name = "Shadows", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 3,
                Config = new SettingBinding { File = "mapper.ini", Section = "G", Key = "shadows", Levels = ["0", "1", "2"] },
            },
        ],
    };

    [Fact]
    public void Modern_plans_use_snapshots_only_and_never_fall_back_to_names()
    {
        List<StoredRecommendation> plan =
        [
            new()
            {
                Title = "Mapper Game: lower \"Shadows\" by one step", SettingName = "Shadows",
                ConfigFile = "mapper.ini", ConfigSection = "G", ConfigKey = "shadows",
                ObservedValue = "2", ProposedValue = "1",
            },
            // Menu-only advice from the same session: same name as a bound
            // profile setting, but NO snapshot — must not be resolved by name.
            new() { Title = "Mapper Game: lower \"Shadows\" in the menu", SettingName = "Shadows" },
        ];

        var settings = StoredPlanMapper.ToSettings(Game, plan);

        var setting = Assert.Single(settings);
        // The exact snapshot, not the profile's ladder: [proposed, observed].
        Assert.Equal(["1", "2"], setting.Config!.Levels);
    }

    [Fact]
    public void Legacy_plans_without_any_snapshot_still_match_by_name()
    {
        List<StoredRecommendation> plan =
        [
            new() { Title = "old", SettingName = "Shadows" },
        ];

        var settings = StoredPlanMapper.ToSettings(Game, plan);

        var setting = Assert.Single(settings);
        Assert.Equal(["0", "1", "2"], setting.Config!.Levels); // profile setting
    }
}
