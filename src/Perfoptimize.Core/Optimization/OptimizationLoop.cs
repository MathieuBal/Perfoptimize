using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Monitoring.Frametimes;

namespace Perfoptimize.Core.Optimization;

/// <summary>Result of one measured tuning iteration.</summary>
public sealed class TuneOutcome
{
    public required FrametimeStatistics Baseline { get; init; }
    public required FrametimeStatistics After { get; init; }
    public required PlannedChange Change { get; init; }
    /// <summary>True when the change was kept; false when it was rolled back.</summary>
    public bool Kept { get; init; }
    public double AverageFpsChangePercent { get; init; }
    public double OnePercentLowChangePercent { get; init; }
    public string Verdict { get; init; } = "";
}

/// <summary>
/// The measured optimization loop: apply ONE change, re-measure in the same
/// scene, and keep it only if the numbers actually improved on THIS machine.
/// This is what turns declarative profile estimates into verified results.
/// </summary>
public sealed class OptimizationLoop
{
    /// <summary>Minimum average-FPS improvement (percent) for a change to be kept.</summary>
    private const double KeepAverageThresholdPercent = 3;
    /// <summary>A 1% low improvement this large keeps a change even with flat averages.</summary>
    private const double KeepOnePercentLowThresholdPercent = 8;
    /// <summary>An average regression beyond this always rolls the change back.</summary>
    private const double RegressionTolerancePercent = -1;

    private readonly SettingsApplier _applier;
    private readonly ConfigBackupService _backups;

    public OptimizationLoop(SettingsApplier applier, ConfigBackupService backups)
    {
        _applier = applier;
        _backups = backups;
    }

    /// <summary>
    /// Runs one iteration: applies <paramref name="settingToLower"/>, has the
    /// caller re-measure via <paramref name="measureAfter"/> (same scene!), and
    /// keeps or rolls back based on the numbers. Returns null when the setting
    /// could not be applied (no binding, value out of ladder, ...).
    /// </summary>
    /// <param name="baseline">FPS statistics measured before any change.</param>
    /// <param name="measureAfter">
    /// Re-measures FPS after the change is in place — the caller is responsible
    /// for the game being back in a comparable scene.
    /// </param>
    public async Task<TuneOutcome?> RunOnceAsync(
        GameProfile game,
        GameSetting settingToLower,
        FrametimeStatistics baseline,
        Func<Task<FrametimeStatistics?>> measureAfter)
    {
        var applyResult = _applier.Apply(game, [settingToLower], write: true);
        var change = applyResult.Changes.FirstOrDefault(c => c.Status == PlannedChangeStatus.Ready);
        if (change is null || !applyResult.Written)
            return null;

        var after = await measureAfter();
        if (after is null)
        {
            // No usable second measurement: never keep an unverified change.
            Restore(game, change);
            return new TuneOutcome
            {
                Baseline = baseline,
                After = baseline,
                Change = change,
                Kept = false,
                Verdict = "The second measurement failed, so the change was rolled back — " +
                          "a change that cannot be verified must not stay.",
            };
        }

        var averageDelta = PercentChange(baseline.AverageFps, after.AverageFps);
        var lowDelta = PercentChange(baseline.OnePercentLowFps, after.OnePercentLowFps);

        var keep = averageDelta >= KeepAverageThresholdPercent ||
                   (averageDelta >= RegressionTolerancePercent &&
                    lowDelta >= KeepOnePercentLowThresholdPercent);
        if (!keep)
            Restore(game, change);

        return new TuneOutcome
        {
            Baseline = baseline,
            After = after,
            Change = change,
            Kept = keep,
            AverageFpsChangePercent = Math.Round(averageDelta, 1),
            OnePercentLowChangePercent = Math.Round(lowDelta, 1),
            Verdict = keep
                ? $"Kept: average FPS {Format(averageDelta)}, 1% low {Format(lowDelta)} — " +
                  "measured on this machine, not estimated."
                : $"Rolled back: average FPS {Format(averageDelta)}, 1% low {Format(lowDelta)} — " +
                  "the measured benefit did not justify the visual cost.",
        };
    }

    private void Restore(GameProfile game, PlannedChange change)
    {
        if (change.FilePath is { } path)
            _backups.RestoreLatest(game.Name, path);
    }

    private static double PercentChange(double before, double after) =>
        before > 0 ? (after / before - 1) * 100 : 0;

    private static string Format(double delta) => $"{(delta >= 0 ? "+" : "")}{delta:0.#}%";
}
