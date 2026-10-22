namespace Perfoptimize.Core.Configs;

/// <summary>
/// Timestamped backups of game configuration files. Golden rule of the
/// assistant: never touch a config file without a backup first, and always
/// offer a way back ("Restore my old settings").
/// </summary>
public sealed class ConfigBackupService
{
    private readonly string _backupRoot;

    public ConfigBackupService(string backupRoot) => _backupRoot = backupRoot;

    /// <summary>Default backup location, next to the session database.</summary>
    public static string DefaultBackupRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Perfoptimize", "backups");

    /// <summary>Copies the config file into the game's backup folder; returns the backup path.</summary>
    public string Backup(string gameName, string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Config file to back up was not found.", configPath);

        var directory = BackupDirectory(gameName);
        Directory.CreateDirectory(directory);

        var fileName = Path.GetFileName(configPath);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
        // The counter keeps same-millisecond backups distinct while preserving
        // the lexicographic ordering that ListBackups relies on.
        var backupPath = Path.Combine(directory, $"{fileName}.{timestamp}-0.bak");
        for (var suffix = 1; File.Exists(backupPath); suffix++)
            backupPath = Path.Combine(directory, $"{fileName}.{timestamp}-{suffix}.bak");
        File.Copy(configPath, backupPath, overwrite: false);
        return backupPath;
    }

    /// <summary>Backups of one config file, most recent first.</summary>
    public IReadOnlyList<string> ListBackups(string gameName, string configPath)
    {
        var directory = BackupDirectory(gameName);
        if (!Directory.Exists(directory))
            return [];
        var fileName = Path.GetFileName(configPath);
        return Directory.GetFiles(directory, $"{fileName}.*.bak")
            .OrderByDescending(f => f, StringComparer.Ordinal) // timestamps sort lexicographically
            .ToList();
    }

    /// <summary>
    /// True only when <paramref name="backupPath"/> is one of the backups that
    /// this service actually created for this game's config file. Client-supplied
    /// paths are never trusted on their shape alone.
    /// </summary>
    public bool IsKnownBackup(string gameName, string configPath, string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
            return false;
        try
        {
            var requested = Path.GetFullPath(backupPath);
            return ListBackups(gameName, configPath)
                .Any(candidate => PathsEqual(Path.GetFullPath(candidate), requested));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Restores one specific backup, but only after verifying it belongs to the
    /// requested game/config pair. Returns the canonical restored backup path,
    /// or null for an unknown/untrusted path.
    /// </summary>
    public string? RestoreExact(string gameName, string configPath, string backupPath)
    {
        if (!IsKnownBackup(gameName, configPath, backupPath))
            return null;

        var requested = Path.GetFullPath(backupPath);
        var canonical = ListBackups(gameName, configPath)
            .First(candidate => PathsEqual(Path.GetFullPath(candidate), requested));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(configPath))!);
        File.Copy(canonical, configPath, overwrite: true);
        return canonical;
    }

    /// <summary>
    /// Restores the most recent backup over the config file.
    /// Returns the backup that was restored, or null when none exists.
    /// </summary>
    public string? RestoreLatest(string gameName, string configPath)
    {
        var latest = ListBackups(gameName, configPath).FirstOrDefault();
        if (latest is null)
            return null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(configPath))!);
        File.Copy(latest, configPath, overwrite: true);
        return latest;
    }

    private string BackupDirectory(string gameName)
    {
        // Game names may contain characters that are invalid in directory names.
        var safe = string.Concat(gameName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_backupRoot, safe);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
