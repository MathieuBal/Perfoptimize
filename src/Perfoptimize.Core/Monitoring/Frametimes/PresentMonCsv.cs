using System.Globalization;

namespace Perfoptimize.Core.Monitoring.Frametimes;

/// <summary>
/// Reads frame times from a CSV captured by Intel's PresentMon
/// (https://github.com/GameTechDev/PresentMon), the standard no-injection way
/// to measure real FPS on Windows. Column names changed across versions:
/// v1 uses "MsBetweenPresents", v2 uses "FrameTime".
/// </summary>
public static class PresentMonCsv
{
    private static readonly string[] FrametimeColumns =
        ["MsBetweenPresents", "FrameTime", "FrameTime(ms)", "msBetweenPresents"];

    /// <summary>
    /// Extracts frame times (ms) from PresentMon CSV text, optionally filtered
    /// to one process name (e.g. "Cyberpunk2077.exe") when the capture
    /// contains several applications.
    /// </summary>
    public static IReadOnlyList<double> ReadFrametimes(string csvText, string? processName = null)
    {
        var lines = csvText.Split('\n');
        if (lines.Length < 2)
            return [];

        var header = SplitCsvLine(lines[0]);
        var frametimeIndex = FindColumn(header, FrametimeColumns);
        if (frametimeIndex < 0)
            return [];
        var applicationIndex = FindColumn(header, ["Application"]);

        var frametimes = new List<double>();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var fields = SplitCsvLine(line);
            if (fields.Length <= frametimeIndex)
                continue;

            if (processName is not null && applicationIndex >= 0 &&
                applicationIndex < fields.Length &&
                !fields[applicationIndex].Equals(processName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (double.TryParse(fields[frametimeIndex], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var ms) && ms > 0)
                frametimes.Add(ms);
        }
        return frametimes;
    }

    private static int FindColumn(string[] header, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var index = Array.FindIndex(header,
                h => h.Trim().Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                return index;
        }
        return -1;
    }

    private static string[] SplitCsvLine(string line) =>
        line.TrimEnd('\r').Split(',');
}
