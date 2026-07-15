namespace Perfoptimize.Core.Diagnostics;

/// <summary>The kind of stability incident, from most game-relevant to least.</summary>
public enum IncidentKind
{
    /// <summary>An application terminated abnormally (Application Error / WER).</summary>
    AppCrash,
    /// <summary>An application stopped responding (Application Hang).</summary>
    AppHang,
    /// <summary>The graphics driver reset — a TDR ("stopped responding and recovered").</summary>
    GpuReset,
    /// <summary>A kernel bugcheck — the blue screen.</summary>
    BugCheck,
    /// <summary>The machine went down without a clean shutdown (Kernel-Power 41).</summary>
    UnexpectedShutdown,
    /// <summary>Windows ran out of memory (Resource-Exhaustion-Detector).</summary>
    OutOfMemory,
    /// <summary>A disk reported an I/O error.</summary>
    DiskError,
}

public enum IncidentSeverity { Info, Warning, Critical }

/// <summary>
/// A classified stability incident: what happened, when, which process, and —
/// the whole point — the likely cause and a concrete suggested fix, in the same
/// spirit as the recommendation engine. Purely a value object.
/// </summary>
public sealed class SystemIncident
{
    public IncidentKind Kind { get; init; }
    public IncidentSeverity Severity { get; init; }
    public DateTimeOffset? When { get; init; }
    /// <summary>The process involved, when the event names one (e.g. "Cyberpunk2077.exe").</summary>
    public string? Process { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string LikelyCause { get; init; } = "";
    public string SuggestedFix { get; init; } = "";
}

/// <summary>
/// A normalized event-log record, decoupled from WMI so the classifier can be
/// unit-tested without Windows. Built by <c>WindowsEventLogReader</c> on Windows.
/// </summary>
public sealed class IncidentRecord
{
    public string LogFile { get; init; } = "";
    public int EventCode { get; init; }
    public string Source { get; init; } = "";
    public DateTimeOffset? When { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> InsertionStrings { get; init; } = Array.Empty<string>();
}
