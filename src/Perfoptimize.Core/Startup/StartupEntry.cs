namespace Perfoptimize.Core.Startup;

/// <summary>Where an auto-run entry comes from.</summary>
public enum StartupLocation
{
    /// <summary>HKCU …\CurrentVersion\Run — starts for the current user.</summary>
    RegistryRunUser,
    /// <summary>HKLM …\CurrentVersion\Run — starts for every user.</summary>
    RegistryRunMachine,
    /// <summary>A shortcut/executable in a Startup folder.</summary>
    StartupFolder,
    /// <summary>A non-Microsoft scheduled task.</summary>
    ScheduledTask,
}

/// <summary>
/// One program that runs on its own (at logon or on a schedule). The likely
/// culprit behind a background app that "flashes a window and freezes the PC"
/// lives in this list.
/// </summary>
public sealed class StartupEntry
{
    public string Name { get; init; } = "";
    /// <summary>The command line it runs.</summary>
    public string Command { get; init; } = "";
    public StartupLocation Location { get; init; }
    /// <summary>Where exactly it was found (registry key, folder path, task path).</summary>
    public string? Detail { get; init; }
    /// <summary>Set when this entry matches a process seen "not responding" recently.</summary>
    public bool LinkedToHang { get; set; }

    /// <summary>Stable id used to disable / restore this entry from the UI.</summary>
    public string Id => StartupIdentity.Compute(Location, Name, Command);
}
