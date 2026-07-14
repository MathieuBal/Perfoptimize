using System.Diagnostics;

namespace Perfoptimize.Core.Games;

/// <summary>A known game found among the running processes.</summary>
public sealed record RunningGame(GameProfile Profile, int ProcessId, string ProcessName);

/// <summary>
/// A game currently running: either a known one (with its profile) or an
/// unknown one recognised by heuristics (profile null, so no config apply, but
/// still monitorable and eligible for generic + hardware-aware advice).
/// </summary>
public sealed record DetectedGame(
    string Name, int ProcessId, GameProfile? Profile, bool IsKnown, IReadOnlyList<string> Reasons)
{
    /// <summary>A displayable game name from a process name ("eldenring" → "Eldenring").</summary>
    public static string PrettifyProcessName(string processName)
    {
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;
        return name.Length == 0 ? processName : char.ToUpperInvariant(name[0]) + name[1..];
    }
}

/// <summary>
/// Finds which known game is currently running by comparing process names with
/// game profiles. Only reads the process list — never touches game memory,
/// so anti-cheat systems have nothing to object to.
/// </summary>
public sealed class RunningGameDetector
{
    /// <summary>Pure matching logic, unit-testable without real processes.</summary>
    public static GameProfile? Match(string processName, IEnumerable<GameProfile> profiles) =>
        Match(processName, executablePath: null, profiles);

    /// <summary>
    /// Matches a process to a profile by executable name, disambiguating by the
    /// executable's full path when several profiles share that name. This is what
    /// separates Path of Exile 2 from Path of Exile 1: same "PathOfExile.exe", but
    /// the install folder ("...\Path of Exile 2\...") tells them apart. Without a
    /// path, a name collision falls back to the first profile carrying no
    /// <see cref="GameProfile.PathHint"/> (the base game), else the first match.
    /// </summary>
    public static GameProfile? Match(
        string processName, string? executablePath, IEnumerable<GameProfile> profiles)
    {
        var normalized = NormalizeExecutableName(processName);
        var byName = profiles
            .Where(p => p.Executables.Any(e =>
                NormalizeExecutableName(e).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (byName.Count <= 1)
            return byName.FirstOrDefault();

        // Name collision: prefer the most specific path hint that the executable
        // path actually contains (longest hint first, so "Path of Exile 2" beats
        // a hypothetical "Path of Exile").
        if (!string.IsNullOrEmpty(executablePath))
        {
            var byPath = byName
                .Where(p => !string.IsNullOrEmpty(p.PathHint) &&
                            executablePath.Contains(p.PathHint!, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.PathHint!.Length)
                .FirstOrDefault();
            if (byPath is not null)
                return byPath;
        }

        // No path evidence: fall back to the profile that claims no distinctive
        // path (the base game), rather than guessing a variant.
        return byName.FirstOrDefault(p => string.IsNullOrEmpty(p.PathHint)) ?? byName[0];
    }

    /// <summary>Scans running processes and returns the first known game found.</summary>
    public RunningGame? Detect(IReadOnlyList<GameProfile> profiles)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                // Pass the executable path so games that share a process name
                // (Path of Exile 1 vs 2) are told apart by their install folder.
                if (Match(process.ProcessName, TryGetExecutablePath(process), profiles) is { } profile)
                    return new RunningGame(profile, process.Id, process.ProcessName);
            }
            catch (InvalidOperationException)
            {
                // Process exited between enumeration and inspection.
            }
            finally
            {
                process.Dispose();
            }
        }
        return null;
    }

    /// <summary>
    /// Detects the running game: a known profile first, otherwise the best
    /// heuristic candidate among running processes (unknown game). Returns null
    /// when nothing game-like is running.
    /// </summary>
    public DetectedGame? DetectAny(IReadOnlyList<GameProfile> profiles)
    {
        if (Detect(profiles) is { } known)
            return new DetectedGame(known.Profile.Name, known.ProcessId, known.Profile, IsKnown: true, []);

        DetectedGame? best = null;
        var bestScore = GameHeuristics.LikelyThreshold - 1;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var likelihood = GameHeuristics.Evaluate(
                    new ProcessInfo(process.ProcessName, TryGetExecutablePath(process)));
                if (likelihood.IsLikelyGame && likelihood.Score > bestScore)
                {
                    bestScore = likelihood.Score;
                    best = new DetectedGame(
                        DetectedGame.PrettifyProcessName(process.ProcessName),
                        process.Id, Profile: null, IsKnown: false, likelihood.Reasons);
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited between enumeration and inspection.
            }
            finally
            {
                process.Dispose();
            }
        }
        return best;
    }

    /// <summary>Reading the executable path can throw for protected processes.</summary>
    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"Cyberpunk2077.exe" and "cyberpunk2077" both normalize to "cyberpunk2077".</summary>
    private static string NormalizeExecutableName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
