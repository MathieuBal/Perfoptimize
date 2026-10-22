using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Tests;

public class IniFileTests
{
    private const string Sample = """
        ; Path of Exile style config
        [DISPLAY]
        resolution_width=2560
        resolution_height=1440
        fullscreen_mode=1

        [GRAPHICS]
        shadow_quality = high
        texture_quality=0
        # engine comment
        multithreading=true
        """;

    [Fact]
    public void Parse_reads_sections_keys_and_values()
    {
        var ini = IniFile.Parse(Sample);

        Assert.Equal("2560", ini["DISPLAY"]["resolution_width"]);
        Assert.Equal("high", ini["GRAPHICS"]["shadow_quality"]);
        Assert.Equal("true", ini["GRAPHICS"]["multithreading"]);
        Assert.False(ini["GRAPHICS"].ContainsKey("# engine comment"));
    }

    [Fact]
    public void Parse_is_case_insensitive_and_handles_global_keys()
    {
        var ini = IniFile.Parse("orphan=1\n[Video]\nVSync=0");

        Assert.Equal("1", ini[""]["orphan"]);
        Assert.Equal("0", ini["video"]["vsync"]);
    }

    [Fact]
    public void SetValue_edits_only_the_target_line_and_keeps_comments()
    {
        var updated = IniFile.SetValue(Sample, "GRAPHICS", "shadow_quality", "medium");

        var ini = IniFile.Parse(updated);
        Assert.Equal("medium", ini["GRAPHICS"]["shadow_quality"]);
        Assert.Equal("0", ini["GRAPHICS"]["texture_quality"]);        // untouched
        Assert.Contains("; Path of Exile style config", updated);     // comments preserved
        Assert.Contains("# engine comment", updated);
        Assert.Contains("shadow_quality = medium", updated);          // original spacing kept
    }

    [Fact]
    public void SetValue_appends_missing_key_inside_its_section()
    {
        var updated = IniFile.SetValue(Sample, "DISPLAY", "vsync", "0");

        var ini = IniFile.Parse(updated);
        Assert.Equal("0", ini["DISPLAY"]["vsync"]);
        // The key must land in [DISPLAY], not leak into [GRAPHICS].
        Assert.False(ini["GRAPHICS"].ContainsKey("vsync"));
    }

    [Fact]
    public void SetValue_creates_missing_section_at_the_end()
    {
        var updated = IniFile.SetValue("[A]\nx=1", "B", "y", "2");

        var ini = IniFile.Parse(updated);
        Assert.Equal("1", ini["A"]["x"]);
        Assert.Equal("2", ini["B"]["y"]);
    }
}

public class ConfigPathResolverTests
{
    // Platform-neutral fixture: hardcoded '/' in expected values broke the
    // Windows leg of the CI matrix.
    private static readonly string DocumentsDir =
        Path.Combine(Path.GetTempPath(), "player", "Documents");

    private static string? Lookup(string token) => token switch
    {
        "DOCUMENTS" => DocumentsDir,
        _ => null,
    };

    [Fact]
    public void Resolve_expands_placeholders_and_normalizes_separators()
    {
        var resolved = ConfigPathResolver.Resolve(
            @"%DOCUMENTS%\The Witcher 3\user.settings", Lookup);

        Assert.Equal(Path.Combine(DocumentsDir, "The Witcher 3", "user.settings"), resolved);
    }

    [Fact]
    public void Resolve_leaves_unknown_placeholders_untouched()
    {
        var resolved = ConfigPathResolver.Resolve("%UNKNOWN%/file.ini", Lookup);

        Assert.Contains("%UNKNOWN%", resolved);
    }

    [Fact]
    public void Built_in_profiles_expose_resolvable_config_files()
    {
        var profiles = GameProfileRepository.LoadBuiltIn();

        var withConfigs = profiles.Where(p => p.ConfigFiles.Count > 0).ToList();
        Assert.True(withConfigs.Count >= 4);
        Assert.All(withConfigs.SelectMany(p => p.ConfigFiles), file =>
        {
            var resolved = file.ResolvePath();
            Assert.DoesNotContain("%DOCUMENTS%", resolved);
            Assert.DoesNotContain("%LOCALAPPDATA%", resolved);
            Assert.False(string.IsNullOrWhiteSpace(resolved));
        });
    }
}

public class ConfigBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"perfoptimize-backups-{Guid.NewGuid():N}");
    private readonly string _configDir;
    private readonly string _configPath;

    public ConfigBackupServiceTests()
    {
        _configDir = Path.Combine(_root, "game");
        Directory.CreateDirectory(_configDir);
        _configPath = Path.Combine(_configDir, "user.settings");
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=4");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ConfigBackupService Service() => new(Path.Combine(_root, "backups"));

    [Fact]
    public void Backup_copies_the_file_and_ListBackups_finds_it()
    {
        var service = Service();

        var backupPath = service.Backup("The Witcher 3: Wild Hunt", _configPath);

        Assert.True(File.Exists(backupPath));
        Assert.Equal(File.ReadAllText(_configPath), File.ReadAllText(backupPath));
        Assert.Equal([backupPath], service.ListBackups("The Witcher 3: Wild Hunt", _configPath));
    }

    [Fact]
    public void RestoreLatest_brings_back_the_most_recent_backup()
    {
        var service = Service();
        service.Backup("Game", _configPath);
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=1"); // "bad" modification

        var restored = service.RestoreLatest("Game", _configPath);

        Assert.NotNull(restored);
        Assert.Equal("[Rendering]\nShadowQuality=4", File.ReadAllText(_configPath));
    }

    [Fact]
    public void RestoreExact_can_restore_the_backup_from_this_apply_not_the_latest_one()
    {
        var service = Service();
        var first = service.Backup("Game", _configPath); // ShadowQuality=4
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=3");
        var second = service.Backup("Game", _configPath); // ShadowQuality=3
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=1");

        var restored = service.RestoreExact("Game", _configPath, first);

        Assert.Equal(first, restored);
        Assert.NotEqual(second, restored);
        Assert.Equal("[Rendering]\nShadowQuality=4", File.ReadAllText(_configPath));
    }

    [Fact]
    public void RestoreExact_rejects_a_path_that_is_not_a_known_backup_for_this_config()
    {
        var service = Service();
        service.Backup("Game", _configPath);
        File.WriteAllText(_configPath, "[Rendering]\nShadowQuality=1");
        var untrusted = Path.Combine(_root, "untrusted.bak");
        File.WriteAllText(untrusted, "[Rendering]\nShadowQuality=999");

        var restored = service.RestoreExact("Game", _configPath, untrusted);

        Assert.Null(restored);
        Assert.False(service.IsKnownBackup("Game", _configPath, untrusted));
        Assert.Equal("[Rendering]\nShadowQuality=1", File.ReadAllText(_configPath));
    }

    [Fact]
    public void RestoreLatest_without_backup_returns_null_and_leaves_file_alone()
    {
        var service = Service();

        Assert.Null(service.RestoreLatest("Game", _configPath));
        Assert.Equal("[Rendering]\nShadowQuality=4", File.ReadAllText(_configPath));
    }

    [Fact]
    public void Backup_of_missing_file_throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            Service().Backup("Game", Path.Combine(_configDir, "missing.ini")));
    }

    [Fact]
    public void Multiple_backups_are_ordered_newest_first()
    {
        var service = Service();
        var first = service.Backup("Game", _configPath);
        var second = service.Backup("Game", _configPath);

        var backups = service.ListBackups("Game", _configPath);

        Assert.Equal(2, backups.Count);
        Assert.Equal(second, backups[0]);
        Assert.Equal(first, backups[1]);
    }
}
