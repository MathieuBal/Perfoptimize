using Perfoptimize.Core.Live;

namespace Perfoptimize.Core.Tests;

public class LiveUsageCalculatorTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static ProcessTimes P(int pid, string name, double cpuMs, long mem) =>
        new(pid, name, TimeSpan.FromMilliseconds(cpuMs), mem);

    [Fact]
    public void Cpu_percent_is_the_delta_normalized_by_window_and_cores()
    {
        // One process burned 1000 ms of CPU over a 1000 ms window on 2 cores:
        // that is one core fully busy = 50% of the machine.
        var before = new[] { P(1, "game", 0, 100 * 1024 * 1024) };
        var after = new[] { P(1, "game", 1000, 100 * 1024 * 1024) };

        var snap = LiveUsageCalculator.Compute(
            before, after, TimeSpan.FromMilliseconds(1000), cpuCount: 2,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: 1, gameName: "game");

        Assert.Single(snap.TopByCpu);
        Assert.Equal(50.0, snap.TopByCpu[0].CpuPercent);
        Assert.True(snap.TopByCpu[0].IsGame);
        Assert.Equal(50.0, snap.SystemCpuPercent);
    }

    [Fact]
    public void A_background_hog_competing_with_the_game_raises_an_alert_and_tops_the_list()
    {
        var before = new[] { P(1, "game", 0, GiB), P(2, "chrome", 0, GiB) };
        var after = new[] { P(1, "game", 500, GiB), P(2, "chrome", 2000, GiB) };

        var snap = LiveUsageCalculator.Compute(
            before, after, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: 1, gameName: "game");

        // chrome: 2000 / (1000*4) = 50%; game: 12.5%.
        Assert.Equal("chrome", snap.TopByCpu[0].Name);
        Assert.Equal(50.0, snap.TopByCpu[0].CpuPercent);

        var hog = Assert.Single(snap.Alerts, a => a.Kind == LiveAlertKind.ProcessHog);
        Assert.Contains("chrome", hog.Title);
        Assert.Contains("game", hog.Detail);
    }

    [Fact]
    public void A_process_only_present_in_the_second_reading_gets_zero_not_negative()
    {
        var before = Array.Empty<ProcessTimes>();
        var after = new[] { P(9, "spawned", 5000, GiB) };

        var snap = LiveUsageCalculator.Compute(
            before, after, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: null, gameName: null);

        Assert.Empty(snap.TopByCpu); // 0% is filtered out of the CPU chart
        Assert.Equal(0.0, snap.SystemCpuPercent);
        Assert.Single(snap.TopByMemory); // still shows under memory
    }

    [Fact]
    public void Memory_percent_and_ram_pressure_alert_use_the_totals_when_known()
    {
        var before = new[] { P(1, "game", 0, 4 * GiB) };
        var after = new[] { P(1, "game", 0, 4 * GiB) };

        var snap = LiveUsageCalculator.Compute(
            before, after, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: 16 * GiB, ramUsedBytes: 15 * GiB, gamePid: 1, gameName: "game");

        Assert.Equal(25.0, snap.TopByMemory[0].MemoryPercent); // 4 of 16 GiB
        Assert.Equal(93.8, snap.RamUsedPercent);               // 15 of 16 GiB
        Assert.Contains(snap.Alerts, a => a.Kind == LiveAlertKind.MemoryPressure);
    }

    [Fact]
    public void A_hot_or_throttling_gpu_raises_a_thermal_alert_and_carries_the_readings()
    {
        var reading = new[] { P(1, "game", 0, GiB) };

        var hot = LiveUsageCalculator.Compute(
            reading, reading, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: 1, gameName: "game",
            gpuPercent: 99, vramPercent: 80, cpuTemperatureC: 60, gpuTemperatureC: 90, gpuThrottling: false);

        Assert.Equal(99, hot.GpuPercent);
        Assert.Equal(90, hot.GpuTemperatureC);
        Assert.Contains(hot.Alerts, a => a.Kind == LiveAlertKind.Thermal);

        // Throttling flag alone (even at a lower temperature) still alerts.
        var throttling = LiveUsageCalculator.Compute(
            reading, reading, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: 1, gameName: "game",
            gpuTemperatureC: 70, gpuThrottling: true);
        Assert.Contains(throttling.Alerts, a => a.Kind == LiveAlertKind.Thermal);
    }

    [Fact]
    public void High_dpc_time_raises_a_driver_latency_alert()
    {
        var reading = new[] { P(1, "game", 0, GiB) };

        var snap = LiveUsageCalculator.Compute(
            reading, reading, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: 1, gameName: "game",
            dpcPercent: 12, interruptPercent: 2);

        Assert.Equal(12, snap.DpcPercent);
        Assert.Contains(snap.Alerts, a => a.Kind == LiveAlertKind.DriverLatency);
    }

    [Fact]
    public void Low_dpc_time_raises_no_driver_latency_alert()
    {
        var reading = new[] { P(1, "game", 0, GiB) };

        var snap = LiveUsageCalculator.Compute(
            reading, reading, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: 1, gameName: "game",
            dpcPercent: 2, interruptPercent: 1);

        Assert.DoesNotContain(snap.Alerts, a => a.Kind == LiveAlertKind.DriverLatency);
    }

    [Fact]
    public void Cool_temperatures_raise_no_thermal_alert()
    {
        var reading = new[] { P(1, "game", 0, GiB) };

        var snap = LiveUsageCalculator.Compute(
            reading, reading, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: 1, gameName: "game",
            cpuTemperatureC: 55, gpuTemperatureC: 65, gpuThrottling: false);

        Assert.DoesNotContain(snap.Alerts, a => a.Kind == LiveAlertKind.Thermal);
    }

    [Fact]
    public void Memory_percent_is_null_when_total_ram_is_unknown()
    {
        var reading = new[] { P(1, "x", 0, GiB) };

        var snap = LiveUsageCalculator.Compute(
            reading, reading, TimeSpan.FromMilliseconds(1000), cpuCount: 4,
            ramTotalBytes: null, ramUsedBytes: null, gamePid: null, gameName: null);

        Assert.Null(snap.TopByMemory[0].MemoryPercent);
        Assert.Null(snap.RamUsedPercent);
    }
}
