using System.Runtime.Versioning;
using Perfoptimize.Core.Hardware.Windows;

namespace Perfoptimize.Core.Live;

/// <summary>
/// Reads the share of CPU time spent in deferred procedure calls (DPC) and
/// interrupt service routines (ISR) — the coarse, always-available version of
/// what LatencyMon surfaces. High values point to a misbehaving driver, the
/// classic cause of micro-freezes that show up with no CPU/GPU saturation.
/// Windows-only (these are kernel perf counters); reuses the bounded WMI helper.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DpcLatencyReader
{
    public static (double? DpcPercent, double? InterruptPercent) Read()
    {
        var row = WmiQuery.Run(
                "SELECT PercentDPCTime, PercentInterruptTime " +
                "FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'")
            .FirstOrDefault();
        if (row is null)
            return (null, null);

        return (
            WmiQuery.ToDouble(row.GetValueOrDefault("PercentDPCTime")),
            WmiQuery.ToDouble(row.GetValueOrDefault("PercentInterruptTime")));
    }
}
