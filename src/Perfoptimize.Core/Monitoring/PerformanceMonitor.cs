using Perfoptimize.Core.Monitoring.Linux;
using Perfoptimize.Core.Monitoring.Windows;

namespace Perfoptimize.Core.Monitoring;

/// <summary>Result of a monitoring session: raw samples plus their aggregation.</summary>
public sealed class PerformanceReport
{
    public IReadOnlyList<TelemetrySample> Samples { get; init; } = [];
    public PerformanceSummary Summary { get; init; } = new();
    /// <summary>
    /// How long the first (primer) probe took. On a healthy machine this is
    /// tens of milliseconds; a value in seconds means the telemetry backend
    /// (WMI performance counters, nvidia-smi) is slow and starved the session.
    /// </summary>
    public TimeSpan PrimeDuration { get; init; }
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

    /// <param name="gameProcessId">
    /// PID of the detected game: readings are then attributed to that process
    /// and to the GPU adapter it actually renders on.
    /// </param>
    public async Task<PerformanceReport> CaptureAsync(
        TimeSpan duration,
        TimeSpan? interval = null,
        IProgress<TelemetrySample>? onSample = null,
        int? gameProcessId = null,
        CancellationToken cancellationToken = default)
    {
        var step = interval ?? TimeSpan.FromSeconds(1);
        var samples = new List<TelemetrySample>();

        // Prime the source: the first CPU reading has no previous measurement
        // to diff against, so it is taken and discarded. Time it — a slow prime
        // is the tell-tale of a starved WMI/nvidia-smi backend.
        var primeStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _source.Sample(gameProcessId);
        primeStopwatch.Stop();

        // The deadline starts AFTER priming: a slow backend should still get its
        // full observation window rather than collecting zero samples.
        var deadline = DateTimeOffset.Now + duration;
        // A full probe can itself cost close to a step on real hardware (~1.3s of
        // WMI on a warm system). Pace the wait by the last probe's cost so we
        // sample about once per step, and never below a small floor that keeps
        // the CPU-usage delta interval meaningful.
        var lastSampleCost = primeStopwatch.Elapsed;
        var floor = TimeSpan.FromMilliseconds(200);
        while (DateTimeOffset.Now < deadline && !cancellationToken.IsCancellationRequested)
        {
            var wait = step - lastSampleCost;
            if (wait < floor)
                wait = floor;
            try
            {
                await Task.Delay(wait, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var sampleStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var sample = _source.Sample(gameProcessId);
            sampleStopwatch.Stop();
            lastSampleCost = sampleStopwatch.Elapsed;
            samples.Add(sample);
            onSample?.Report(sample);
        }

        return new PerformanceReport
        {
            Samples = samples,
            Summary = TelemetryAggregator.Summarize(samples),
            PrimeDuration = primeStopwatch.Elapsed,
        };
    }
}
