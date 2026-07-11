using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Monitoring.Linux;

namespace Perfoptimize.Core.Tests;

public class TelemetryTests
{
    [Fact]
    public void ParseProcStatCpuLine_reads_the_aggregate_line()
    {
        const string procStat = """
            cpu  10132153 290696 3084719 46828483 16683 0 25195 0 175628 0
            cpu0 1393280 32966 572056 13343292 6130 0 17875 0 23933 0
            """;

        var ticks = LinuxTelemetryParser.ParseProcStatCpuLine(procStat);

        Assert.NotNull(ticks);
        // idle (46828483) + iowait (16683)
        Assert.Equal(46845166, ticks.Value.IdleTicks);
        Assert.Equal(10132153L + 290696 + 3084719 + 46828483 + 16683 + 0 + 25195 + 0, ticks.Value.TotalTicks);
    }

    [Fact]
    public void CpuUsageBetween_computes_busy_share_of_elapsed_ticks()
    {
        var previous = new CpuTicks(IdleTicks: 1000, TotalTicks: 2000);
        var current = new CpuTicks(IdleTicks: 1250, TotalTicks: 3000); // 750 busy / 1000 total

        Assert.Equal(75.0, LinuxTelemetryParser.CpuUsageBetween(previous, current));
    }

    [Fact]
    public void CpuUsageBetween_is_null_when_no_time_elapsed()
    {
        var ticks = new CpuTicks(1000, 2000);
        Assert.Null(LinuxTelemetryParser.CpuUsageBetween(ticks, ticks));
    }

    [Theory]
    [InlineData("45, 6144, 12288, 65", 45.0, 50.0, 65.0)]
    [InlineData("100, 12288, 12288, 83", 100.0, 100.0, 83.0)]
    public void ParseLine_reads_nvidia_smi_csv(string line, double utilization, double vramPercent, double temperature)
    {
        var status = NvidiaSmi.ParseLine(line);

        Assert.NotNull(status);
        Assert.Equal(utilization, status.Value.UtilizationPercent);
        Assert.Equal(vramPercent, NvidiaSmi.VramPercent(status));
        Assert.Equal(temperature, status.Value.TemperatureC);
    }

    [Fact]
    public void ParseLine_tolerates_missing_temperature()
    {
        var status = NvidiaSmi.ParseLine("45, 6144, 12288, [N/A]");

        Assert.NotNull(status);
        Assert.Null(status.Value.TemperatureC);
    }

    [Fact]
    public void Summarize_aggregates_only_present_values()
    {
        var t0 = DateTimeOffset.Now;
        List<TelemetrySample> samples =
        [
            new() { Timestamp = t0, CpuUsagePercent = 40, GpuUsagePercent = 90, RamUsagePercent = 50 },
            new() { Timestamp = t0.AddSeconds(1), CpuUsagePercent = 60, GpuUsagePercent = 100, RamUsagePercent = 52 },
            new() { Timestamp = t0.AddSeconds(2), CpuUsagePercent = 50, GpuUsagePercent = null, RamUsagePercent = 54 },
        ];

        var summary = TelemetryAggregator.Summarize(samples);

        Assert.Equal(3, summary.SampleCount);
        Assert.Equal(2, summary.Duration.TotalSeconds, precision: 1);
        Assert.Equal(50, summary.AverageCpuUsagePercent);
        Assert.Equal(60, summary.PeakCpuUsagePercent);
        Assert.Equal(95, summary.AverageGpuUsagePercent); // null sample excluded from the average
        Assert.Equal(100, summary.PeakGpuUsagePercent);
        Assert.Null(summary.AverageVramUsagePercent);     // sensor absent on every sample
        Assert.Null(summary.PeakGpuTemperatureC);
    }

    [Fact]
    public void Summarize_of_empty_session_is_all_null()
    {
        var summary = TelemetryAggregator.Summarize([]);

        Assert.Equal(0, summary.SampleCount);
        Assert.Null(summary.AverageCpuUsagePercent);
    }
}
