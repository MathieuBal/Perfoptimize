namespace Perfoptimize.Core.Startup;

/// <summary>
/// Everything needed to put a disabled startup entry back exactly as it was.
/// Written before the entry is touched — the golden rule, same as the game
/// config backups: never disable anything without a way back.
/// </summary>
public sealed record StartupBackup
{
    /// <summary>Matches the entry's <see cref="StartupEntry.Id"/>.</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public StartupLocation Location { get; init; }
    public StartupDisableMethod Method { get; init; }
    public DateTimeOffset DisabledAt { get; init; }

    /// <summary>Registry: the sub-key path the value was deleted from.</summary>
    public string? RegistryKeyPath { get; init; }
    /// <summary>Registry: the value name that was deleted.</summary>
    public string? RegistryValueName { get; init; }
    /// <summary>Registry: the value kind, so restore re-writes it with the same type.</summary>
    public string? RegistryValueKind { get; init; }

    /// <summary>StartupFile: where the file lived before it was moved aside.</summary>
    public string? OriginalPath { get; init; }
    /// <summary>StartupFile: where it now sits, ready to be moved back.</summary>
    public string? DisabledPath { get; init; }

    /// <summary>ScheduledTask: the task path passed to schtasks (e.g. \Vendor\Update).</summary>
    public string? TaskPath { get; init; }
}
