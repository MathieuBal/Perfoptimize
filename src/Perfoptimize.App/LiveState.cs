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
    private readonly object _gate = new();
    private readonly AlertTracker _tracker = new();
    private LiveSnapshot? _latest;
    private IReadOnlyList<TrackedAlert> _alerts = Array.Empty<TrackedAlert>();

    public void Update(LiveSnapshot snapshot)
    {
        lock (_gate)
        {
            _latest = snapshot;
            _alerts = _tracker.Update(snapshot.Alerts, DateTimeOffset.Now);
        }
    }

    public (LiveSnapshot? Snapshot, IReadOnlyList<TrackedAlert> Alerts) Read()
    {
        lock (_gate)
        {
            return (_latest, _alerts);
        }
    }
}
