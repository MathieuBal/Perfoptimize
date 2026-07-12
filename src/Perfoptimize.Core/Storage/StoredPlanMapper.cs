using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Storage;

/// <summary>
/// Turns a stored session plan back into applicable settings. Plans recorded
/// with exact snapshots (file/section/key/values) are applied from those
/// snapshots ONLY — matching by display name would silently apply something
/// else than what the user saw. Name matching survives solely for legacy
/// sessions recorded before snapshots existed.
/// </summary>
public static class StoredPlanMapper
{
    public static List<GameSetting> ToSettings(GameProfile game, IReadOnlyList<StoredRecommendation> plan)
    {
        var exactSettings = plan
            .Select(r => r.ToExactSetting())
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

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
