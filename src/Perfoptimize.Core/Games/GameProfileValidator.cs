namespace Perfoptimize.Core.Games;

/// <summary>
/// Validates game profiles at load time. JSON deserialization accepts almost
/// anything; these checks catch out-of-range scores, empty ladders and other
/// data mistakes before they reach the advisor or — worse — a config write.
/// </summary>
public static class GameProfileValidator
{
    /// <summary>Returns every problem found; an empty list means the profile is usable.</summary>
    public static IReadOnlyList<string> Validate(GameProfile profile)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name == "Unknown")
            errors.Add("Profile has no name.");
        if (profile.Executables.Count == 0 ||
            profile.Executables.Any(string.IsNullOrWhiteSpace))
            errors.Add($"{profile.Name}: executables list is empty or contains blanks.");
        if (profile.Settings.Count == 0)
            errors.Add($"{profile.Name}: no settings documented.");
        if (profile.FrameCapFps is <= 0)
            errors.Add($"{profile.Name}: frameCapFps must be positive.");

        foreach (var setting in profile.Settings)
        {
            var label = $"{profile.Name} / {setting.Name}";
            if (string.IsNullOrWhiteSpace(setting.Name) || setting.Name == "Unknown")
                errors.Add($"{profile.Name}: a setting has no name.");
            if (setting.QualityLoss is < 0 or > 5)
                errors.Add($"{label}: qualityLoss must be within 0-5.");
            if (setting.PerformanceGain is < 0 or > 5)
                errors.Add($"{label}: performanceGain must be within 0-5.");
            if (setting.EstimatedGainPercent is < 0 or > 100)
                errors.Add($"{label}: estimatedGainPercent must be within 0-100.");

            if (setting.VramGiBByLevel is { } budgets &&
                budgets.Values.Any(v => v is <= 0 or > 64))
                errors.Add($"{label}: VRAM budgets must be between 0 and 64 GiB.");

            if (setting.Config is { } binding)
            {
                if (string.IsNullOrWhiteSpace(binding.File) || string.IsNullOrWhiteSpace(binding.Key))
                    errors.Add($"{label}: config binding needs both a file and a key.");
                if (binding.Levels.Count < 2)
                    errors.Add($"{label}: a config ladder needs at least 2 levels.");
                if (binding.Levels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != binding.Levels.Count)
                    errors.Add($"{label}: config ladder contains duplicate levels.");
                if (profile.ConfigFiles.Count == 0)
                    errors.Add($"{label}: has a config binding but the profile lists no config files.");
            }
        }

        foreach (var upscaler in profile.Upscalers ?? [])
        {
            if (upscaler is not ("dlss" or "fsr" or "xess" or "dlss-fg" or "fsr-fg"))
                errors.Add($"{profile.Name}: unknown upscaler identifier \"{upscaler}\".");
        }

        return errors;
    }
}
