using System.Globalization;
using Perfoptimize.Core.Games;

namespace Perfoptimize.Core.Optimization;

public enum AdvisoryStatus
{
    Triggered,
    AlreadyOk,
    CannotVerify,
}

public sealed class AdvisoryContext
{
    public IReadOnlyDictionary<string, string> ConfigValues { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public BottleneckType Bottleneck { get; init; } = BottleneckType.Unknown;
    public bool? TargetMet { get; init; }
}

public sealed class EvaluatedAdvisory
{
    public required SettingAdvisory Advisory { get; init; }
    public AdvisoryStatus Status { get; init; }
    public bool HighPriority { get; init; }
    public string? CurrentValue { get; init; }
    public string Explanation { get; init; } = "";
}

/// <summary>
/// Evaluates only advisories that provide conditions. Historical advisories with
/// no conditions remain general profile guidance and are handled unchanged by
/// callers, preserving compatibility without pretending their current state was
/// inspected.
/// </summary>
public static class AdvisoryEvaluator
{
    public static EvaluatedAdvisory Evaluate(SettingAdvisory advisory, AdvisoryContext context)
    {
        if (advisory.Conditions.Count == 0)
        {
            return new EvaluatedAdvisory
            {
                Advisory = advisory,
                Status = AdvisoryStatus.CannotVerify,
                Explanation = "Avertissement général du profil : aucune condition de configuration n'est définie.",
            };
        }

        var currentValue = PrimaryConfigValue(advisory, context);
        var anyFalse = false;
        var anyUnknown = false;

        foreach (var condition in advisory.Conditions)
        {
            var result = EvaluateCondition(condition, context);
            if (result is null)
                anyUnknown = true;
            else if (!result.Value)
                anyFalse = true;
        }

        // A known false condition proves that the problematic combination is absent,
        // even if another input could not be read.
        if (anyFalse)
        {
            return new EvaluatedAdvisory
            {
                Advisory = advisory,
                Status = AdvisoryStatus.AlreadyOk,
                CurrentValue = currentValue,
                Explanation = "Les conditions problématiques ne sont pas réunies dans la configuration actuelle.",
            };
        }

        if (anyUnknown)
        {
            return new EvaluatedAdvisory
            {
                Advisory = advisory,
                Status = AdvisoryStatus.CannotVerify,
                CurrentValue = currentValue,
                Explanation = "Impossible de vérifier toutes les conditions de cet avertissement.",
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
                ? $"Configuration problématique détectée et pertinente pour le goulot {context.Bottleneck}."
                : "Configuration problématique détectée.",
        };
    }

    public static IReadOnlyList<EvaluatedAdvisory> EvaluateAll(
        IEnumerable<SettingAdvisory> advisories, AdvisoryContext context) =>
        advisories.Where(a => a.Conditions.Count > 0)
            .Select(a => Evaluate(a, context))
            .OrderByDescending(e => e.Status == AdvisoryStatus.Triggered)
            .ThenByDescending(e => e.HighPriority)
            .ToList();

    private static string? PrimaryConfigValue(SettingAdvisory advisory, AdvisoryContext context)
    {
        var condition = advisory.Conditions.FirstOrDefault(c => c.Key is not null);
        return condition is not null && context.ConfigValues.TryGetValue(LookupKey(condition), out var value)
            ? value
            : null;
    }

    private static string LookupKey(AdvisoryCondition condition) =>
        string.IsNullOrWhiteSpace(condition.Section)
            ? condition.Key!
            : $"{condition.Section}.{condition.Key}";

    private static bool? EvaluateCondition(AdvisoryCondition condition, AdvisoryContext context)
    {
        string? actual;
        if (condition.Key is not null)
        {
            if (!context.ConfigValues.TryGetValue(LookupKey(condition), out actual))
                return null;
        }
        else if (condition.System is not null)
        {
            actual = condition.System.ToLowerInvariant() switch
            {
                "bottleneck" => context.Bottleneck.ToString(),
                "targetmet" => context.TargetMet?.ToString(),
                _ => null,
            };
            if (actual is null)
                return null;
        }
        else
        {
            return null;
        }

        return Compare(actual, condition.Operator, condition.Value);
    }

    private static bool Compare(string actual, ConditionOperator op, string expected)
    {
        if (op == ConditionOperator.IsOn)
            return IsOn(actual);

        if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var left) &&
            double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var right))
        {
            return op switch
            {
                ConditionOperator.Equals => left == right,
                ConditionOperator.NotEquals => left != right,
                ConditionOperator.GreaterThan => left > right,
                ConditionOperator.GreaterThanOrEqual => left >= right,
                ConditionOperator.LessThan => left < right,
                ConditionOperator.LessThanOrEqual => left <= right,
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
        var trimmed = value.Trim();
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return number > 0;
        return trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
