using System.Security.Cryptography;
using System.Text;

namespace Perfoptimize.Core.Startup;

/// <summary>How a startup entry is turned off (and turned back on).</summary>
public enum StartupDisableMethod
{
    /// <summary>Delete the value from a …\CurrentVersion\Run key; restore re-writes it.</summary>
    Registry,
    /// <summary>Move the file out of the Startup folder; restore moves it back.</summary>
    StartupFile,
    /// <summary>schtasks /Change /DISABLE; restore /ENABLE.</summary>
    ScheduledTask,
}

/// <summary>
/// Pure helpers for identifying a startup entry and deciding how it would be
/// disabled. Kept free of I/O so the identity is stable and unit-testable.
/// </summary>
public static class StartupIdentity
{
    /// <summary>
    /// A stable, deterministic id for an entry, derived from what actually names
    /// it (location + name + command). The same entry always hashes to the same
    /// id across scans, so the UI can disable/restore it by id.
    /// </summary>
    public static string Compute(StartupLocation location, string name, string command)
    {
        var raw = $"{location}|{name}|{command}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant(); // 16 hex chars
    }

    /// <summary>Which mechanism disables an entry at this location.</summary>
    public static StartupDisableMethod MethodFor(StartupLocation location) => location switch
    {
        StartupLocation.RegistryRunUser => StartupDisableMethod.Registry,
        StartupLocation.RegistryRunMachine => StartupDisableMethod.Registry,
        StartupLocation.StartupFolder => StartupDisableMethod.StartupFile,
        StartupLocation.ScheduledTask => StartupDisableMethod.ScheduledTask,
        _ => StartupDisableMethod.Registry,
    };

    /// <summary>
    /// Whether disabling this entry needs administrator rights. Machine-wide Run
    /// keys and scheduled tasks are protected; the current-user key and the
    /// per-user Startup folder are not.
    /// </summary>
    public static bool NeedsElevation(StartupLocation location) => location switch
    {
        StartupLocation.RegistryRunMachine => true,
        StartupLocation.ScheduledTask => true,
        _ => false,
    };
}
