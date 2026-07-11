using System.Text.Json;
using System.Text.Json.Serialization;
using Perfoptimize.Core.Monitoring.Frametimes;

namespace Perfoptimize.Core.Optimization;

/// <summary>
/// A tuning experiment in progress: one change has been applied and awaits its
/// verification measurement. Persisted so 'tune prepare' and 'tune finish' can
/// be separate commands (the user plays the game in between).
/// </summary>
public sealed class PendingTune
{
    public string Id { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string GameName { get; init; } = "";
    public string SettingName { get; init; } = "";
    public string ChangeDescription { get; init; } = "";
    public FrametimeStatistics Baseline { get; init; } = new();
    /// <summary>Config file → exact backup created when the change was applied.</summary>
    public Dictionary<string, string> BackupByFile { get; init; } = [];
}

/// <summary>JSON-file persistence for pending tuning experiments.</summary>
public sealed class TuningStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;

    public TuningStateStore(string directory) => _directory = directory;

    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Perfoptimize", "tuning");

    public PendingTune Create(
        string gameName, string settingName, string changeDescription,
        FrametimeStatistics baseline, IReadOnlyDictionary<string, string> backupByFile)
    {
        var pending = new PendingTune
        {
            Id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"),
            GameName = gameName,
            SettingName = settingName,
            ChangeDescription = changeDescription,
            Baseline = baseline,
            BackupByFile = new Dictionary<string, string>(backupByFile),
        };
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(pending.Id), JsonSerializer.Serialize(pending, Options));
        return pending;
    }

    public PendingTune? Load(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<PendingTune>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public IReadOnlyList<PendingTune> ListPending()
    {
        if (!Directory.Exists(_directory))
            return [];
        return Directory.GetFiles(_directory, "*.json")
            .Select(f => Load(Path.GetFileNameWithoutExtension(f)))
            .Where(p => p is not null)
            .Select(p => p!)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }

    public void Delete(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string PathFor(string id)
    {
        // Ids are generated timestamps, but sanitize anyway: they come back
        // from the command line.
        var safe = string.Concat(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        return Path.Combine(_directory, $"{safe}.json");
    }
}
