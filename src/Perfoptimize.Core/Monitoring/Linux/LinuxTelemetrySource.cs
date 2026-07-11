using Perfoptimize.Core.Hardware.Linux;

namespace Perfoptimize.Core.Monitoring.Linux;

/// <summary>
/// Linux telemetry from /proc, /sys and nvidia-smi (when present).
/// CPU usage is derived from /proc/stat deltas between two calls.
/// </summary>
public sealed class LinuxTelemetrySource : ISystemTelemetrySource
{
    private CpuTicks? _previousTicks;

    public TelemetrySample Sample()
    {
        var cpuUsage = SampleCpuUsage();
        var memory = LinuxProcParser.ParseMemInfo(ReadFileOrEmpty("/proc/meminfo"));
        double? ramUsage = memory.TotalBytes > 0 && memory.AvailableBytes is { } available
            ? Math.Clamp(100.0 * (memory.TotalBytes - available) / memory.TotalBytes, 0, 100)
            : null;

        var gpu = NvidiaSmi.TryQuery();

        return new TelemetrySample
        {
            CpuUsagePercent = cpuUsage,
            RamUsagePercent = ramUsage,
            GpuUsagePercent = gpu?.UtilizationPercent,
            VramUsagePercent = NvidiaSmi.VramPercent(gpu),
            GpuTemperatureC = gpu?.TemperatureC,
            CpuTemperatureC = ReadCpuTemperature(),
        };
    }

    private double? SampleCpuUsage()
    {
        var current = LinuxTelemetryParser.ParseProcStatCpuLine(ReadFileOrEmpty("/proc/stat"));
        if (current is null)
            return null;
        var previous = _previousTicks;
        _previousTicks = current;
        return previous is null ? null : LinuxTelemetryParser.CpuUsageBetween(previous.Value, current.Value);
    }

    private static double? ReadCpuTemperature()
    {
        // Look for a CPU-package sensor among the hwmon devices (x86_pkg_temp for
        // Intel, k10temp for AMD); values are in millidegrees Celsius.
        try
        {
            if (!Directory.Exists("/sys/class/hwmon"))
                return null;
            foreach (var hwmon in Directory.GetDirectories("/sys/class/hwmon"))
            {
                var name = ReadFileOrEmpty($"{hwmon}/name").Trim();
                if (name is not ("x86_pkg_temp" or "k10temp" or "coretemp" or "zenpower"))
                    continue;
                var text = ReadFileOrEmpty($"{hwmon}/temp1_input").Trim();
                if (long.TryParse(text, out var milliDegrees) && milliDegrees > 0)
                    return milliDegrees / 1000.0;
            }
        }
        catch
        {
            // Sensors are optional.
        }
        return null;
    }

    private static string ReadFileOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

}
