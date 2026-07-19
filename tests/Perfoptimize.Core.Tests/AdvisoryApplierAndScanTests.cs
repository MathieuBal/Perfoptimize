using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Library;
using Perfoptimize.Core.Monitoring.Frametimes;

namespace Perfoptimize.Core.Tests;

public class AdvisoryApplierTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"perfoptimize-apply-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private (GameProfile Game, string ConfigPath) GameWith(string iniContent)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.ini");
        File.WriteAllText(path, iniContent);
        var game = new GameProfile
        {
            Name = "Apply Test",
            Executables = ["test.exe"],
            ConfigFiles = [new GameConfigFile { Path = path, Format = ConfigFormat.Ini }],
            Settings = [new GameSetting { Name = "X", Impact = SettingImpact.Gpu }],
        };
        return (game, path);
    }

    private static SettingAdvisory Advisory(string key, string target) => new()
    {
        Id = "adv", Setting = "Dynamic resolution", Kind = AdvisoryKind.PoorValue,
        Conditions = [new AdvisoryCondition { Key = key, Operator = ConditionOperator.IsOn }],
        Recommendation = new AdvisoryRecommendation { Action = SettingAction.Disable, TargetValue = target },
    };

    private static AdvisoryApplier NewApplier() =>
        new(new ConfigBackupService(Path.Combine(Path.GetTempPath(), $"perfoptimize-bk-{Guid.NewGuid():N}")));

    [Fact]
    public void Preview_reports_the_concrete_change_without_writing()
    {
        var (game, path) = GameWith("[DISPLAY]\nuse_dynamic_resolution=true\n");

        var result = NewApplier().Apply(game, [Advisory("use_dynamic_resolution", "false")], write: false);

        var change = Assert.Single(result.Changes);
        Assert.Equal(AdvisoryChangeStatus.Ready, change.Status);
        Assert.Equal("true", change.CurrentValue);
        Assert.Equal("false", change.NewValue);
        // Nothing written on a preview.
        Assert.Contains("use_dynamic_resolution=true", File.ReadAllText(path));
    }

    [Fact]
    public void Apply_writes_then_verifies_the_value_on_disk()
    {
        var (game, path) = GameWith("[DISPLAY]\nuse_dynamic_resolution=true\n");

        var result = NewApplier().Apply(game, [Advisory("use_dynamic_resolution", "false")], write: true);

        Assert.True(result.Written);
        Assert.Equal(AdvisoryChangeStatus.Verified, Assert.Single(result.Changes).Status);
        Assert.Contains("use_dynamic_resolution=false", File.ReadAllText(path));
        Assert.NotEmpty(result.BackupPaths);
    }

    [Fact]
    public void A_setting_already_at_target_is_left_untouched()
    {
        var (game, _) = GameWith("[DISPLAY]\nuse_dynamic_resolution=false\n");

        var result = NewApplier().Apply(game, [Advisory("use_dynamic_resolution", "false")], write: true);

        Assert.Equal(AdvisoryChangeStatus.AlreadyAtTarget, Assert.Single(result.Changes).Status);
        Assert.False(result.Written); // no Ready change → nothing written
    }

    [Fact]
    public void A_missing_key_cannot_be_corrected_automatically()
    {
        var (game, _) = GameWith("[DISPLAY]\nother=1\n");

        var result = NewApplier().Apply(game, [Advisory("use_dynamic_resolution", "false")], write: false);

        Assert.Equal(AdvisoryChangeStatus.KeyNotFound, Assert.Single(result.Changes).Status);
    }
}

public class StandaloneScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-standalone-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ScanStandaloneFolders_finds_PoE2_installed_off_the_C_drive()
    {
        var drive = Path.Combine(_root, "D");
        Directory.CreateDirectory(Path.Combine(drive, "Games", "Path of Exile 2"));

        var games = new InstalledGameScanner().ScanStandaloneFolders([drive]);

        var poe2 = Assert.Single(games, g => g.Name == "Path of Exile 2");
        Assert.Equal(GameSource.Folder, poe2.Source);
    }
}

public class FpsSeriesTests
{
    [Fact]
    public void Series_is_downsampled_but_keeps_the_dips()
    {
        // 600 frames of ~60 FPS (16.67 ms) with a hard spike near the middle.
        var frames = new List<double>();
        for (var i = 0; i < 600; i++)
            frames.Add(i == 300 ? 66.0 : 16.67);

        var stats = FrametimeAnalyzer.Analyze(frames);

        Assert.NotNull(stats);
        Assert.InRange(stats!.FpsSeries.Count, 2, 120);
        // The dip survives downsampling: some point is well below 60 FPS.
        Assert.Contains(stats.FpsSeries, fps => fps < 45);
    }
}
