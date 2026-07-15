using Microsoft.Extensions.Hosting;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Live;

namespace Perfoptimize.App;

/// <summary>
/// Samples the whole machine continuously in the background, regardless of what
/// the UI is showing, and publishes each snapshot (with its alerts) into
/// <see cref="LiveState"/>. This is the "understand at every instant" engine:
/// it keeps running as long as the app process is alive, so overheating or a
/// resource hog is caught even when the user is on another section.
/// </summary>
public sealed class LiveMonitorHostedService : BackgroundService
{
    private static readonly TimeSpan SampleWindow = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly LiveState _state;

    public LiveMonitorHostedService(LiveState state) => _state = state;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var profiles = GameProfileRepository.LoadBuiltIn();
        var detector = new RunningGameDetector();
        var monitor = new LiveMonitor();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var detected = detector.DetectAny(profiles);
                var snapshot = await monitor.SampleAsync(
                    SampleWindow, detected?.ProcessId, detected?.Name, stoppingToken);
                _state.Update(snapshot);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // One bad sample must never stop the continuous monitor.
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
