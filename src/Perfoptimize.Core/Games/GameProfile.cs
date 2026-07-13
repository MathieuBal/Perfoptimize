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
    /// <summary>Process executable names, with or without ".exe", case-insensitive.</summary>
    public IReadOnlyList<string> Executables { get; init; } = [];
    public IReadOnlyList<GameSetting> Settings { get; init; } = [];
    /// <summary>Configuration files the assistant may read (and back up).</summary>
    public IReadOnlyList<GameConfigFile> ConfigFiles { get; init; } = [];
    /// <summary>
    /// Upscalers the game ships: "dlss", "fsr", "xess", "dlss-fg", "fsr-fg".
    /// Null means unknown (generic advice allowed); an explicit empty list means
    /// the game has none, so upscaling must never be recommended for it.
    /// </summary>
    public IReadOnlyList<string>? Upscalers { get; init; }
    /// <summary>Hard engine frame cap (e.g. 60 for Elden Ring); null when uncapped.</summary>
    public int? FrameCapFps { get; init; }
}

/// <summary>Which resource a graphics setting mostly consumes.</summary>
public enum SettingImpact
{
    Gpu,
    Cpu,
    Vram,
    Ram,
    /// <summary>Setting that stresses several components (e.g. draw distance).</summary>
    Mixed,
}

/// <summary>
/// What to do with a setting to gain performance. Crucial: not every setting is
/// "lower for FPS" — some (engine multithreading) HELP when left on, and some
/// can only be judged by measurement. The recommendation engine must respect
/// this instead of blindly lowering everything.
/// </summary>
public enum SettingAction
{
    /// <summary>Lower it by one step to gain FPS (the common case).</summary>
    Lower,
    /// <summary>Turn it off entirely (ray tracing, HairWorks).</summary>
    Disable,
    /// <summary>Leave it ON — lowering/disabling it usually hurts performance.</summary>
    KeepEnabled,
    /// <summary>Only a measured A/B test can decide (e.g. DX11 vs DX12 vs Vulkan).</summary>
    TestAlternatives,
    /// <summary>Change it in the game's menu; never applied automatically.</summary>
    ManualOnly,
}

/// <summary>How trustworthy a setting's expected gain is.</summary>
public enum EvidenceLevel
{
    /// <summary>Order-of-magnitude guess from the profile — not verified.</summary>
    Estimated,
    /// <summary>Commonly recommended by the game's community.</summary>
    CommunityValidated,
    /// <summary>From published benchmarks.</summary>
    Benchmarked,
    /// <summary>Measured on THIS machine (via the tune loop).</summary>
    LocallyMeasured,
}

public sealed class GameSetting
{
    public string Name { get; init; } = "Unknown";
    public SettingImpact Impact { get; init; }
    /// <summary>What to do with this setting to gain performance.</summary>
    public SettingAction Action { get; init; } = SettingAction.Lower;
    /// <summary>How trustworthy this setting's expected gain is.</summary>
    public EvidenceLevel Evidence { get; init; } = EvidenceLevel.Estimated;
    /// <summary>Visual cost of lowering this setting, from 0 (invisible) to 5 (very visible).</summary>
    public int QualityLoss { get; init; }
    /// <summary>Typical FPS gain of lowering this setting, from 0 (none) to 5 (major).</summary>
    public int PerformanceGain { get; init; }
    /// <summary>
    /// Typical FPS gain (in percent) of lowering this setting by one step, from
    /// public benchmarks; 0 when unquantified. Community data refines these.
    /// </summary>
    public int EstimatedGainPercent { get; init; }
    /// <summary>
    /// For texture-style settings: approximate VRAM needed per quality tier in
    /// GiB (e.g. {"Low": 4, "Medium": 5, "High": 7}), used to pick the highest
    /// tier that fits the player's card.
    /// </summary>
    public IReadOnlyDictionary<string, double>? VramGiBByLevel { get; init; }
    /// <summary>Where this setting lives in the game's config files; null when unknown.</summary>
    public SettingBinding? Config { get; init; }

    /// <summary>A setting the engine may recommend lowering/disabling for FPS.</summary>
    public bool IsAutoLowerable => Action is SettingAction.Lower or SettingAction.Disable;
}

/// <summary>
/// Maps a <see cref="GameSetting"/> to a concrete key in one of the game's
/// config files, with the ordered list of values the game accepts.
/// </summary>
public sealed class SettingBinding
{
    /// <summary>File name (or path suffix) matching one of the profile's config files.</summary>
    public string File { get; init; } = "";
    /// <summary>INI section; empty for keys outside any section.</summary>
    public string Section { get; init; } = "";
    public string Key { get; init; } = "";
    /// <summary>
    /// Accepted values ordered from lowest to highest quality, e.g.
    /// ["0","1","2","3","4"] — "lower by one step" moves one position left.
    /// </summary>
    public IReadOnlyList<string> Levels { get; init; } = [];
}
