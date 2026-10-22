using Perfoptimize.Core.Configs;

namespace Perfoptimize.Core.Games;

/// <summary>
/// Knowledge about one game: how to recognize it (process names) and how its
/// graphics settings typically trade visual quality for performance.
/// Profiles are plain JSON files so the community can refine them over time.
/// </summary>
public sealed class GameProfile
{
    public string Name { get; init; } = "Unknown";
    public IReadOnlyList<string> Executables { get; init; } = [];
    public string? PathHint { get; init; }
    public IReadOnlyList<GameSetting> Settings { get; init; } = [];
    public IReadOnlyList<GameConfigFile> ConfigFiles { get; init; } = [];
    public IReadOnlyList<string>? Upscalers { get; init; }
    public int? FrameCapFps { get; init; }
    public IReadOnlyList<SettingAdvisory> Advisories { get; init; } = [];
}

public enum AdvisoryKind
{
    Crash,
    Stutter,
    PoorValue,
}

/// <summary>Comparison used by a contextual advisory condition.</summary>
public enum ConditionOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    IsOn,
}

/// <summary>
/// One condition on either a config key or a measured/system value. Existing
/// profiles can omit conditions and retain their historical general advisory.
/// </summary>
public sealed class AdvisoryCondition
{
    public string? Key { get; init; }
    public string? Section { get; init; }
    public string? System { get; init; }
    public ConditionOperator Operator { get; init; }
    public string Value { get; init; } = "";
}

public sealed class AdvisorySource
{
    public string Url { get; init; } = "";
    public string? Note { get; init; }
}

/// <summary>
/// A curated warning. The original fields remain unchanged for backwards
/// compatibility; optional conditions/provenance make newer advisories
/// verifiable against the player's actual configuration.
/// </summary>
public sealed class SettingAdvisory
{
    public string Id { get; init; } = "";
    public string Setting { get; init; } = "";
    public AdvisoryKind Kind { get; init; }
    public IReadOnlyList<AdvisoryCondition> Conditions { get; init; } = [];
    public IReadOnlyList<AdvisoryCondition> PriorityWhen { get; init; } = [];
    public string Recommendation { get; init; } = "";
    public string? TargetValue { get; init; }
    public string Detail { get; init; } = "";
    public EvidenceLevel Evidence { get; init; } = EvidenceLevel.CommunityValidated;
    public IReadOnlyList<AdvisorySource> Sources { get; init; } = [];
    public string? LastVerified { get; init; }
    public IReadOnlyList<string> GameVersions { get; init; } = [];
    public bool AutomaticApplication { get; init; }
}

public enum SettingImpact
{
    Gpu,
    Cpu,
    Vram,
    Ram,
    Mixed,
}

public enum SettingAction
{
    Lower,
    Disable,
    KeepEnabled,
    TestAlternatives,
    ManualOnly,
}

public enum EvidenceLevel
{
    Estimated,
    CommunityValidated,
    Benchmarked,
    LocallyMeasured,
}

public sealed class GameSetting
{
    public string Name { get; init; } = "Unknown";
    public SettingImpact Impact { get; init; }
    public SettingAction Action { get; init; } = SettingAction.Lower;
    public EvidenceLevel Evidence { get; init; } = EvidenceLevel.Estimated;
    public int QualityLoss { get; init; }
    public int PerformanceGain { get; init; }
    public int EstimatedGainPercent { get; init; }
    public IReadOnlyDictionary<string, double>? VramGiBByLevel { get; init; }
    public SettingBinding? Config { get; init; }
    public bool IsAutoLowerable => Action is SettingAction.Lower or SettingAction.Disable;
}

public sealed class SettingBinding
{
    public string File { get; init; } = "";
    public string Section { get; init; } = "";
    public string Key { get; init; } = "";
    public IReadOnlyList<string> Levels { get; init; } = [];
}
