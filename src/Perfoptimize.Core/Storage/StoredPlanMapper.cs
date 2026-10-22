using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Storage;

/// <summary>
/// Captures an advised plan and turns stored recommendations back into
/// applicable settings. Modern plans are identified by exact config snapshots
/// (file/section/key/observed/proposed); display names are never enough to
/// decide what the dashboard may write.
/// </summary>
public static class StoredPlanMapper
{
    /// <summary>
    /// Snapshots the exact config edits that were Ready when the advice was
    /// produced. Recommendations that are menu-only, unsupported, already at
    /// their lowest level, or otherwise ambiguous remain useful as advice but
    /// intentionally carry no writable snapshot.
    /// </summary>
    public static List<StoredRecommendation> Snapshot(AdvicePlan plan, GameProfile? game)
    {
        var plannedBySetting = new Dictionary<string, PlannedChange>(StringComparer.OrdinalIgnoreCase);
        if (game is not null)
        {
            var boundSettings = plan.Recommendations
                .Where(r => r.Setting is { Config: not null } s && s.IsAutoLowerable)
                .Select(r => r.Setting!)
                .ToList();

            if (boundSettings.Count > 0)
            {
                var dryRun = new SettingsApplier(
                        new ConfigBackupService(ConfigBackupService.DefaultBackupRoot()))
                    .Apply(game, boundSettings, write: false);

                foreach (var change in dryRun.Changes.Where(c => c.Status == PlannedChangeStatus.Ready))
                    plannedBySetting.TryAdd(change.Setting.Name, change);
            }
        }

        return plan.Recommendations.Select(r =>
        {
            PlannedChange? change = null;
            if (r.Setting is not null)
                plannedBySetting.TryGetValue(r.Setting.Name, out change);

            return new StoredRecommendation
            {
                Title = r.Title,
                Reason = r.Reason,
                SettingName = r.Setting?.Name,
                ExpectedGainPercent = r.ExpectedGainPercent,
                ConfigFile = change is not null ? r.Setting!.Config!.File : null,
                ConfigSection = change is not null ? r.Setting!.Config!.Section : null,
                ConfigKey = change is not null ? r.Setting!.Config!.Key : null,
                ObservedValue = change?.CurrentValue,
                ProposedValue = change?.NewValue,
            };
        }).ToList();
    }

    /// <summary>
    /// Returns only edits with a complete exact snapshot. This is the safe path
    /// for interactive/dashboard writes: if no exact snapshot exists, nothing
    /// is guessed or recomputed.
    /// </summary>
    public static List<GameSetting> ToExactSettings(IReadOnlyList<StoredRecommendation> plan) =>
        plan.Select(r => r.ToExactSetting())
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

    /// <summary>
    /// Compatibility mapper used by the CLI. Exact snapshots always win.
    /// Display-name matching survives solely for legacy sessions recorded
    /// before snapshots existed.
    /// </summary>
    public static List<GameSetting> ToSettings(GameProfile game, IReadOnlyList<StoredRecommendation> plan)
    {
        var exactSettings = ToExactSettings(plan);

        // A modern plan: every bound setting carries its snapshot. Settings
        // without one were menu-only advice and must not be guessed by name.
        if (exactSettings.Count > 0)
            return exactSettings;

        // Legacy plan (recorded before snapshots): name matching, clearly the
        // best effort it is — the caller has already verified the game matches.
        return plan
            .Where(r => r.SettingName is not null)
            .Select(r => game.Settings.FirstOrDefault(s =>
                s.Name.Equals(r.SettingName, StringComparison.OrdinalIgnoreCase)))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }
}
