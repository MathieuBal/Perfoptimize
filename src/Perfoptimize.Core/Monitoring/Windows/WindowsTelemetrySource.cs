using System.Runtime.Versioning;
using Perfoptimize.Core.Hardware.Windows;

namespace Perfoptimize.Core.Monitoring.Windows;

/// <summary>
/// Windows telemetry from WMI performance counters. GPU usage is attributed to
/// the game's PID and to the adapter (LUID) it actually renders on, so an iGPU,
/// a browser or an overlay never pollutes the numbers; VRAM is scoped to the
/// same adapter. GPU temperature/clocks use nvidia-smi when available.
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

    public TelemetrySample Sample(int? gameProcessId = null)
    {
        var nvidia = NvidiaSmi.TryQuery();
        var (cpuTotal, maxCore) = SampleCpuUsage();
        var (gameGpu, adapterGpu, adapterLuid) = SampleGpuEngines(gameProcessId);

        return new TelemetrySample
        {
            CpuUsagePercent = cpuTotal,
            MaxCoreUsagePercent = maxCore,
            GameCpuUsagePercent = gameProcessId is { } pid ? SampleProcessCpu(pid) : null,
            RamUsagePercent = SampleRamUsage(),
            GpuUsagePercent = adapterGpu ?? nvidia?.UtilizationPercent,
            GameGpuUsagePercent = gameGpu,
            VramUsagePercent = SampleVramUsage(adapterLuid) ?? NvidiaSmi.VramPercent(nvidia),
            GpuTemperatureC = nvidia?.TemperatureC,
            GpuCoreClockMhz = nvidia?.CoreClockMhz,
            GpuThermalThrottleActive = nvidia?.ThermalThrottleActive,
            GpuHardwareSlowdownActive = nvidia?.HardwareSlowdownActive,
            CpuTemperatureC = SampleCpuTemperature(),
        };
    }

    /// <summary>Machine-wide CPU plus the busiest logical core (single-thread limits).</summary>
    private static (double? Total, double? MaxCore) SampleCpuUsage()
    {
        var rows = WmiQuery.Run(
            "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor");
        double? total = null, maxCore = null;
        foreach (var row in rows)
        {
            var value = WmiQuery.ToDouble(row["PercentProcessorTime"]);
            if (value is null || row["Name"] is not string name)
                continue;
            if (name == "_Total")
                total = value;
            else if (int.TryParse(name, out _))
                maxCore = Math.Max(maxCore ?? 0, value.Value);
        }
        return (total, maxCore);
    }

    /// <summary>Game CPU as a share of the whole machine (counter reports % of one core).</summary>
    private static double? SampleProcessCpu(int pid)
    {
        var row = WmiQuery.Run(
            "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfProc_Process " +
            $"WHERE IDProcess = {pid}").FirstOrDefault();
        var perCore = row is null ? null : WmiQuery.ToDouble(row["PercentProcessorTime"]);
        return perCore is { } value
            ? Math.Clamp(value / Environment.ProcessorCount, 0, 100)
            : null;
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

    private static (double? GameGpu, double? AdapterGpu, string? AdapterLuid) SampleGpuEngines(
        int? gameProcessId)
    {
        var rows = WmiQuery.Run(
            "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
        var readings = new List<(GpuEngineInstance, double)>();
        foreach (var row in rows)
        {
            if (row["Name"] is not string name ||
                GpuEngineInstanceParser.Parse(name) is not { } instance)
                continue;
            readings.Add((instance, WmiQuery.ToDouble(row["UtilizationPercentage"]) ?? 0));
        }
        return GpuEngineInstanceParser.Aggregate(readings, gameProcessId);
    }

    /// <summary>Dedicated VRAM in use on one adapter (the game's), not summed across cards.</summary>
    private double? SampleVramUsage(string? adapterLuid)
    {
        if (_totalVramBytes is not > 0)
            return null;
        var rows = WmiQuery.Run(
            "SELECT Name, DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
        if (rows.Count == 0)
            return null;

        long used = 0;
        var matched = false;
        foreach (var row in rows)
        {
            if (row["Name"] is not string name)
                continue;
            if (adapterLuid is not null &&
                !name.Contains(adapterLuid, StringComparison.OrdinalIgnoreCase))
                continue;
            used += WmiQuery.ToLong(row["DedicatedUsage"]) ?? 0;
            matched = true;
        }
        if (!matched)
            return null;
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
