namespace Perfoptimize.Core.Live;

/// <summary>A live alert that has been assigned a stable id and seen over time.</summary>
public sealed class TrackedAlert
{
    public long Id { get; init; }
    public string Kind { get; init; } = "";
    public string Level { get; init; } = "warn";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; set; }
    internal string Key { get; init; } = "";
}

/// <summary>
/// Keeps a stable, de-duplicated view of the live alerts across successive
/// samples: the same condition keeps the same id (so the UI can tell a genuinely
/// new alert from an ongoing one and notify only once), and an alert that stops
/// being reported for longer than the TTL is dropped. Pure and unit-tested; the
/// background service just feeds it each sample's alerts.
/// </summary>
public sealed class AlertTracker
{
    private readonly TimeSpan _ttl;
    private readonly List<TrackedAlert> _active = new();
    private long _nextId = 1;

    public AlertTracker(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromSeconds(20);

    /// <summary>Merges the current sample's alerts and returns the active set, newest first.</summary>
    public IReadOnlyList<TrackedAlert> Update(IReadOnlyList<LiveAlert> incoming, DateTimeOffset now)
    {
        foreach (var alert in incoming)
        {
            var key = alert.Kind + "|" + alert.Title;
            var existing = _active.FirstOrDefault(a => a.Key == key);
            if (existing is not null)
            {
                existing.LastSeen = now;
            }
            else
            {
                _active.Add(new TrackedAlert
                {
                    Id = _nextId++,
                    Key = key,
                    Kind = alert.Kind.ToString(),
                    Level = alert.Level,
                    Title = alert.Title,
                    Detail = alert.Detail,
                    FirstSeen = now,
                    LastSeen = now,
                });
            }
        }

        _active.RemoveAll(a => now - a.LastSeen > _ttl);
        return _active.OrderByDescending(a => a.FirstSeen).ToList();
    }
}
