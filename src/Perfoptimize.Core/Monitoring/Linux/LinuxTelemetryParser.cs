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
}
