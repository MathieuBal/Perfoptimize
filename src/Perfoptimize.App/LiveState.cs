using Perfoptimize.Core.Live;

namespace Perfoptimize.App;

/// <summary>
/// Thread-safe holder for the latest live snapshot and the tracked alerts,
/// written by the background sampler and read by the /api/live endpoint. Keeping
/// it here (a singleton) is what makes the analysis continuous server-side —
/// independent of whether a browser tab is looking at the Moniteur.
/// </summary>
public sealed class LiveState
{
    private const int FpsHistoryCapacity = 24;

    private readonly object _gate = new();
    private readonly AlertTracker _tracker = new();
    private readonly List<FpsReading> _fpsHistory = new();
    private LiveSnapshot? _latest;
    private IReadOnlyList<TrackedAlert> _alerts = Array.Empty<TrackedAlert>();
    private double? _stableFps;
    private FpsChange? _fpsChange;

    public void Update(LiveSnapshot snapshot)
    {
        lock (_gate)
        {
            _latest = snapshot;
            _alerts = _tracker.Update(snapshot.Alerts, DateTimeOffset.Now);

            if (snapshot.Fps is { } fps and > 0)
            {
                _fpsHistory.Add(new FpsReading(snapshot.Timestamp, fps));
                if (_fpsHistory.Count > FpsHistoryCapacity)
                    _fpsHistory.RemoveAt(0);
                (_stableFps, _fpsChange) = FpsChangeDetector.Analyze(_fpsHistory);
            }
            else
            {
                // No FPS this cycle (game closed / PresentMon gone): forget the trail.
                _fpsHistory.Clear();
                _stableFps = null;
                _fpsChange = null;
            }
        }
    }

    public (LiveSnapshot? Snapshot, IReadOnlyList<TrackedAlert> Alerts, double? StableFps, FpsChange? FpsChange) Read()
    {
        lock (_gate)
        {
            return (_latest, _alerts, _stableFps, _fpsChange);
        }
    }
}
