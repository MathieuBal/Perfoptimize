using System.Runtime.Versioning;
using Perfoptimize.Core.Hardware.Windows;

namespace Perfoptimize.Core.Monitoring.Windows;

/// <summary>
/// Windows telemetry from WMI performance counters. GPU usage and VRAM come
/// from the "GPU Engine" / "GPU Adapter Memory" counters that feed Task
/// Manager; GPU temperature uses nvidia-smi when the NVIDIA driver installed it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTelemetrySource : ISystemTelemetrySource
{
    private readonly long? _totalVramBytes;

    /// <param name="totalVramBytes">
    /// Dedicated VRAM of the gaming GPU (from the hardware profile), used to
    /// turn the "dedicated usage" counter into a percentage.
    /// </param>
    public WindowsTelemetrySource(long? totalVramBytes = null) => _totalVramBytes = totalVramBytes;

    public TelemetrySample Sample()
    {
        var nvidia = NvidiaSmi.TryQuery();
        return new TelemetrySample
        {
            CpuUsagePercent = SampleCpuUsage(),
            RamUsagePercent = SampleRamUsage(),
            GpuUsagePercent = SampleGpuUsage() ?? nvidia?.UtilizationPercent,
            VramUsagePercent = SampleVramUsage() ?? NvidiaSmi.VramPercent(nvidia),
            GpuTemperatureC = nvidia?.TemperatureC,
            CpuTemperatureC = SampleCpuTemperature(),
        };
    }

    private static double? SampleCpuUsage()
    {
        var row = WmiQuery.Run(
            "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'")
            .FirstOrDefault();
        return row is null ? null : WmiQuery.ToDouble(row["PercentProcessorTime"]);
    }

    private static double? SampleRamUsage()
    {
        var row = WmiQuery.Run(
            "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem")
            .FirstOrDefault();
        var total = row is null ? null : WmiQuery.ToLong(row["TotalVisibleMemorySize"]);
        var free = row is null ? null : WmiQuery.ToLong(row["FreePhysicalMemory"]);
        if (total is not > 0 || free is null)
            return null;
        return Math.Clamp(100.0 * (total.Value - free.Value) / total.Value, 0, 100);
    }

    private static double? SampleGpuUsage()
    {
        // Same source as Task Manager: sum the 3D engine utilization across processes.
        var rows = WmiQuery.Run(
            "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
        if (rows.Count == 0)
            return null;

        double total = 0;
        var found = false;
        foreach (var row in rows)
        {
            if (row["Name"] is not string name ||
                !name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                continue;
            total += WmiQuery.ToDouble(row["UtilizationPercentage"]) ?? 0;
            found = true;
        }
        return found ? Math.Clamp(total, 0, 100) : null;
    }

    private double? SampleVramUsage()
    {
        if (_totalVramBytes is not > 0)
            return null;
        var rows = WmiQuery.Run(
            "SELECT DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
        if (rows.Count == 0)
            return null;
        var used = rows.Sum(r => WmiQuery.ToLong(r["DedicatedUsage"]) ?? 0);
        return Math.Clamp(100.0 * used / _totalVramBytes.Value, 0, 100);
    }

    private static double? SampleCpuTemperature()
    {
        // Best effort: many boards do not expose ACPI thermal zones, and some
        // report the chipset rather than the CPU. Value is in tenths of Kelvin.
        var row = WmiQuery.Run(
            "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\wmi")
            .FirstOrDefault();
        var tenthsKelvin = row is null ? null : WmiQuery.ToLong(row["CurrentTemperature"]);
        if (tenthsKelvin is not > 0)
            return null;
        var celsius = tenthsKelvin.Value / 10.0 - 273.15;
        return celsius is > 0 and < 120 ? celsius : null;
    }

}
