namespace Perfoptimize.Core.Configs;

public enum ConfigFormat
{
    Ini,
    Json,
    Xml,
}

/// <summary>
/// A game configuration file the assistant is allowed to read (and later,
/// modify while the game is closed). Paths use placeholders such as
/// %DOCUMENTS% or %LOCALAPPDATA%, expanded by <see cref="ConfigPathResolver"/>.
/// </summary>
public sealed class GameConfigFile
{
    public string Path { get; init; } = "";
    public ConfigFormat Format { get; init; }

    /// <summary>Expanded absolute path on this machine.</summary>
    public string ResolvePath() => ConfigPathResolver.Resolve(Path);
}
