using System.Text;
using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;

namespace Perfoptimize.Core.Tests;

/// <summary>
/// 'apply --session' must execute the snapshotted plan exactly: same file, same
/// key, same proposed value — and refuse to touch a value that changed since
/// the session was recorded.
/// </summary>
public class SessionExactApplyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-exact-{Guid.NewGuid():N}");
    private readonly string _configPath;
    private readonly GameProfile _game;

    public SessionExactApplyTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "user.settings");
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=4");
        _game = new GameProfile
        {
            Name = "Exact Game",
            Executables = ["exact.exe"],
            ConfigFiles = [new GameConfigFile { Path = _configPath, Format = ConfigFormat.Ini }],
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static StoredRecommendation Stored(string observed, string proposed) => new()
    {
        Title = "Exact Game: lower \"Shadow Quality\" by one step",
        Reason = "…",
        SettingName = "Shadow Quality",
        ConfigFile = "user.settings",
        ConfigSection = "Rendering",
        ConfigKey = "ShadowQuality",
        ObservedValue = observed,
        ProposedValue = proposed,
    };

    private SettingsApplier Applier() =>
        new(new ConfigBackupService(Path.Combine(_root, "backups")));

    [Fact]
    public void Snapshot_applies_the_exact_proposed_value()
    {
        var setting = Stored(observed: "4", proposed: "3").ToExactSetting();

        Assert.NotNull(setting);
        var result = Applier().Apply(_game, [setting], write: true);

        var change = Assert.Single(result.Changes);
        Assert.Equal(PlannedChangeStatus.Ready, change.Status);
        Assert.Equal("3", IniFile.Parse(File.ReadAllText(_configPath))["Rendering"]["ShadowQuality"]);
    }

    [Fact]
    public void Snapshot_refuses_a_value_that_changed_since_the_session()
    {
        // The user manually set shadows to 2 between monitor and apply: the
        // stored plan (4 -> 3) no longer describes reality, so nothing moves.
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=2");

        var setting = Stored(observed: "4", proposed: "3").ToExactSetting()!;
        var result = Applier().Apply(_game, [setting], write: true);

        var change = Assert.Single(result.Changes);
        Assert.Equal(PlannedChangeStatus.UnknownCurrentValue, change.Status);
        Assert.Equal("2", IniFile.Parse(File.ReadAllText(_configPath))["Rendering"]["ShadowQuality"]);
    }

    [Fact]
    public void Recommendations_without_snapshot_produce_no_exact_setting()
    {
        Assert.Null(new StoredRecommendation { Title = "generic", SettingName = "Shadows" }.ToExactSetting());
    }
}

public class ApplierEncodingEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-enc-{Guid.NewGuid():N}");

    public ApplierEncodingEndToEndTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Windows1252_config_survives_the_full_apply_path()
    {
        // The reviewer's exact concern: SettingsApplier used to READ with UTF-8
        // before the encoding-aware writer ever ran, corrupting 0xE9 ('é').
        var configPath = Path.Combine(_root, "legacy.ini");
        File.WriteAllBytes(configPath,
            Encoding.Latin1.GetBytes("; qualité générale\n[Rendering]\nShadowQuality=4"));

        var game = new GameProfile
        {
            Name = "Legacy Game",
            Executables = ["legacy.exe"],
            ConfigFiles = [new GameConfigFile { Path = configPath, Format = ConfigFormat.Ini }],
            Settings =
            [
                new GameSetting
                {
                    Name = "Shadow Quality", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 3,
                    Config = new SettingBinding
                        { File = "legacy.ini", Section = "Rendering", Key = "ShadowQuality", Levels = ["0", "1", "2", "3", "4"] },
                },
            ],
        };

        var applier = new SettingsApplier(new ConfigBackupService(Path.Combine(_root, "backups")));
        var result = applier.Apply(game, game.Settings.ToList(), write: true);

        Assert.All(result.Changes, c => Assert.Equal(PlannedChangeStatus.Ready, c.Status));
        var bytes = File.ReadAllBytes(configPath);
        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains("qualité générale", text);           // accents intact
        Assert.Contains("ShadowQuality=3", text);            // change applied
        Assert.DoesNotContain((byte)0xC3, bytes);            // no UTF-8 re-encoding of 'é'
    }
}

public class TuningStateStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"perfoptimize-tuning-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Pending_experiments_round_trip_and_delete()
    {
        var store = new TuningStateStore(_directory);
        var baseline = new Perfoptimize.Core.Monitoring.Frametimes.FrametimeStatistics
        {
            FrameCount = 500, AverageFps = 48.1, OnePercentLowFps = 39.7,
            AverageFrametimeMs = 20.8, WorstFrametimeMs = 41, StutterCount = 4, StutterPercent = 0.8,
        };

        var pending = new PendingTune
        {
            Id = TuningStateStore.NewId(),
            GameName = "The Witcher 3: Wild Hunt",
            SettingName = "Shadow Quality",
            ChangeDescription = "[Rendering] ShadowQuality: 4 → 3",
            Baseline = baseline,
            BackupByFile = new Dictionary<string, string> { ["/tmp/user.settings"] = "/tmp/backup.bak" },
            ConfigPath = "/tmp/user.settings",
            ConfigSection = "Rendering",
            ConfigKey = "ShadowQuality",
            OriginalValue = "4",
            AppliedValue = "3",
        };
        store.Save(pending);

        var loaded = store.Load(pending.Id);
        Assert.NotNull(loaded);
        Assert.Equal("The Witcher 3: Wild Hunt", loaded.GameName);
        Assert.Equal(48.1, loaded.Baseline.AverageFps);
        Assert.Equal("/tmp/backup.bak", loaded.BackupByFile["/tmp/user.settings"]);
        Assert.Equal("4", loaded.OriginalValue);   // surgical rollback data survives
        Assert.Equal("ShadowQuality", loaded.ConfigKey);
        Assert.Single(store.ListPending());

        store.Delete(pending.Id);
        Assert.Null(store.Load(pending.Id));
        Assert.Empty(store.ListPending());
    }

    [Fact]
    public void Unknown_or_malicious_ids_load_nothing()
    {
        var store = new TuningStateStore(_directory);
        Assert.Null(store.Load("nope"));
        Assert.Null(store.Load("../../etc/passwd"));
    }
}
