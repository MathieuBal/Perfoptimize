using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Configs;

/// <summary>Outcome of planning one setting change.</summary>
public enum PlannedChangeStatus
{
    /// <summary>The value can be lowered by one step.</summary>
    Ready,
    /// <summary>Already at the lowest level of its ladder.</summary>
    AlreadyLowest,
    /// <summary>The current value is not in the documented ladder — do not touch it.</summary>
    UnknownCurrentValue,
    /// <summary>The key is absent from the file (the game uses its default).</summary>
    KeyNotFound,
    /// <summary>No config binding, non-INI format, or config file missing.</summary>
    NotSupported,
}

public sealed class PlannedChange
{
    public required GameSetting Setting { get; init; }
    public PlannedChangeStatus Status { get; init; }
    public string? FilePath { get; init; }
    public string? CurrentValue { get; init; }
    public string? NewValue { get; init; }
    public string Note { get; init; } = "";
}

/// <summary>
/// Computes which config edits would implement "lower this setting by one
/// step", without touching any file. Pure: config contents come in as text,
/// planned changes come out. Anything ambiguous is skipped — a wrong config
/// write is worse than a missed optimization.
/// </summary>
public static class SettingsPlanner
{
    /// <param name="configTexts">INI text of each config file, keyed by resolved path.</param>
    public static IReadOnlyList<PlannedChange> Plan(
        GameProfile game,
        IEnumerable<GameSetting> settingsToLower,
        IReadOnlyDictionary<string, string> configTexts)
    {
        var changes = new List<PlannedChange>();
        foreach (var setting in settingsToLower)
        {
            changes.Add(PlanOne(game, setting, configTexts));
        }
        return changes;
    }

    private static PlannedChange PlanOne(
        GameProfile game, GameSetting setting, IReadOnlyDictionary<string, string> configTexts)
    {
        var binding = setting.Config;
        if (binding is null || binding.Levels.Count == 0)
        {
            return new PlannedChange
            {
                Setting = setting,
                Status = PlannedChangeStatus.NotSupported,
                Note = "No config binding documented for this setting — change it in the game's menu.",
            };
        }

        var configFile = game.ConfigFiles.FirstOrDefault(f =>
            f.ResolvePath().EndsWith(binding.File.Replace('\\', Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));
        if (configFile is null || configFile.Format != ConfigFormat.Ini)
        {
            return new PlannedChange
            {
                Setting = setting,
                Status = PlannedChangeStatus.NotSupported,
                Note = configFile is null
                    ? $"Config file \"{binding.File}\" is not documented in the game profile."
                    : $"Writing {configFile.Format} files is not supported yet.",
            };
        }

        var path = configFile.ResolvePath();
        if (!configTexts.TryGetValue(path, out var text))
        {
            return new PlannedChange
            {
                Setting = setting,
                Status = PlannedChangeStatus.NotSupported,
                FilePath = path,
                Note = "Config file not found on this machine.",
            };
        }

        var ini = IniFile.Parse(text);
        if (!ini.TryGetValue(binding.Section, out var section) ||
            !section.TryGetValue(binding.Key, out var currentValue))
        {
            return new PlannedChange
            {
                Setting = setting,
                Status = PlannedChangeStatus.KeyNotFound,
                FilePath = path,
                Note = $"Key [{binding.Section}] {binding.Key} not present — the game is using its default value.",
            };
        }

        var levelIndex = IndexOfLevel(binding.Levels, currentValue);
        if (levelIndex < 0)
        {
            return new PlannedChange
            {
                Setting = setting,
                Status = PlannedChangeStatus.UnknownCurrentValue,
                FilePath = path,
                CurrentValue = currentValue,
                Note = $"Current value \"{currentValue}\" is not in the documented ladder " +
                       $"({string.Join(", ", binding.Levels)}) — left untouched.",
            };
        }

        if (levelIndex == 0)
        {
            return new PlannedChange
            {
                Setting = setting,
                Status = PlannedChangeStatus.AlreadyLowest,
                FilePath = path,
                CurrentValue = currentValue,
                Note = "Already at the lowest level.",
            };
        }

        return new PlannedChange
        {
            Setting = setting,
            Status = PlannedChangeStatus.Ready,
            FilePath = path,
            CurrentValue = currentValue,
            NewValue = binding.Levels[levelIndex - 1],
            Note = $"[{binding.Section}] {binding.Key}: {currentValue} → {binding.Levels[levelIndex - 1]}",
        };
    }

    /// <summary>Applies one planned change to INI text; the caller persists the result.</summary>
    public static string ApplyToText(string configText, PlannedChange change)
    {
        if (change.Status != PlannedChangeStatus.Ready || change.Setting.Config is not { } binding)
            throw new InvalidOperationException("Only Ready changes can be applied.");
        return IniFile.SetValue(configText, binding.Section, binding.Key, change.NewValue!);
    }

    private static int IndexOfLevel(IReadOnlyList<string> levels, string value)
    {
        for (var i = 0; i < levels.Count; i++)
        {
            if (levels[i].Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
