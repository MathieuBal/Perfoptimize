using Perfoptimize.Core.Monitoring.Linux;
using Perfoptimize.Core.Monitoring.Windows;

namespace Perfoptimize.Core.Monitoring;

/// <summary>Result of a monitoring session: raw samples plus their aggregation.</summary>
public sealed class PerformanceReport
{
    public IReadOnlyList<TelemetrySample> Samples { get; init; } = [];
    public PerformanceSummary Summary { get; init; } = new();
}

/// <summary>
/// Samples system telemetry at a fixed interval for a given duration.
/// Purely observational: it never attaches to or modifies the game process.
/// </summary>
public sealed class PerformanceMonitor
{
    private readonly ISystemTelemetrySource _source;

    public PerformanceMonitor() : this(CreateForCurrentPlatform()) { }

    public PerformanceMonitor(ISystemTelemetrySource source) => _source = source;

    public static ISystemTelemetrySource CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsTelemetrySource();
        if (OperatingSystem.IsLinux())
            return new LinuxTelemetrySource();
        throw new PlatformNotSupportedException(
            $"Telemetry is not supported on {Environment.OSVersion.Platform}.");
    }

    public async Task<PerformanceReport> CaptureAsync(
        TimeSpan duration,
        TimeSpan? interval = null,
        IProgress<TelemetrySample>? onSample = null,
        CancellationToken cancellationToken = default)
    {
        var step = interval ?? TimeSpan.FromSeconds(1);
        var samples = new List<TelemetrySample>();
        var deadline = DateTimeOffset.Now + duration;

        // Prime the source: the first CPU reading has no previous measurement
        // to diff against, so it is taken and discarded.
        _source.Sample();

        while (DateTimeOffset.Now < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(step, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            var sample = _source.Sample();
            samples.Add(sample);
            onSample?.Report(sample);
        }

        return new PerformanceReport
        {
            Samples = samples,
            Summary = TelemetryAggregator.Summarize(samples),
        };
    }
}
