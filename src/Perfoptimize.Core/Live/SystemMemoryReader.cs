using System.Runtime.Versioning;
using Perfoptimize.Core.Hardware.Linux;
using Perfoptimize.Core.Hardware.Windows;

namespace Perfoptimize.Core.Live;

/// <summary>
/// Cheap physical-memory reading for the live monitor: total and used bytes.
/// Reuses the existing parsers (/proc/meminfo on Linux, WMI on Windows). Never
/// throws — returns nulls when the figure is unavailable.
/// </summary>
public static class SystemMemoryReader
{
    public static (long? Total, long? Used) Read()
    {
        try
        {
            if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
            {
                var memory = LinuxProcParser.ParseMemInfo(File.ReadAllText("/proc/meminfo"));
                if (memory.TotalBytes > 0)
                {
                    long? used = memory.AvailableBytes is { } available
                        ? memory.TotalBytes - available
                        : null;
                    return (memory.TotalBytes, used);
                }
            }

            if (OperatingSystem.IsWindows())
                return ReadWindows();
        }
        catch
        {
            // Best effort: the monitor still works with per-process figures alone.
        }

        return (null, null);
    }

    [SupportedOSPlatform("windows")]
    private static (long?, long?) ReadWindows()
    {
        // Win32_OperatingSystem reports kilobytes.
        foreach (var row in WmiQuery.Run(
                     "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
        {
            var totalKb = WmiQuery.ToLong(row.GetValueOrDefault("TotalVisibleMemorySize"));
            var freeKb = WmiQuery.ToLong(row.GetValueOrDefault("FreePhysicalMemory"));
            if (totalKb is { } total)
            {
                long? used = freeKb is { } free ? (total - free) * 1024 : null;
                return (total * 1024, used);
            }
        }

        return (null, null);
    }
}
