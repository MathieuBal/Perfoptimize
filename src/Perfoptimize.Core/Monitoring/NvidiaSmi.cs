using System.Diagnostics;
using System.Globalization;

namespace Perfoptimize.Core.Monitoring;

/// <summary>One GPU line from nvidia-smi CSV output.</summary>
public readonly record struct NvidiaGpuStatus(
    double UtilizationPercent, double MemoryUsedMiB, double MemoryTotalMiB, double? TemperatureC,
    double? CoreClockMhz = null, ulong? ThrottleReasonsMask = null)
{
    // clocks_throttle_reasons bit values documented by NVML:
    // 0x08 HwSlowdown, 0x20 SwThermalSlowdown, 0x40 HwThermalSlowdown, 0x80 HwPowerBrakeSlowdown.
    // Only the explicitly thermal bits may be reported as thermal; a generic HW
    // slowdown or power brake can be power delivery and must stay a separate cause.
    private const ulong ThermalSlowdownMask = 0x20 | 0x40;
    private const ulong HardwareSlowdownMask = 0x08 | 0x80;

    /// <summary>True when the driver reports an active thermal slowdown; null when unknown.</summary>
    public bool? ThermalThrottleActive =>
        ThrottleReasonsMask is { } mask ? (mask & ThermalSlowdownMask) != 0 : null;

    /// <summary>
    /// True when the driver reports a non-thermal hardware slowdown (HW slowdown,
    /// power brake) — the card is held back, but possibly by power, not heat.
    /// </summary>
    public bool? HardwareSlowdownActive =>
        ThrottleReasonsMask is { } mask ? (mask & HardwareSlowdownMask) != 0 : null;
}

/// <summary>
/// Queries the NVIDIA driver's nvidia-smi tool, which exists on both Windows
/// (installed to System32 by the driver) and Linux. The parsing is pure and
/// unit-testable; the process call is best-effort.
/// </summary>
public static class NvidiaSmi
{
    private const string QueryArguments =
        "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu," +
        "clocks.sm,clocks_throttle_reasons.active --format=csv,noheader,nounits";

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
    /// Parses one CSV line, e.g. "45, 6144, 12288, 65, 1850, 0x0000000000000004".
    /// The clock and throttle-reason fields are optional (older drivers), and
    /// any field may be "[N/A]" on some setups.
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

        return new NvidiaGpuStatus(
            utilization, memoryUsed, memoryTotal,
            TemperatureC: ParseDouble(fields, 3),
            CoreClockMhz: ParseDouble(fields, 4),
            ThrottleReasonsMask: ParseHexMask(fields, 5));
    }

    private static double? ParseDouble(string[] fields, int index) =>
        index < fields.Length &&
        double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static ulong? ParseHexMask(string[] fields, int index)
    {
        if (index >= fields.Length)
            return null;
        var text = fields[index];
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mask)
            ? mask
            : null;
    }

    /// <summary>VRAM usage percentage for a parsed status, when totals are known.</summary>
    public static double? VramPercent(NvidiaGpuStatus? status) =>
        status is { MemoryTotalMiB: > 0 } s
            ? Math.Clamp(100.0 * s.MemoryUsedMiB / s.MemoryTotalMiB, 0, 100)
            : null;
}
