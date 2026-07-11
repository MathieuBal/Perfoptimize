using System.Diagnostics;
using System.Globalization;

namespace Perfoptimize.Core.Monitoring;

/// <summary>One GPU line from nvidia-smi CSV output.</summary>
public readonly record struct NvidiaGpuStatus(
    double UtilizationPercent, double MemoryUsedMiB, double MemoryTotalMiB, double? TemperatureC);

/// <summary>
/// Queries the NVIDIA driver's nvidia-smi tool, which exists on both Windows
/// (installed to System32 by the driver) and Linux. The parsing is pure and
/// unit-testable; the process call is best-effort.
/// </summary>
public static class NvidiaSmi
{
    private const string QueryArguments =
        "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits";

    /// <summary>Runs nvidia-smi and parses the first GPU; null when unavailable.</summary>
    public static NvidiaGpuStatus? TryQuery()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = QueryArguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            var firstLine = output.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            return firstLine is null ? null : ParseLine(firstLine);
        }
        catch
        {
            return null; // Not an NVIDIA machine, or the tool is not on PATH.
        }
    }

    /// <summary>
    /// Parses one CSV line, e.g. "45, 6144, 12288, 65".
    /// Temperature may be "[N/A]" on some setups.
    /// </summary>
    public static NvidiaGpuStatus? ParseLine(string line)
    {
        var fields = line.Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length < 4)
            return null;
        if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var utilization) ||
            !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var memoryUsed) ||
            !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var memoryTotal))
            return null;

        double? temperature =
            double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var temp)
                ? temp
                : null;
        return new NvidiaGpuStatus(utilization, memoryUsed, memoryTotal, temperature);
    }

    /// <summary>VRAM usage percentage for a parsed status, when totals are known.</summary>
    public static double? VramPercent(NvidiaGpuStatus? status) =>
        status is { MemoryTotalMiB: > 0 } s
            ? Math.Clamp(100.0 * s.MemoryUsedMiB / s.MemoryTotalMiB, 0, 100)
            : null;
}
