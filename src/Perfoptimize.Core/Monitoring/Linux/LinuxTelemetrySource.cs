using Perfoptimize.Core.Hardware.Linux;

namespace Perfoptimize.Core.Monitoring.Linux;

/// <summary>
/// Linux telemetry from /proc, /sys and nvidia-smi (when present).
/// CPU usage (total, busiest core, game process) is derived from /proc/stat
/// and /proc/[pid]/stat deltas between two calls.
/// </summary>
public sealed class LinuxTelemetrySource : ISystemTelemetrySource
{
    private CpuTicks? _previousTotal;
    private Dictionary<string, CpuTicks> _previousPerCore = [];
    private (int Pid, long Ticks)? _previousProcess;

    public TelemetrySample Sample(int? gameProcessId = null)
    {
        var procStat = ReadFileOrEmpty("/proc/stat");
        var currentTotal = LinuxTelemetryParser.ParseProcStatCpuLine(procStat);
        var currentPerCore = LinuxTelemetryParser.ParsePerCoreTicks(procStat);

        var cpuUsage = _previousTotal is { } previousTotal && currentTotal is { } total
            ? LinuxTelemetryParser.CpuUsageBetween(previousTotal, total)
            : null;
        var maxCore = MaxCoreUsage(_previousPerCore, currentPerCore);
        var gameCpu = SampleProcessCpu(gameProcessId, currentTotal);

        _previousTotal = currentTotal;
        _previousPerCore = currentPerCore;

        var memory = LinuxProcParser.ParseMemInfo(ReadFileOrEmpty("/proc/meminfo"));
        double? ramUsage = memory.TotalBytes > 0 && memory.AvailableBytes is { } available
            ? Math.Clamp(100.0 * (memory.TotalBytes - available) / memory.TotalBytes, 0, 100)
            : null;

        var gpu = NvidiaSmi.TryQuery();

        return new TelemetrySample
        {
            CpuUsagePercent = cpuUsage,
            MaxCoreUsagePercent = maxCore,
            GameCpuUsagePercent = gameCpu,
            RamUsagePercent = ramUsage,
            GpuUsagePercent = gpu?.UtilizationPercent,
            VramUsagePercent = NvidiaSmi.VramPercent(gpu),
            GpuTemperatureC = gpu?.TemperatureC,
            GpuCoreClockMhz = gpu?.CoreClockMhz,
            GpuThermalThrottleActive = gpu?.ThermalThrottleActive,
            CpuTemperatureC = ReadCpuTemperature(),
        };
    }

    private static double? MaxCoreUsage(
        Dictionary<string, CpuTicks> previous, Dictionary<string, CpuTicks> current)
    {
        double? max = null;
        foreach (var (core, ticks) in current)
        {
            if (!previous.TryGetValue(core, out var before))
                continue;
            var usage = LinuxTelemetryParser.CpuUsageBetween(before, ticks);
            if (usage is { } value)
                max = Math.Max(max ?? 0, value);
        }
        return max;
    }

    private double? SampleProcessCpu(int? gameProcessId, CpuTicks? currentTotal)
    {
        if (gameProcessId is not { } pid || currentTotal is not { } total)
        {
            _previousProcess = null;
            return null;
        }

        var ticks = LinuxTelemetryParser.ParseProcessTicks(ReadFileOrEmpty($"/proc/{pid}/stat"));
        if (ticks is not { } currentTicks)
        {
            _previousProcess = null;
            return null;
        }

        double? usage = null;
        if (_previousProcess is { } previous && previous.Pid == pid && _previousTotal is { } previousTotal)
        {
            usage = LinuxTelemetryParser.ProcessCpuUsageBetween(
                previous.Ticks, currentTicks, previousTotal, total);
        }
        _previousProcess = (pid, currentTicks);
        return usage;
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
