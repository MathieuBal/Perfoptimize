using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class BottleneckDetectorTests
{
    private static PerformanceSummary Summary(
        double? avgCpu = null, double? avgGpu = null,
        double? peakRam = null, double? peakVram = null,
        double? peakCpuTemp = null, double? peakGpuTemp = null,
        int sampleCount = 30)
        => new()
        {
            SampleCount = sampleCount,
            AverageCpuUsagePercent = avgCpu,
            AverageGpuUsagePercent = avgGpu,
            PeakRamUsagePercent = peakRam,
            PeakVramUsagePercent = peakVram,
            PeakCpuTemperatureC = peakCpuTemp,
            PeakGpuTemperatureC = peakGpuTemp,
        };

    [Fact]
    public void Too_few_samples_is_inconclusive()
    {
        var analysis = BottleneckDetector.Analyze(Summary(avgGpu: 99, sampleCount: 2));
        Assert.Equal(BottleneckType.Unknown, analysis.Type);
    }

    [Fact]
    public void Gpu_saturated_means_gpu_bound()
    {
        var analysis = BottleneckDetector.Analyze(Summary(avgCpu: 55, avgGpu: 98));
        Assert.Equal(BottleneckType.Gpu, analysis.Type);
    }

    [Fact]
    public void High_cpu_with_gpu_headroom_means_cpu_bound()
    {
        var analysis = BottleneckDetector.Analyze(Summary(avgCpu: 92, avgGpu: 60));
        Assert.Equal(BottleneckType.Cpu, analysis.Type);
    }

    [Fact]
    public void High_cpu_without_gpu_reading_still_means_cpu_bound()
    {
        var analysis = BottleneckDetector.Analyze(Summary(avgCpu: 92, avgGpu: null));
        Assert.Equal(BottleneckType.Cpu, analysis.Type);
    }

    [Fact]
    public void Vram_pressure_wins_over_gpu_saturation()
    {
        var analysis = BottleneckDetector.Analyze(Summary(avgGpu: 99, peakVram: 96));
        Assert.Equal(BottleneckType.Vram, analysis.Type);
    }

    [Fact]
    public void Ram_pressure_wins_over_gpu_saturation()
    {
        var analysis = BottleneckDetector.Analyze(Summary(avgGpu: 99, peakRam: 93));
        Assert.Equal(BottleneckType.Ram, analysis.Type);
    }

    [Theory]
    [InlineData(null, 90.0)]  // hot GPU
    [InlineData(97.0, null)]  // hot CPU
    public void Thermal_throttling_wins_over_everything(double? cpuTemp, double? gpuTemp)
    {
        var analysis = BottleneckDetector.Analyze(
            Summary(avgCpu: 95, avgGpu: 99, peakVram: 99, peakRam: 99,
                    peakCpuTemp: cpuTemp, peakGpuTemp: gpuTemp));
        Assert.Equal(BottleneckType.Thermal, analysis.Type);
    }

    [Fact]
    public void Nothing_saturated_means_no_bottleneck()
    {
        var analysis = BottleneckDetector.Analyze(Summary(avgCpu: 40, avgGpu: 60, peakRam: 50, peakVram: 40));
        Assert.Equal(BottleneckType.None, analysis.Type);
    }

    [Fact]
    public void No_cpu_nor_gpu_readings_is_inconclusive()
    {
        var analysis = BottleneckDetector.Analyze(Summary(peakRam: 50));
        Assert.Equal(BottleneckType.Unknown, analysis.Type);
    }
}
