using System.Globalization;
using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Optimization;

/// <summary>Result of checking an advisory against the actual configuration.</summary>
public enum AdvisoryStatus
{
    /// <summary>The problematic setting is active AND relevant here.</summary>
    Triggered,
    /// <summary>Nothing to do: the setting is off, or harmless in this context.</summary>
    AlreadyOk,
    /// <summary>The setting's current value could not be read.</summary>
    CannotVerify,
}

/// <summary>Everything the evaluator needs about the player's machine and session.</summary>
public sealed class AdvisoryContext
{
    /// <summary>Current config values, key → value (case-insensitive).</summary>
    public IReadOnlyDictionary<string, string> ConfigValues { get; init; } =
        new Dictionary<string, string>();
    public int? ResolutionWidth { get; init; }
    public int? ResolutionHeight { get; init; }
    public double? VramGiB { get; init; }
    public BottleneckType Bottleneck { get; init; } = BottleneckType.Unknown;
    /// <summary>True when the measured FPS already meets the target; null when unknown.</summary>
    public bool? TargetMet { get; init; }
}

/// <summary>An advisory evaluated in context, ready to display.</summary>
public sealed class EvaluatedAdvisory
{
    public required SettingAdvisory Advisory { get; init; }
    public AdvisoryStatus Status { get; init; }
    public bool HighPriority { get; init; }
    /// <summary>The value read from the config for the flagged setting, if any.</summary>
    public string? CurrentValue { get; init; }
    /// <summary>Human summary of the evaluation outcome.</summary>
    public string Explanation { get; init; } = "";
}

/// <summary>
/// Crosses a game's community advisories with the player's ACTUAL settings,
/// hardware and measured bottleneck — so the assistant only raises an alarm
/// when a known-bad setting is genuinely active and relevant, tells the player
/// when something is already fine, and admits when it cannot tell. Pure.
/// </summary>
public static class AdvisoryEvaluator
{
    public static EvaluatedAdvisory Evaluate(SettingAdvisory advisory, AdvisoryContext context)
    {
        var currentValue = PrimaryConfigValue(advisory, context);

        // Evaluate each trigger condition to true / false / unknown.
        var anyUnknown = false;
        var allTrue = true;
        foreach (var condition in advisory.Conditions)
        {
            var result = EvaluateCondition(condition, context);
            if (result is null)
            {
                // Only a CONFIG-key condition that can't be read makes the whole
                // advisory unverifiable; a missing system fact just fails safe.
                if (condition.Key is not null)
                    anyUnknown = true;
                allTrue = false;
            }
            else if (!result.Value)
            {
                allTrue = false;
            }
        }

        if (anyUnknown && !HasReadableTriggerThatIsFalse(advisory, context))
        {
            return new EvaluatedAdvisory
            {
                Advisory = advisory,
                Status = AdvisoryStatus.CannotVerify,
                CurrentValue = currentValue,
                Explanation = $"Impossible de lire la valeur de « {advisory.Setting} » dans ta configuration.",
            };
        }

        if (!allTrue)
        {
            return new EvaluatedAdvisory
            {
                Advisory = advisory,
                Status = AdvisoryStatus.AlreadyOk,
                CurrentValue = currentValue,
                Explanation = currentValue is not null
                    ? $"« {advisory.Setting} » = {currentValue} : rien à corriger dans ton contexte."
                    : $"« {advisory.Setting} » ne pose pas de problème dans ton contexte.",
            };
        }

        var highPriority = advisory.PriorityWhen.Count > 0 &&
                           advisory.PriorityWhen.All(c => EvaluateCondition(c, context) == true);
        return new EvaluatedAdvisory
        {
            Advisory = advisory,
            Status = AdvisoryStatus.Triggered,
            HighPriority = highPriority,
            CurrentValue = currentValue,
            Explanation = highPriority
                ? $"Réglage problématique actif ET pertinent pour ton goulot ({context.Bottleneck}) — à corriger en priorité."
                : "Réglage problématique détecté dans ta configuration.",
        };
    }

    public static IReadOnlyList<EvaluatedAdvisory> EvaluateAll(
        IEnumerable<SettingAdvisory> advisories, AdvisoryContext context)
    {
        return advisories
            .Select(a => Evaluate(a, context))
            // Triggered first, high priority above that; then AlreadyOk, then CannotVerify.
            .OrderByDescending(e => e.Status == AdvisoryStatus.Triggered)
            .ThenByDescending(e => e.HighPriority)
            .ToList();
    }

    /// <summary>The value the flagged setting currently has, from its first config condition.</summary>
    private static string? PrimaryConfigValue(SettingAdvisory advisory, AdvisoryContext context)
    {
        var key = advisory.Conditions.FirstOrDefault(c => c.Key is not null)?.Key;
        return key is not null && context.ConfigValues.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>A readable config trigger that evaluates false means "already ok", not "unknown".</summary>
    private static bool HasReadableTriggerThatIsFalse(SettingAdvisory advisory, AdvisoryContext context) =>
        advisory.Conditions.Any(c =>
            c.Key is not null && context.ConfigValues.ContainsKey(c.Key) &&
            EvaluateCondition(c, context) == false);

    /// <summary>Evaluates a condition to true/false, or null when its input is unavailable.</summary>
    private static bool? EvaluateCondition(AdvisoryCondition condition, AdvisoryContext context)
    {
        string? actual = null;
        if (condition.Key is not null)
        {
            if (!context.ConfigValues.TryGetValue(condition.Key, out var v))
                return null;
            actual = v;
        }
        else if (condition.System is not null)
        {
            actual = SystemValue(condition.System, context);
            if (actual is null)
                return null;
        }
        else
        {
            return null;
        }

        return Compare(actual, condition.Operator, condition.Value);
    }

    private static string? SystemValue(string system, AdvisoryContext context) => system.ToLowerInvariant() switch
    {
        "resolutionwidth" => context.ResolutionWidth?.ToString(CultureInfo.InvariantCulture),
        "resolutionheight" => context.ResolutionHeight?.ToString(CultureInfo.InvariantCulture),
        "vramgib" => context.VramGiB?.ToString(CultureInfo.InvariantCulture),
        "bottleneck" => context.Bottleneck.ToString(),
        "targetmet" => context.TargetMet?.ToString(),
        _ => null,
    };

    private static bool Compare(string actual, ConditionOperator op, string expected)
    {
        if (op == ConditionOperator.IsOn)
            return IsOn(actual);

        // Numeric comparison when both sides are numbers; textual otherwise.
        if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) &&
            double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var e))
        {
            return op switch
            {
                ConditionOperator.Equals => a == e,
                ConditionOperator.NotEquals => a != e,
                ConditionOperator.GreaterThan => a > e,
                ConditionOperator.GreaterThanOrEqual => a >= e,
                ConditionOperator.LessThan => a < e,
                ConditionOperator.LessThanOrEqual => a <= e,
                _ => false,
            };
        }

        return op switch
        {
            ConditionOperator.Equals => actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotEquals => !actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool IsOn(string value)
    {
        var v = value.Trim();
        if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
            return n > 0;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
