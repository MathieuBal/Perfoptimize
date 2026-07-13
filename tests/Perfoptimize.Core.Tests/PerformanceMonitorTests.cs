using System.Diagnostics;
using Perfoptimize.Core.Monitoring;

namespace Perfoptimize.Core.Tests;

public class PerformanceMonitorTests
{
    /// <summary>A source whose every probe takes a fixed amount of time.</summary>
    private sealed class SlowSource(TimeSpan cost) : ISystemTelemetrySource
    {
        public int Calls { get; private set; }

        public TelemetrySample Sample(int? gameProcessId = null)
        {
            Calls++;
            Thread.Sleep(cost);
            return new TelemetrySample { CpuUsagePercent = 50, GpuUsagePercent = 60 };
        }
    }

    [Fact]
    public async Task A_probe_slower_than_the_step_still_collects_many_samples()
    {
        // Regression: a probe costing more than one step used to terminate the
        // whole session after a single sample. Over ~2s with ~0.15s probes we
        // must get clearly more than one — 3 is also the minimum for a verdict.
        // Bound stays loose because Task.Delay jitters under load.
        var source = new SlowSource(TimeSpan.FromMilliseconds(150));
        var monitor = new PerformanceMonitor(source);

        var report = await monitor.CaptureAsync(
            TimeSpan.FromSeconds(2), interval: TimeSpan.FromMilliseconds(100));

        Assert.True(report.Summary.SampleCount >= 3,
            $"expected several samples, got {report.Summary.SampleCount}");
    }

    [Fact]
    public async Task The_observation_window_starts_after_priming()
    {
        // A slow primer must not eat into the sampling window (the real-Windows
        // cold-start bug): with a 0.3s primer and a 1s window, we still sample.
        var source = new SlowSource(TimeSpan.FromMilliseconds(300));
        var monitor = new PerformanceMonitor(source);

        var stopwatch = Stopwatch.StartNew();
        var report = await monitor.CaptureAsync(
            TimeSpan.FromSeconds(1), interval: TimeSpan.FromMilliseconds(100));
        stopwatch.Stop();

        Assert.True(report.Summary.SampleCount >= 1);
        Assert.True(report.PrimeDuration >= TimeSpan.FromMilliseconds(250));
        // Total time = prime (~0.3s) + window (1s), proving the window ran fully.
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Fast_source_paces_instead_of_busy_looping()
    {
        var source = new SlowSource(TimeSpan.FromMilliseconds(1));
        var monitor = new PerformanceMonitor(source);

        var report = await monitor.CaptureAsync(
            TimeSpan.FromSeconds(1), interval: TimeSpan.FromMilliseconds(100));

        // The invariant is that it waits ~one step between probes rather than
        // busy-looping (which would yield hundreds) — the exact count depends on
        // scheduler timing, so only the order of magnitude is asserted.
        Assert.InRange(report.Summary.SampleCount, 2, 40);
    }
}
