using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Configs;

public sealed class ApplyResult
{
    public IReadOnlyList<PlannedChange> Changes { get; init; } = [];
    /// <summary>Backups created before writing, one per modified file.</summary>
    public IReadOnlyList<string> BackupPaths { get; init; } = [];
    /// <summary>The exact backup created for each modified file in THIS run — the
    /// only safe rollback target (a later backup could belong to someone else).</summary>
    public IReadOnlyDictionary<string, string> BackupByFile { get; init; } =
        new Dictionary<string, string>();
    /// <summary>True only when at least one file was actually written.</summary>
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
    /// <param name="requireAllReady">
    /// When true, writing is all-or-nothing at the planning boundary: if even
    /// one requested setting is no longer exactly applicable, nothing is
    /// written. The cockpit uses this for frozen session plans so a stale plan
    /// can never degrade into a silently partial apply.
    /// </param>
    public ApplyResult Apply(
        GameProfile game,
        IReadOnlyList<GameSetting> settingsToLower,
        bool write,
        bool requireAllReady = false)
    {
        var configTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configFile in game.ConfigFiles)
        {
            var path = configFile.ResolvePath();
            if (File.Exists(path))
            {
                // Read with the file's real encoding: a Windows-1252 INI decoded
                // as UTF-8 would corrupt its accents before the writer even runs.
                configTexts[path] = File.ReadAllText(path, SafeFileWriter.DetectEncoding(path));
            }
        }

        var changes = SettingsPlanner.Plan(game, settingsToLower, configTexts);
        if (!write)
            return new ApplyResult { Changes = changes };

        if (requireAllReady && changes.Any(c => c.Status != PlannedChangeStatus.Ready))
            return new ApplyResult { Changes = changes, Written = false };

        var readyChanges = changes.Where(c => c.Status == PlannedChangeStatus.Ready).ToList();
        if (readyChanges.Count == 0)
            return new ApplyResult { Changes = changes, Written = false };

        // Each file: back up, then swap the new content in atomically with its
        // original encoding. If any file fails midway, every file already
        // written in this run is rolled back from its fresh backup, so the
        // user is never left with a half-applied plan.
        var backupByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var written = new List<(string Path, string BackupPath)>();
        try
        {
            foreach (var fileGroup in readyChanges
                         .GroupBy(c => c.FilePath!, StringComparer.OrdinalIgnoreCase))
            {
                var path = fileGroup.Key;
                var backupPath = _backups.Backup(game.Name, path);
                backupByFile[path] = backupPath;

                var text = configTexts[path];
                foreach (var change in fileGroup)
                    text = SettingsPlanner.ApplyToText(text, change);
                SafeFileWriter.ReplaceAtomically(path, text);
                written.Add((path, backupPath));
            }
        }
        catch
        {
            foreach (var (path, backupPath) in written)
                File.Copy(backupPath, path, overwrite: true);
            throw;
        }

        return new ApplyResult
        {
            Changes = changes,
            BackupPaths = backupByFile.Values.ToList(),
            BackupByFile = backupByFile,
            Written = written.Count > 0,
        };
    }
}
