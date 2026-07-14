using System.Diagnostics;
using System.Runtime.Versioning;
using Perfoptimize.Core.Hardware.Windows;

namespace Perfoptimize.Core.Monitoring.Windows;

/// <summary>One timed telemetry subsystem probe.</summary>
public readonly record struct DiagnosticTiming(string Subsystem, TimeSpan Elapsed, string Result);

/// <summary>
/// Times each Windows telemetry backend independently. When a monitoring
/// session collects too few samples, this pinpoints which query
/// (nvidia-smi, a specific WMI performance-counter class, ...) is the one
/// starving the loop — something no cross-platform unit test can reveal.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsTelemetryDiagnostics
{
    public static IReadOnlyList<DiagnosticTiming> Probe(int? gameProcessId)
    {
        return
        [
            Time("nvidia-smi", () => NvidiaSmi.TryQuery() is { } s
                ? $"gpu {s.UtilizationPercent:0}%, {s.MemoryUsedMiB:0}/{s.MemoryTotalMiB:0} MiB"
                : "unavailable"),
            Time("WMI Win32_Processor (_Total)", () => Rows(
                "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'")),
            Time("WMI per-core CPU", () => Rows(
                "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor")),
            Time("WMI per-process CPU", () => gameProcessId is { } pid
                ? Rows($"SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfProc_Process WHERE IDProcess = {pid}")
                : "skipped (no game pid)"),
            Time("WMI RAM", () => Rows(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem")),
            Time("WMI GPU Engine", () => Rows(
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")),
            Time("WMI GPU Adapter Memory", () => Rows(
                "SELECT Name, DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory")),
            Time("WMI CPU temperature", () => Rows(
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\wmi")),
        ];
    }

    private static DiagnosticTiming Time(string subsystem, Func<string> probe)
    {
        var stopwatch = Stopwatch.StartNew();
        string result;
        try
        {
            result = probe();
        }
        catch (Exception ex)
        {
            result = $"error: {ex.GetType().Name}";
        }
        stopwatch.Stop();
        return new DiagnosticTiming(subsystem, stopwatch.Elapsed, result);
    }

    private static string Rows(string wql, string scope = @"root\cimv2")
    {
        var count = WmiQuery.Run(wql, scope).Count;
        return $"{count} row(s)";
    }
}
