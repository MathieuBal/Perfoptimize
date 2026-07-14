namespace Perfoptimize.Core.Games;

/// <summary>A running process reduced to what game heuristics need.</summary>
public readonly record struct ProcessInfo(string Name, string? ExecutablePath);

/// <summary>How likely a process is a game, with the reasons why.</summary>
public sealed class GameLikelihood
{
    public bool IsLikelyGame { get; init; }
    public int Score { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
}

/// <summary>
/// Recognises that an unknown process is probably a game — so the assistant can
/// monitor and advise on games that have no built-in profile. The strongest
/// signal is the install location (a game-platform folder); a denylist keeps
/// launchers, browsers and dev tools out. Pure and unit-tested.
/// </summary>
public static class GameHeuristics
{
    /// <summary>Score at or above which a process is treated as a game.</summary>
    public const int LikelyThreshold = 3;

    // Path fragments that strongly indicate a game install.
    private static readonly string[] PlatformFolders =
    [
        "steamapps", "steamlibrary", "epic games", "gog galaxy", "goggames",
        "ubisoft", "origin games", "ea games", "ea\\", "riot games",
        "battle.net", "games\\", "\\game\\",
    ];

    // Processes that live in game folders or look game-ish but are not games.
    private static readonly HashSet<string> NonGameNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "steamservice", "epicgameslauncher", "epicwebhelper",
        "galaxyclient", "gog galaxy", "upc", "ubisoftconnect", "origin", "eadesktop",
        "eabackgroundservice", "riotclientservices", "battle.net", "explorer", "chrome",
        "firefox", "msedge", "iexplore", "brave", "opera", "discord", "spotify", "code",
        "devenv", "obs64", "obs32", "obs", "cmd", "powershell", "pwsh", "dotnet", "node",
        "python", "conhost", "svchost", "taskmgr", "notepad", "wallpaper32", "wallpaper64",
        "msiafterburner", "rtss", "nvcontainer", "nvidia web helper", "razer synapse",
        "logi_", "crashhandler", "crashreporter", "unitycrashhandler64", "handbrake",
    };

    // Executable paths that are never games.
    private static readonly string[] SystemPathFragments =
    [
        "\\windows\\", "\\system32\\", "\\syswow64\\", "\\microsoft\\edge\\",
        "\\common files\\", "\\windows defender\\",
    ];

    public static GameLikelihood Evaluate(ProcessInfo process)
    {
        var reasons = new List<string>();
        var name = StripExe(process.Name);

        if (NonGameNames.Contains(name) || NonGameNames.Any(n =>
                name.StartsWith(n, StringComparison.OrdinalIgnoreCase) && n.EndsWith('_')))
        {
            return new GameLikelihood { IsLikelyGame = false, Score = 0, Reasons = ["Known non-game process"] };
        }

        var path = process.ExecutablePath?.Replace('/', '\\');
        var score = 0;

        if (path is not null && SystemPathFragments.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return new GameLikelihood { IsLikelyGame = false, Score = 0, Reasons = ["System / OS executable"] };

        if (path is not null)
        {
            var folder = PlatformFolders.FirstOrDefault(f => path.Contains(f, StringComparison.OrdinalIgnoreCase));
            if (folder is not null)
            {
                score += 3;
                reasons.Add($"Installed under a game folder ({folder.Trim('\\')})");
            }
        }

        // A crash handler / setup / launcher shipped inside a game folder is not the game.
        if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("installer", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("redist", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("unins", StringComparison.OrdinalIgnoreCase))
        {
            score -= 3;
            reasons.Add("Looks like a launcher/installer, not the game");
        }

        return new GameLikelihood
        {
            IsLikelyGame = score >= LikelyThreshold,
            Score = score,
            Reasons = reasons,
        };
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
