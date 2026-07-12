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

/// <summary>The keep-or-restore decision, separated from any file I/O so the
/// two-phase workflow (prepare/finish) and simulations reuse the same rules.</summary>
public sealed class TuneDecision
{
    public bool Keep { get; init; }
    public double AverageFpsChangePercent { get; init; }
    public double OnePercentLowChangePercent { get; init; }
    public string Verdict { get; init; } = "";
}

/// <summary>
/// The measured optimization loop: apply ONE change, re-measure in the same
/// scene, and keep it only if the numbers actually improved on THIS machine.
/// Any failure — including an exception during the second measurement — rolls
/// the change back from the exact backup taken by this run.
/// </summary>
public sealed class OptimizationLoop
{
    /// <summary>Minimum average-FPS improvement (percent) for a change to be kept.</summary>
    private const double KeepAverageThresholdPercent = 3;
    /// <summary>A 1% low improvement this large keeps a change even with flat averages.</summary>
    private const double KeepOnePercentLowThresholdPercent = 8;
    /// <summary>An average regression beyond this always rolls the change back.</summary>
    private const double AverageRegressionLimitPercent = -1;
    /// <summary>A 1% low regression beyond this always rolls the change back,
    /// even when the average improved — smoothness beats raw numbers.</summary>
    private const double OnePercentLowRegressionLimitPercent = -5;
    /// <summary>Maximum tolerated increase of the stutter share (percentage points).</summary>
    private const double StutterIncreaseLimitPoints = 2;

    private readonly SettingsApplier _applier;

    public OptimizationLoop(SettingsApplier applier) => _applier = applier;

    /// <summary>Pure keep-or-restore rule from two measurements.</summary>
    public static TuneDecision Decide(FrametimeStatistics baseline, FrametimeStatistics after)
    {
        var averageDelta = PercentChange(baseline.AverageFps, after.AverageFps);
        var lowDelta = PercentChange(baseline.OnePercentLowFps, after.OnePercentLowFps);
        var stutterDelta = after.StutterPercent - baseline.StutterPercent;

        // A change that trades stutter for average FPS is a bad trade.
        var severeRegression = averageDelta < AverageRegressionLimitPercent ||
                               lowDelta < OnePercentLowRegressionLimitPercent ||
                               stutterDelta > StutterIncreaseLimitPoints;
        var keep = !severeRegression &&
                   (averageDelta >= KeepAverageThresholdPercent ||
                    lowDelta >= KeepOnePercentLowThresholdPercent);

        string verdict;
        if (keep)
        {
            verdict = $"Kept: average FPS {Format(averageDelta)}, 1% low {Format(lowDelta)} — " +
                      "measured on this machine, not estimated.";
        }
        else if (severeRegression && averageDelta >= KeepAverageThresholdPercent)
        {
            verdict = $"Rolled back despite average FPS {Format(averageDelta)}: 1% low {Format(lowDelta)}, " +
                      $"stutter {Format(stutterDelta)} pts — smoothness got worse, and stutter is felt " +
                      "more than averages.";
        }
        else
        {
            verdict = $"Rolled back: average FPS {Format(averageDelta)}, 1% low {Format(lowDelta)} — " +
                      "the measured benefit did not justify the visual cost.";
        }

        return new TuneDecision
        {
            Keep = keep,
            AverageFpsChangePercent = Math.Round(averageDelta, 1),
            OnePercentLowChangePercent = Math.Round(lowDelta, 1),
            Verdict = verdict,
        };
    }

    /// <summary>
    /// Runs one iteration: applies <paramref name="settingToLower"/>, has the
    /// caller re-measure via <paramref name="measureAfter"/> (same scene!), and
    /// keeps or rolls back based on the numbers. Returns null when the setting
    /// could not be applied (no binding, value out of ladder, ...).
    /// </summary>
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

        var kept = false;
        try
        {
            // If measureAfter throws (missing file, cancellation, crash), the
            // finally block restores before the exception reaches the caller:
            // an unverifiable change never stays.
            var after = await measureAfter();

            if (after is null)
            {
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

            var decision = Decide(baseline, after);
            kept = decision.Keep;
            return new TuneOutcome
            {
                Baseline = baseline,
                After = after,
                Change = change,
                Kept = decision.Keep,
                AverageFpsChangePercent = decision.AverageFpsChangePercent,
                OnePercentLowChangePercent = decision.OnePercentLowChangePercent,
                Verdict = decision.Verdict,
            };
        }
        finally
        {
            if (!kept)
                Restore(applyResult);
        }
    }

    /// <summary>
    /// Rolls back the experiment: surgically reverts only the tested key to its
    /// observed value (the game may have legitimately rewritten other keys in
    /// the meantime), falling back to the exact backup taken by this run —
    /// never "the latest backup", which could belong to another operation.
    /// </summary>
    private static void Restore(ApplyResult applyResult)
    {
        foreach (var change in applyResult.Changes.Where(c => c.Status == PlannedChangeStatus.Ready))
        {
            var restored =
                change is { FilePath: { } path, CurrentValue: { } original } &&
                change.Setting.Config is { } binding &&
                SurgicalRestore.TryRestoreKey(path, binding.Section, binding.Key, original);

            if (!restored &&
                change.FilePath is { } file &&
                applyResult.BackupByFile.TryGetValue(file, out var backupPath) &&
                File.Exists(backupPath))
            {
                File.Copy(backupPath, file, overwrite: true);
            }
        }
    }

    private static double PercentChange(double before, double after) =>
        before > 0 ? (after / before - 1) * 100 : 0;

    private static string Format(double delta) => $"{(delta >= 0 ? "+" : "")}{delta:0.#}%";
}
