using System.Diagnostics;
using System.Runtime.Versioning;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Monitoring.Frametimes;

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
        TimeSpan window, int? gamePid, string? gameName,
        bool sampleFps = false, CancellationToken cancellationToken = default)
    {
        var before = ReadProcessTimes();
        // When a game is running and PresentMon is available, this short capture
        // measures live FPS during the same window — so change detection sees it.
        // It is opportunistic: a deliberate analysis/tune capture has priority.
        var fpsTask = sampleFps ? CaptureFpsAsync(gamePid, cancellationToken) : Task.FromResult<FrametimeStatistics?>(null);
        await Task.Delay(window, cancellationToken);
        var after = ReadProcessTimes();

        var (ramTotal, ramUsed) = SystemMemoryReader.Read();
        var telemetry = TrySampleTelemetry(gamePid);
        double? dpc = null, interrupt = null;
        if (OperatingSystem.IsWindows())
            (dpc, interrupt) = DpcLatencyReader.Read();
        var frametimes = await fpsTask;

        return LiveUsageCalculator.Compute(
            before, after, window, Environment.ProcessorCount,
            ramTotal, ramUsed, gamePid, gameName,
            gpuPercent: telemetry?.GpuUsagePercent,
            vramPercent: telemetry?.VramUsagePercent,
            cpuTemperatureC: telemetry?.CpuTemperatureC,
            gpuTemperatureC: telemetry?.GpuTemperatureC,
            gpuThrottling: telemetry?.GpuThermalThrottleActive,
            dpcPercent: dpc,
            interruptPercent: interrupt,
            fps: frametimes?.AverageFps,
            fpsOnePercentLow: frametimes?.OnePercentLowFps);
    }

    private static Task<FrametimeStatistics?> CaptureFpsAsync(int? gamePid, CancellationToken cancellationToken)
    {
        if (gamePid is not int pid || pid <= 0 || !OperatingSystem.IsWindows())
            return Task.FromResult<FrametimeStatistics?>(null);
        return CaptureFpsWindowsAsync(pid, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<FrametimeStatistics?> CaptureFpsWindowsAsync(int pid, CancellationToken cancellationToken)
    {
        try
        {
            if (PresentMonCapture.FindExecutable() is null)
                return null;
            return await PresentMonCapture.CaptureAsync(
                pid, TimeSpan.FromMilliseconds(1200), cancellationToken,
                waitForAvailability: false);
        }
        catch
        {
            return null;
        }
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
