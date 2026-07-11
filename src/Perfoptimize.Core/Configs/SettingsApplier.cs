using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Configs;

public sealed class ApplyResult
{
    public IReadOnlyList<PlannedChange> Changes { get; init; } = [];
    /// <summary>Backups created before writing, one per modified file.</summary>
    public IReadOnlyList<string> BackupPaths { get; init; } = [];
    /// <summary>True when files were written (not a dry run).</summary>
    public bool Written { get; init; }
}

/// <summary>
/// Orchestrates the safe write path: read configs, plan the edits, and — only
/// when explicitly asked — back every file up and write the new values.
/// The caller is responsible for verifying the game is not running.
/// </summary>
public sealed class SettingsApplier
{
    private readonly ConfigBackupService _backups;

    public SettingsApplier(ConfigBackupService backups) => _backups = backups;

    /// <param name="write">False plans only (dry run); true backs up then writes.</param>
    public ApplyResult Apply(GameProfile game, IReadOnlyList<GameSetting> settingsToLower, bool write)
    {
        var configTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configFile in game.ConfigFiles)
        {
            var path = configFile.ResolvePath();
            if (File.Exists(path))
                configTexts[path] = File.ReadAllText(path);
        }

        var changes = SettingsPlanner.Plan(game, settingsToLower, configTexts);
        if (!write)
            return new ApplyResult { Changes = changes };

        var backupPaths = new List<string>();
        foreach (var fileGroup in changes
                     .Where(c => c.Status == PlannedChangeStatus.Ready)
                     .GroupBy(c => c.FilePath!, StringComparer.OrdinalIgnoreCase))
        {
            var path = fileGroup.Key;
            backupPaths.Add(_backups.Backup(game.Name, path));

            var text = configTexts[path];
            foreach (var change in fileGroup)
                text = SettingsPlanner.ApplyToText(text, change);
            File.WriteAllText(path, text);
        }

        return new ApplyResult
        {
            Changes = changes,
            BackupPaths = backupPaths,
            Written = true,
        };
    }
}
