namespace Perfoptimize.Core.Monitoring.Linux;

/// <summary>Aggregate CPU tick counters from the first line of /proc/stat.</summary>
public readonly record struct CpuTicks(long IdleTicks, long TotalTicks);

/// <summary>Pure parsers for the Linux telemetry sources, unit-testable with fixtures.</summary>
public static class LinuxTelemetryParser
{
    /// <summary>
    /// Parses the aggregate "cpu " line of /proc/stat:
    /// cpu  user nice system idle iowait irq softirq steal [guest guest_nice]
    /// Idle time counts both "idle" and "iowait"; guest columns are already
    /// included in "user"/"nice" and must not be re-added.
    /// </summary>
    public static CpuTicks? ParseProcStatCpuLine(string procStatText)
    {
        var line = procStatText.Split('\n')
            .FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
            return null;

        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 5)
            return null;

        var values = new List<long>();
        foreach (var field in fields.Skip(1).Take(8))
        {
            if (!long.TryParse(field, out var value))
                return null;
            values.Add(value);
        }

        var idle = values[3] + (values.Count > 4 ? values[4] : 0);
        return new CpuTicks(idle, values.Sum());
    }

    /// <summary>CPU usage (0-100) between two /proc/stat readings; null when indeterminate.</summary>
    public static double? CpuUsageBetween(CpuTicks previous, CpuTicks current)
    {
        var totalDelta = current.TotalTicks - previous.TotalTicks;
        var idleDelta = current.IdleTicks - previous.IdleTicks;
        if (totalDelta <= 0)
            return null;
        var usage = 100.0 * (totalDelta - idleDelta) / totalDelta;
        return Math.Clamp(usage, 0, 100);
    }

    /// <summary>
    /// Parses every per-core "cpuN" line of /proc/stat, keyed by core name.
    /// Needed to spot single-thread bottlenecks that the aggregate line hides.
    /// </summary>
    public static Dictionary<string, CpuTicks> ParsePerCoreTicks(string procStatText)
    {
        var cores = new Dictionary<string, CpuTicks>(StringComparer.Ordinal);
        foreach (var line in procStatText.Split('\n'))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal) || line.StartsWith("cpu ", StringComparison.Ordinal))
                continue;
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5)
                continue;

            var values = new List<long>();
            var valid = true;
            foreach (var field in fields.Skip(1).Take(8))
            {
                if (!long.TryParse(field, out var value))
                {
                    valid = false;
                    break;
                }
                values.Add(value);
            }
            if (!valid)
                continue;
            var idle = values[3] + (values.Count > 4 ? values[4] : 0);
            cores[fields[0]] = new CpuTicks(idle, values.Sum());
        }
        return cores;
    }

    /// <summary>
    /// Extracts utime+stime (jiffies consumed) from /proc/[pid]/stat. The comm
    /// field may contain spaces and parentheses, so parsing starts after the
    /// last ')'.
    /// </summary>
    public static long? ParseProcessTicks(string procPidStatText)
    {
        var afterComm = procPidStatText.LastIndexOf(')');
        if (afterComm < 0)
            return null;
        var fields = procPidStatText[(afterComm + 1)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // After ')': state(0) ppid(1) ... utime is field 11, stime field 12.
        if (fields.Length < 13 ||
            !long.TryParse(fields[11], out var utime) ||
            !long.TryParse(fields[12], out var stime))
            return null;
        return utime + stime;
    }

    /// <summary>
    /// Process CPU usage as a share of the whole machine: the process's jiffy
    /// delta over the all-core jiffy delta from /proc/stat's aggregate line.
    /// </summary>
    public static double? ProcessCpuUsageBetween(
        long previousProcessTicks, long currentProcessTicks,
        CpuTicks previousTotal, CpuTicks currentTotal)
    {
        var totalDelta = currentTotal.TotalTicks - previousTotal.TotalTicks;
        var processDelta = currentProcessTicks - previousProcessTicks;
        if (totalDelta <= 0 || processDelta < 0)
            return null;
        return Math.Clamp(100.0 * processDelta / totalDelta, 0, 100);
    }
}
