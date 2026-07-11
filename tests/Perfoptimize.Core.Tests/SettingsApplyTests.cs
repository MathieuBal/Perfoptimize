using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Tests;

public class SettingsPlannerTests
{
    private const string IniText = """
        ; test config
        [Rendering]
        ShadowQuality=3
        HairWorksLevel=0
        TextureDownscale=0
        """;

    private static GameSetting Setting(string name, string key, params string[] levels) => new()
    {
        Name = name,
        Impact = SettingImpact.Gpu,
        QualityLoss = 2,
        PerformanceGain = 3,
        Config = new SettingBinding
        {
            File = "user.settings",
            Section = "Rendering",
            Key = key,
            Levels = levels,
        },
    };

    private static GameProfile Game(params GameSetting[] settings) => new()
    {
        Name = "Test Game",
        Executables = ["testgame.exe"],
        Settings = settings,
        ConfigFiles = [new GameConfigFile { Path = "/tmp/test/user.settings", Format = ConfigFormat.Ini }],
    };

    private static Dictionary<string, string> Texts(GameProfile game, string text) =>
        new(StringComparer.OrdinalIgnoreCase) { [game.ConfigFiles[0].ResolvePath()] = text };

    [Fact]
    public void Plan_lowers_a_known_value_by_one_step()
    {
        var setting = Setting("Shadow Quality", "ShadowQuality", "0", "1", "2", "3", "4");
        var game = Game(setting);

        var change = Assert.Single(SettingsPlanner.Plan(game, [setting], Texts(game, IniText)));

        Assert.Equal(PlannedChangeStatus.Ready, change.Status);
        Assert.Equal("3", change.CurrentValue);
        Assert.Equal("2", change.NewValue);
    }

    [Fact]
    public void Plan_respects_inverted_ladders()
    {
        // TextureDownscale: 0 is the BEST quality, so the ladder lists 3,2,1,0
        // and lowering from 0 goes to 1.
        var setting = Setting("Texture Quality", "TextureDownscale", "3", "2", "1", "0");
        var game = Game(setting);

        var change = Assert.Single(SettingsPlanner.Plan(game, [setting], Texts(game, IniText)));

        Assert.Equal(PlannedChangeStatus.Ready, change.Status);
        Assert.Equal("0", change.CurrentValue);
        Assert.Equal("1", change.NewValue);
    }

    [Fact]
    public void Plan_never_goes_below_the_lowest_level()
    {
        var setting = Setting("HairWorks", "HairWorksLevel", "0", "1", "2");
        var game = Game(setting);

        var change = Assert.Single(SettingsPlanner.Plan(game, [setting], Texts(game, IniText)));

        Assert.Equal(PlannedChangeStatus.AlreadyLowest, change.Status);
    }

    [Fact]
    public void Plan_skips_values_outside_the_documented_ladder()
    {
        var setting = Setting("Shadow Quality", "ShadowQuality", "low", "medium", "high");
        var game = Game(setting);

        var change = Assert.Single(SettingsPlanner.Plan(game, [setting], Texts(game, IniText)));

        Assert.Equal(PlannedChangeStatus.UnknownCurrentValue, change.Status); // "3" not in ladder
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void Plan_reports_missing_keys_and_unbound_settings()
    {
        var missingKey = Setting("Foliage", "FoliageDistance", "0", "1", "2");
        var unbound = new GameSetting { Name = "Ray Tracing", Impact = SettingImpact.Gpu, PerformanceGain = 5 };
        var game = Game(missingKey, unbound);

        var changes = SettingsPlanner.Plan(game, [missingKey, unbound], Texts(game, IniText));

        Assert.Equal(PlannedChangeStatus.KeyNotFound, changes[0].Status);
        Assert.Equal(PlannedChangeStatus.NotSupported, changes[1].Status);
    }

    [Fact]
    public void ApplyToText_only_touches_the_planned_line()
    {
        var setting = Setting("Shadow Quality", "ShadowQuality", "0", "1", "2", "3", "4");
        var game = Game(setting);
        var change = SettingsPlanner.Plan(game, [setting], Texts(game, IniText)).Single();

        var updated = SettingsPlanner.ApplyToText(IniText, change);

        var ini = IniFile.Parse(updated);
        Assert.Equal("2", ini["Rendering"]["ShadowQuality"]);
        Assert.Equal("0", ini["Rendering"]["HairWorksLevel"]); // untouched
        Assert.Contains("; test config", updated);              // comment preserved
    }
}

public class SettingsApplierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-apply-{Guid.NewGuid():N}");
    private readonly string _configPath;
    private readonly GameProfile _game;

    public SettingsApplierTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "user.settings");
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=3\nHairWorksLevel=2");
        _game = new GameProfile
        {
            Name = "Test Game",
            Executables = ["testgame.exe"],
            ConfigFiles = [new GameConfigFile { Path = _configPath, Format = ConfigFormat.Ini }],
            Settings =
            [
                new GameSetting
                {
                    Name = "Shadow Quality", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 3,
                    Config = new SettingBinding
                        { File = "user.settings", Section = "Rendering", Key = "ShadowQuality", Levels = ["0", "1", "2", "3", "4"] },
                },
                new GameSetting
                {
                    Name = "HairWorks", Impact = SettingImpact.Gpu, QualityLoss = 1, PerformanceGain = 4,
                    Config = new SettingBinding
                        { File = "user.settings", Section = "Rendering", Key = "HairWorksLevel", Levels = ["0", "1", "2"] },
                },
            ],
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private SettingsApplier Applier() => new(new ConfigBackupService(Path.Combine(_root, "backups")));

    [Fact]
    public void Dry_run_plans_but_writes_nothing()
    {
        var original = File.ReadAllText(_configPath);

        var result = Applier().Apply(_game, _game.Settings.ToList(), write: false);

        Assert.False(result.Written);
        Assert.Empty(result.BackupPaths);
        Assert.All(result.Changes, c => Assert.Equal(PlannedChangeStatus.Ready, c.Status));
        Assert.Equal(original, File.ReadAllText(_configPath)); // untouched
    }

    [Fact]
    public void Write_backs_up_once_then_applies_every_ready_change()
    {
        var result = Applier().Apply(_game, _game.Settings.ToList(), write: true);

        Assert.True(result.Written);
        var backup = Assert.Single(result.BackupPaths);
        Assert.Equal("[Rendering]\nShadowQuality=3\nHairWorksLevel=2", File.ReadAllText(backup));

        var ini = IniFile.Parse(File.ReadAllText(_configPath));
        Assert.Equal("2", ini["Rendering"]["ShadowQuality"]);
        Assert.Equal("1", ini["Rendering"]["HairWorksLevel"]);
    }

    [Fact]
    public void Write_with_nothing_ready_creates_no_backup()
    {
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=0\nHairWorksLevel=0");

        var result = Applier().Apply(_game, _game.Settings.ToList(), write: true);

        Assert.Empty(result.BackupPaths);
        Assert.All(result.Changes, c => Assert.Equal(PlannedChangeStatus.AlreadyLowest, c.Status));
    }
}
