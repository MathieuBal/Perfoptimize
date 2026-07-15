using System.Management;
using System.Runtime.Versioning;
using Perfoptimize.Core.Hardware.Windows;

namespace Perfoptimize.Core.Diagnostics;

/// <summary>
/// Reads recent stability events from the Windows event log via WMI
/// (<c>Win32_NTLogEvent</c>), reusing the bounded <see cref="WmiQuery"/> helper
/// so a slow query can never stall the app. Only the Application and System
/// logs are touched, and only the specific event codes that map to a crash,
/// hang, GPU reset, bugcheck, unexpected shutdown, memory exhaustion or disk
/// error. Purely read-only — nothing is modified. Never throws.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsEventLogReader
{
    /// <summary>Reads incident-bearing events generated within the given window.</summary>
    public IReadOnlyList<IncidentRecord> ReadRecent(TimeSpan window)
    {
        var records = new List<IncidentRecord>();
        string cutoff;
        try
        {
            cutoff = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now - window);
        }
        catch
        {
            return records;
        }

        // Application log: crashes (1000), hangs (1002), Windows Error Reporting (1001).
        records.AddRange(Query("Application", "EventCode=1000 OR EventCode=1002 OR EventCode=1001", cutoff));
        // System log: kernel-power (41), bugcheck (1001), GPU TDR (4101),
        // resource exhaustion (2004), and common disk error codes.
        records.AddRange(Query("System",
            "EventCode=41 OR EventCode=1001 OR EventCode=4101 OR EventCode=2004 OR " +
            "EventCode=7 OR EventCode=9 OR EventCode=11 OR EventCode=51 OR EventCode=52 OR EventCode=153",
            cutoff));

        return records;
    }

    private static IEnumerable<IncidentRecord> Query(string logFile, string codeFilter, string cimCutoff)
    {
        var wql =
            "SELECT EventCode, SourceName, TimeGenerated, Message, InsertionStrings, Logfile " +
            $"FROM Win32_NTLogEvent WHERE Logfile='{logFile}' AND ({codeFilter}) " +
            $"AND TimeGenerated >= '{cimCutoff}'";

        foreach (var row in WmiQuery.Run(wql))
        {
            DateTimeOffset? when = null;
            if (row.GetValueOrDefault("TimeGenerated") is string ts)
            {
                try { when = ManagementDateTimeConverter.ToDateTime(ts); }
                catch { /* leave unset if the DMTF string is malformed */ }
            }

            yield return new IncidentRecord
            {
                LogFile = row.GetValueOrDefault("Logfile") as string ?? logFile,
                EventCode = WmiQuery.ToInt(row.GetValueOrDefault("EventCode")) ?? 0,
                Source = row.GetValueOrDefault("SourceName") as string ?? "",
                When = when,
                Message = row.GetValueOrDefault("Message") as string,
                InsertionStrings = row.GetValueOrDefault("InsertionStrings") as string[] ?? Array.Empty<string>(),
            };
        }
    }
}
