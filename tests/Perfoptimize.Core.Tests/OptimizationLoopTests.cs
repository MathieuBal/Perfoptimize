using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Monitoring.Frametimes;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;

namespace Perfoptimize.Core.Tests;

public class OptimizationLoopTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-loop-{Guid.NewGuid():N}");
    private readonly string _configPath;
    private readonly GameProfile _game;
    private readonly GameSetting _setting;

    public OptimizationLoopTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "user.settings");
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=3");
        _setting = new GameSetting
        {
            Name = "Shadow Quality", Impact = SettingImpact.Gpu, QualityLoss = 2, PerformanceGain = 3,
            Config = new SettingBinding
                { File = "user.settings", Section = "Rendering", Key = "ShadowQuality", Levels = ["0", "1", "2", "3", "4"] },
        };
        _game = new GameProfile
        {
            Name = "Loop Game",
            Executables = ["loop.exe"],
            ConfigFiles = [new GameConfigFile { Path = _configPath, Format = ConfigFormat.Ini }],
            Settings = [_setting],
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private OptimizationLoop Loop()
    {
        var backups = new ConfigBackupService(Path.Combine(_root, "backups"));
        return new OptimizationLoop(new SettingsApplier(backups));
    }

    private static FrametimeStatistics Fps(double average, double onePercentLow) => new()
    {
        FrameCount = 1000,
        AverageFps = average,
        OnePercentLowFps = onePercentLow,
        AverageFrametimeMs = 1000 / average,
        WorstFrametimeMs = 1000 / onePercentLow,
        StutterCount = 0,
        StutterPercent = 0,
    };

    [Fact]
    public async Task Measured_improvement_keeps_the_change()
    {
        var outcome = await Loop().RunOnceAsync(
            _game, _setting, Fps(48, 38),
            () => Task.FromResult<FrametimeStatistics?>(Fps(55, 45)));

        Assert.NotNull(outcome);
        Assert.True(outcome.Kept);
        Assert.Contains("measured on this machine", outcome.Verdict);
        // The lowered value stays in the file.
        Assert.Equal("2", IniFile.Parse(File.ReadAllText(_configPath))["Rendering"]["ShadowQuality"]);
    }

    [Fact]
    public async Task No_measured_benefit_rolls_the_file_back()
    {
        var outcome = await Loop().RunOnceAsync(
            _game, _setting, Fps(48, 38),
            () => Task.FromResult<FrametimeStatistics?>(Fps(48.5, 38))); // +1%: not worth it

        Assert.NotNull(outcome);
        Assert.False(outcome.Kept);
        Assert.Equal("3", IniFile.Parse(File.ReadAllText(_configPath))["Rendering"]["ShadowQuality"]);
    }

    [Fact]
    public async Task Better_one_percent_low_keeps_the_change_even_with_flat_average()
    {
        var outcome = await Loop().RunOnceAsync(
            _game, _setting, Fps(60, 30),
            () => Task.FromResult<FrametimeStatistics?>(Fps(60, 42))); // +40% on 1% low

        Assert.NotNull(outcome);
        Assert.True(outcome.Kept);
    }

    [Fact]
    public async Task Failed_second_measurement_rolls_back_and_says_so()
    {
        var outcome = await Loop().RunOnceAsync(
            _game, _setting, Fps(48, 38),
            () => Task.FromResult<FrametimeStatistics?>(null));

        Assert.NotNull(outcome);
        Assert.False(outcome.Kept);
        Assert.Contains("rolled back", outcome.Verdict);
        Assert.Equal("3", IniFile.Parse(File.ReadAllText(_configPath))["Rendering"]["ShadowQuality"]);
    }

    [Fact]
    public async Task Unappliable_setting_changes_nothing_and_returns_null()
    {
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=exotic");

        var outcome = await Loop().RunOnceAsync(
            _game, _setting, Fps(48, 38),
            () => Task.FromResult<FrametimeStatistics?>(Fps(60, 50)));

        Assert.Null(outcome);
        Assert.Contains("exotic", File.ReadAllText(_configPath));
    }

    [Fact]
    public async Task Exception_during_the_second_measurement_still_rolls_back()
    {
        // "Unverifiable change never stays" must hold even when the measurement
        // code throws instead of politely returning null.
        await Assert.ThrowsAsync<IOException>(() => Loop().RunOnceAsync(
            _game, _setting, Fps(48, 38),
            () => throw new IOException("capture file vanished")));

        Assert.Equal("3", IniFile.Parse(File.ReadAllText(_configPath))["Rendering"]["ShadowQuality"]);
    }

    [Fact]
    public async Task Rollback_uses_this_runs_backup_not_the_latest_one()
    {
        var backups = new ConfigBackupService(Path.Combine(_root, "backups"));
        var loop = new OptimizationLoop(new SettingsApplier(backups));

        var outcome = await loop.RunOnceAsync(
            _game, _setting, Fps(48, 38),
            () =>
            {
                // A decoy backup taken between apply and rollback (another
                // operation, another instance): rollback must NOT pick it.
                File.WriteAllText(Path.Combine(_root, "decoy.settings"), "[Rendering]\nShadowQuality=0");
                backups.Backup(_game.Name, Path.Combine(_root, "decoy.settings"));
                var decoy = backups.Backup(_game.Name, _configPath); // newer backup of the SAME file
                File.WriteAllText(decoy, "[Rendering]\nShadowQuality=999");
                return Task.FromResult<FrametimeStatistics?>(null);
            });

        Assert.NotNull(outcome);
        Assert.False(outcome.Kept);
        Assert.Equal("3", IniFile.Parse(File.ReadAllText(_configPath))["Rendering"]["ShadowQuality"]);
    }

    [Fact]
    public async Task Average_gain_with_collapsed_one_percent_low_is_rolled_back()
    {
        // +4% average but the 1% low tanks by 35%: stutter is felt more than averages.
        var outcome = await Loop().RunOnceAsync(
            _game, _setting, Fps(50, 40),
            () => Task.FromResult<FrametimeStatistics?>(Fps(52, 26)));

        Assert.NotNull(outcome);
        Assert.False(outcome.Kept);
        Assert.Contains("smoothness", outcome.Verdict);
        Assert.Equal("3", IniFile.Parse(File.ReadAllText(_configPath))["Rendering"]["ShadowQuality"]);
    }

    [Fact]
    public void Decide_rejects_a_stutter_increase_even_with_better_averages()
    {
        var baseline = Fps(50, 45);
        var after = new FrametimeStatistics
        {
            FrameCount = 1000, AverageFps = 54, OnePercentLowFps = 46,
            AverageFrametimeMs = 18.5, WorstFrametimeMs = 80,
            StutterCount = 60, StutterPercent = 6, // baseline was 0
        };

        var decision = OptimizationLoop.Decide(baseline, after);

        Assert.False(decision.Keep);
    }
}

public class SessionPlanStorageTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"perfoptimize-plan-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public void Plan_fps_and_target_round_trip()
    {
        using var store = new SessionStore(_databasePath);
        var id = store.Save(new SessionRecord
        {
            StartedAt = DateTimeOffset.Now,
            GameName = "Cyberpunk 2077",
            Bottleneck = BottleneckType.Gpu,
            TargetFps = 90,
            AverageFps = 61.4,
            OnePercentLowFps = 44.2,
            Plan =
            [
                new StoredRecommendation
                {
                    Title = "Cyberpunk 2077: lower \"Ray Tracing\" by one step",
                    Reason = "…", SettingName = "Ray Tracing", ExpectedGainPercent = 30,
                },
                new StoredRecommendation { Title = "Enable DLSS in Quality mode", Reason = "…" },
            ],
        });

        var session = store.GetById(id);

        Assert.NotNull(session);
        Assert.Equal(90, session.TargetFps);
        Assert.Equal(61.4, session.AverageFps);
        Assert.Equal(44.2, session.OnePercentLowFps);
        Assert.Equal(2, session.Plan.Count);
        Assert.Equal("Ray Tracing", session.Plan[0].SettingName);
        Assert.Null(session.Plan[1].SettingName); // generic advice has no setting
    }

    [Fact]
    public void Old_databases_are_migrated_in_place()
    {
        // Simulate a v1 database created before the plan columns existed.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at TEXT NOT NULL,
                    game_name TEXT NULL,
                    bottleneck TEXT NOT NULL,
                    summary_json TEXT NOT NULL
                );
                INSERT INTO sessions (started_at, game_name, bottleneck, summary_json)
                VALUES ('2026-07-01T10:00:00+00:00', 'Elden Ring', 'Cpu', '{}');
                """;
            command.ExecuteNonQuery();
        }

        using var store = new SessionStore(_databasePath);
        var legacy = Assert.Single(store.GetRecent());
        Assert.Equal("Elden Ring", legacy.GameName);
        Assert.Null(legacy.TargetFps);
        Assert.Empty(legacy.Plan);

        // And new records with plans can be saved into the migrated schema.
        var id = store.Save(new SessionRecord
        {
            StartedAt = DateTimeOffset.Now,
            GameName = "Elden Ring",
            Bottleneck = BottleneckType.Gpu,
            TargetFps = 60,
            Plan = [new StoredRecommendation { Title = "t", Reason = "r", SettingName = "Shadow Quality" }],
        });
        Assert.Equal("Shadow Quality", store.GetById(id)!.Plan[0].SettingName);
    }
}

public class LegacyEncodingTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"perfoptimize-legacy-{Guid.NewGuid():N}");

    public LegacyEncodingTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Windows1252_accents_survive_a_rewrite()
    {
        // "é" in Windows-1252/Latin-1 is the single byte 0xE9 — invalid as
        // standalone UTF-8, which is exactly what the detector must handle.
        var path = Path.Combine(_directory, "legacy.ini");
        var latin1Bytes = System.Text.Encoding.Latin1.GetBytes("[Général]\nqualité=3");
        File.WriteAllBytes(path, latin1Bytes);

        SafeFileWriter.ReplaceAtomically(path, "[Général]\nqualité=2");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(latin1Bytes.Length, bytes.Length); // still single-byte encoded
        Assert.Equal("[Général]\nqualité=2", System.Text.Encoding.Latin1.GetString(bytes));
    }
}
