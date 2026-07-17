using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perfoptimize.Core.Startup;

/// <summary>
/// Persists <see cref="StartupBackup"/> records (one JSON file per disabled
/// entry) so a disabled program can be restored across app restarts. Pure file
/// I/O with no Windows dependency, which keeps it unit-testable against a temp
/// directory.
/// </summary>
public sealed class StartupBackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public StartupBackupStore(string directory) => _directory = directory;

    /// <summary>Default location, alongside the game-config backups.</summary>
    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Perfoptimize", "backups", "startup");

    /// <summary>Writes (or overwrites) the backup for its entry id.</summary>
    public void Save(StartupBackup backup)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(backup.Id), JsonSerializer.Serialize(backup, JsonOptions));
    }

    /// <summary>The backup for an id, or null if the entry was never disabled.</summary>
    public StartupBackup? Find(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<StartupBackup>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null; // Corrupt file — treat as absent rather than throwing.
        }
    }

    /// <summary>All disabled entries, most recently disabled first.</summary>
    public IReadOnlyList<StartupBackup> List()
    {
        if (!Directory.Exists(_directory))
            return [];
        var backups = new List<StartupBackup>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<StartupBackup>(File.ReadAllText(file), JsonOptions) is { } backup)
                    backups.Add(backup);
            }
            catch
            {
                // Skip a corrupt record rather than failing the whole list.
            }
        }
        return backups.OrderByDescending(b => b.DisabledAt).ToList();
    }

    /// <summary>
    /// Folder that holds a disabled Startup-folder file's copy, keyed by entry id.
    /// Created on demand by the caller before moving the file in.
    /// </summary>
    public string DisabledFilesDirectory(string id) =>
        Path.Combine(_directory, "files", string.Concat(id.Where(char.IsLetterOrDigit)));

    /// <summary>Drops the backup once its entry has been restored.</summary>
    public void Remove(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string PathFor(string id)
    {
        // Ids are hex, but guard anyway so a crafted id can't escape the folder.
        var safe = string.Concat(id.Where(c => char.IsLetterOrDigit(c)));
        if (safe.Length == 0)
            safe = "invalid";
        return Path.Combine(_directory, safe + ".json");
    }
}
