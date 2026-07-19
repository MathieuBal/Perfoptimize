using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Configs;

/// <summary>Outcome of planning (and, when written, verifying) one advisory fix.</summary>
public enum AdvisoryChangeStatus
{
    /// <summary>The value can be set to the recommended target.</summary>
    Ready,
    /// <summary>The setting is already at the recommended value — nothing to do.</summary>
    AlreadyAtTarget,
    /// <summary>The key is absent from every config file (game uses its default).</summary>
    KeyNotFound,
    /// <summary>The advisory has no concrete target value — it must be changed in the menu.</summary>
    NoTargetValue,
    /// <summary>No config-bound condition, or the file is a format we cannot write yet.</summary>
    NotSupported,
    /// <summary>Written AND re-read: the file now holds the target value.</summary>
    Verified,
    /// <summary>Written but the re-read did not show the target value — investigate.</summary>
    VerifyFailed,
}

public sealed record AdvisoryPlannedChange
{
    public required SettingAdvisory Advisory { get; init; }
    public AdvisoryChangeStatus Status { get; init; }
    public string? FilePath { get; init; }
    public string? Section { get; init; }
    public string? Key { get; init; }
    public string? CurrentValue { get; init; }
    public string? NewValue { get; init; }
    public string Note { get; init; } = "";
}

public sealed class AdvisoryApplyResult
{
    public IReadOnlyList<AdvisoryPlannedChange> Changes { get; init; } = [];
    public IReadOnlyList<string> BackupPaths { get; init; } = [];
    public bool Written { get; init; }
}

/// <summary>
/// Turns a triggered <see cref="SettingAdvisory"/> into a concrete config edit —
/// setting its key to the advisory's recommended target — then, when asked to
/// write, backs the file up, applies the change atomically, and RE-READS the
/// file to confirm the new value actually stuck. This is what makes the
/// assistant hands-on: instead of only saying "check that X is off", it can
/// verify X's current value and correct it, safely and reversibly.
///
/// Only INI writes are supported (as for <see cref="SettingsApplier"/>); other
/// formats are reported as not-yet-writable rather than touched.
/// </summary>
public sealed class AdvisoryApplier
{
    private readonly ConfigBackupService _backups;

    public AdvisoryApplier(ConfigBackupService backups) => _backups = backups;

    /// <param name="write">False previews only; true backs up, writes, then verifies.</param>
    public AdvisoryApplyResult Apply(
        GameProfile game, IReadOnlyList<SettingAdvisory> advisories, bool write)
    {
        // Read every INI config once, with its real encoding.
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configFile in game.ConfigFiles.Where(f => f.Format == ConfigFormat.Ini))
        {
            var path = configFile.ResolvePath();
            if (File.Exists(path))
                texts[path] = File.ReadAllText(path, SafeFileWriter.DetectEncoding(path));
        }

        var changes = advisories.Select(a => PlanOne(a, texts)).ToList();
        if (!write)
            return new AdvisoryApplyResult { Changes = changes };

        var backupByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var written = new List<(string Path, string BackupPath)>();
        var verified = new List<AdvisoryPlannedChange>();
        try
        {
            // Group Ready changes per file so each file is backed up once.
            foreach (var group in changes
                         .Where(c => c.Status == AdvisoryChangeStatus.Ready)
                         .GroupBy(c => c.FilePath!, StringComparer.OrdinalIgnoreCase))
            {
                var path = group.Key;
                var backupPath = _backups.Backup(game.Name, path);
                backupByFile[path] = backupPath;

                var text = texts[path];
                foreach (var change in group)
                    text = IniFile.SetValue(text, change.Section ?? "", change.Key!, change.NewValue!);
                SafeFileWriter.ReplaceAtomically(path, text);
                written.Add((path, backupPath));

                // Verify by re-reading from disk: did the value actually change?
                var reread = IniFile.Parse(File.ReadAllText(path, SafeFileWriter.DetectEncoding(path)));
                foreach (var change in group)
                {
                    var ok = reread.TryGetValue(change.Section ?? "", out var section) &&
                             section.TryGetValue(change.Key!, out var now) &&
                             now.Trim().Equals(change.NewValue, StringComparison.OrdinalIgnoreCase);
                    verified.Add(change with
                    {
                        Status = ok ? AdvisoryChangeStatus.Verified : AdvisoryChangeStatus.VerifyFailed,
                        Note = ok
                            ? $"[{change.Section}] {change.Key} = {change.NewValue} (vérifié sur le disque)."
                            : $"Écriture effectuée mais la relecture de [{change.Section}] {change.Key} ne montre pas {change.NewValue}.",
                    });
                }
            }
        }
        catch
        {
            foreach (var (path, backupPath) in written)
                File.Copy(backupPath, path, overwrite: true);
            throw;
        }

        // Replace the Ready entries with their verified outcome, keep the rest.
        var byId = verified.ToDictionary(c => c.Advisory.Id);
        var final = changes
            .Select(c => byId.TryGetValue(c.Advisory.Id, out var v) && c.Status == AdvisoryChangeStatus.Ready ? v : c)
            .ToList();

        return new AdvisoryApplyResult
        {
            Changes = final,
            BackupPaths = backupByFile.Values.ToList(),
            Written = written.Count > 0,
        };
    }

    private AdvisoryPlannedChange PlanOne(
        SettingAdvisory advisory, IReadOnlyDictionary<string, string> texts)
    {
        var condition = advisory.Conditions.FirstOrDefault(c => c.Key is not null);
        if (condition?.Key is not { } key)
            return new AdvisoryPlannedChange
            {
                Advisory = advisory,
                Status = AdvisoryChangeStatus.NotSupported,
                Note = "Cette recommandation ne cible pas une clé de configuration (à régler dans le menu du jeu).",
            };

        var target = advisory.Recommendation.TargetValue;
        if (string.IsNullOrWhiteSpace(target))
            return new AdvisoryPlannedChange
            {
                Advisory = advisory,
                Status = AdvisoryChangeStatus.NoTargetValue,
                Key = key,
                Note = "Aucune valeur cible précise — à ajuster manuellement dans le menu du jeu.",
            };

        // Find the file + section that actually holds this key.
        foreach (var (path, text) in texts)
        {
            var ini = IniFile.Parse(text);
            var section = LocateSection(ini, condition.Section, key);
            if (section is null)
                continue;

            var currentValue = ini[section][key];
            if (currentValue.Trim().Equals(target, StringComparison.OrdinalIgnoreCase))
                return new AdvisoryPlannedChange
                {
                    Advisory = advisory,
                    Status = AdvisoryChangeStatus.AlreadyAtTarget,
                    FilePath = path, Section = section, Key = key, CurrentValue = currentValue,
                    Note = $"[{section}] {key} vaut déjà {target} — rien à corriger.",
                };

            return new AdvisoryPlannedChange
            {
                Advisory = advisory,
                Status = AdvisoryChangeStatus.Ready,
                FilePath = path, Section = section, Key = key,
                CurrentValue = currentValue, NewValue = target,
                Note = $"[{section}] {key} : {currentValue} → {target}",
            };
        }

        return new AdvisoryPlannedChange
        {
            Advisory = advisory,
            Status = AdvisoryChangeStatus.KeyNotFound,
            Key = key,
            Note = $"Clé « {key} » absente des fichiers INI — impossible de la corriger automatiquement.",
        };
    }

    /// <summary>The INI section holding <paramref name="key"/>: the declared one if it has it, else the first that does.</summary>
    private static string? LocateSection(
        Dictionary<string, Dictionary<string, string>> ini, string? declaredSection, string key)
    {
        if (!string.IsNullOrEmpty(declaredSection))
            return ini.TryGetValue(declaredSection, out var s) && s.ContainsKey(key) ? declaredSection : null;

        foreach (var section in ini)
            if (section.Value.ContainsKey(key))
                return section.Key;
        return null;
    }
}
