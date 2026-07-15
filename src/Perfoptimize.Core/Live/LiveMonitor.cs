using System.Diagnostics;
using Perfoptimize.Core.Monitoring;

namespace Perfoptimize.Core.Live;

/// <summary>
/// Samples the whole machine over a short window: reads every process' CPU time
/// and working set twice, a moment apart, and hands both readings to the pure
/// <see cref="LiveUsageCalculator"/>. Cross-platform (the .NET Process API works
/// on Windows and Linux) and defensive — a process vanishing or refusing access
/// mid-scan is skipped, never fatal.
/// </summary>
public sealed class LiveMonitor
{
    public async Task<LiveSnapshot> SampleAsync(
        TimeSpan window, int? gamePid, string? gameName, CancellationToken cancellationToken = default)
    {
        var before = ReadProcessTimes();
        await Task.Delay(window, cancellationToken);
        var after = ReadProcessTimes();

        var (ramTotal, ramUsed) = SystemMemoryReader.Read();
        var telemetry = TrySampleTelemetry(gamePid);
        double? dpc = null, interrupt = null;
        if (OperatingSystem.IsWindows())
            (dpc, interrupt) = DpcLatencyReader.Read();

        return LiveUsageCalculator.Compute(
            before, after, window, Environment.ProcessorCount,
            ramTotal, ramUsed, gamePid, gameName,
            gpuPercent: telemetry?.GpuUsagePercent,
            vramPercent: telemetry?.VramUsagePercent,
            cpuTemperatureC: telemetry?.CpuTemperatureC,
            gpuTemperatureC: telemetry?.GpuTemperatureC,
            gpuThrottling: telemetry?.GpuThermalThrottleActive,
            dpcPercent: dpc,
            interruptPercent: interrupt);
    }

    /// <summary>
    /// One best-effort telemetry sample for GPU load and temperatures. GPU load
    /// and temps are instantaneous readings (unlike CPU, they need no priming),
    /// so a single sample is meaningful. Never fatal — the monitor still works
    /// on process figures alone if telemetry is unavailable.
    /// </summary>
    private static TelemetrySample? TrySampleTelemetry(int? gamePid)
    {
        try
        {
            return PerformanceMonitor.CreateForCurrentPlatform().Sample(gamePid);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<ProcessTimes> ReadProcessTimes()
    {
        var readings = new List<ProcessTimes>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                readings.Add(new ProcessTimes(
                    process.Id, process.ProcessName, process.TotalProcessorTime, process.WorkingSet64));
            }
            catch
            {
                // Access denied (protected system process) or exited between
                // enumeration and inspection — skip it.
            }
            finally
            {
                process.Dispose();
            }
        }
        return readings;
    }
}
