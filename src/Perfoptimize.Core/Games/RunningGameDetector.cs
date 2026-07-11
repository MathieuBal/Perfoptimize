using System.Diagnostics;

namespace Perfoptimize.Core.Games;

/// <summary>A known game found among the running processes.</summary>
public sealed record RunningGame(GameProfile Profile, int ProcessId, string ProcessName);

/// <summary>
/// Finds which known game is currently running by comparing process names with
/// game profiles. Only reads the process list — never touches game memory,
/// so anti-cheat systems have nothing to object to.
/// </summary>
public sealed class RunningGameDetector
{
    /// <summary>Pure matching logic, unit-testable without real processes.</summary>
    public static GameProfile? Match(string processName, IEnumerable<GameProfile> profiles)
    {
        var normalized = NormalizeExecutableName(processName);
        return profiles.FirstOrDefault(p =>
            p.Executables.Any(e => NormalizeExecutableName(e).Equals(normalized, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Scans running processes and returns the first known game found.</summary>
    public RunningGame? Detect(IReadOnlyList<GameProfile> profiles)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (Match(process.ProcessName, profiles) is { } profile)
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

    /// <summary>"Cyberpunk2077.exe" and "cyberpunk2077" both normalize to "cyberpunk2077".</summary>
    private static string NormalizeExecutableName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
