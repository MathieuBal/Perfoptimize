using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Tests;

public class ExactPlanApplyTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"perfoptimize-exact-apply-{Guid.NewGuid():N}");
    private readonly string _configPath;

    public ExactPlanApplyTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "user.ini");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Exact_plan_refuses_the_whole_write_when_one_snapshot_is_stale()
    {
        const string original = "[Rendering]\nShadowQuality=4\nEffectsQuality=custom";
        File.WriteAllText(_configPath, original);
        var game = Game(
            Setting("Shadow Quality", "ShadowQuality", ["3", "4"]),
            Setting("Effects Quality", "EffectsQuality", ["2", "3"]));
        var backups = Path.Combine(_root, "backups");
        var applier = new SettingsApplier(new ConfigBackupService(backups));

        var result = applier.Apply(game, game.Settings, write: true, requireAllReady: true);

        Assert.False(result.Written);
        Assert.Empty(result.BackupPaths);
        Assert.Equal(original, File.ReadAllText(_configPath));
        Assert.Contains(result.Changes, c =>
            c.Setting.Name == "Shadow Quality" && c.Status == PlannedChangeStatus.Ready);
        Assert.Contains(result.Changes, c =>
            c.Setting.Name == "Effects Quality" && c.Status == PlannedChangeStatus.UnknownCurrentValue);
        Assert.False(Directory.Exists(backups));
    }

    [Fact]
    public void Exact_plan_writes_every_ready_snapshot_and_reports_the_created_backup()
    {
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=4\nEffectsQuality=3");
        var game = Game(
            Setting("Shadow Quality", "ShadowQuality", ["3", "4"]),
            Setting("Effects Quality", "EffectsQuality", ["2", "3"]));
        var applier = new SettingsApplier(new ConfigBackupService(Path.Combine(_root, "backups")));

        var result = applier.Apply(game, game.Settings, write: true, requireAllReady: true);

        Assert.True(result.Written);
        Assert.Single(result.BackupPaths); // both edits share the same config file
        var ini = IniFile.Parse(File.ReadAllText(_configPath));
        Assert.Equal("3", ini["Rendering"]["ShadowQuality"]);
        Assert.Equal("2", ini["Rendering"]["EffectsQuality"]);
        Assert.All(result.Changes, c => Assert.Equal(PlannedChangeStatus.Ready, c.Status));
    }

    [Fact]
    public void A_write_with_nothing_ready_is_not_reported_as_written()
    {
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=3");
        var game = Game(Setting("Shadow Quality", "ShadowQuality", ["3", "4"]));
        var applier = new SettingsApplier(new ConfigBackupService(Path.Combine(_root, "backups")));

        var result = applier.Apply(game, game.Settings, write: true);

        Assert.False(result.Written);
        Assert.Empty(result.BackupPaths);
        Assert.Equal(PlannedChangeStatus.AlreadyLowest, Assert.Single(result.Changes).Status);
    }

    private GameProfile Game(params GameSetting[] settings) => new()
    {
        Name = "Exact Plan Test Game",
        Executables = ["exact-plan-test.exe"],
        ConfigFiles = [new GameConfigFile { Path = _configPath, Format = ConfigFormat.Ini }],
        Settings = settings,
    };

    private static GameSetting Setting(string name, string key, IReadOnlyList<string> levels) => new()
    {
        Name = name,
        Impact = SettingImpact.Gpu,
        Action = SettingAction.Lower,
        Config = new SettingBinding
        {
            File = "user.ini",
            Section = "Rendering",
            Key = key,
            Levels = levels,
        },
    };
}
