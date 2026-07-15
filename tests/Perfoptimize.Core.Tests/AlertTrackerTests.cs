using Perfoptimize.Core.Live;

namespace Perfoptimize.Core.Tests;

public class AlertTrackerTests
{
    private static LiveAlert Alert(LiveAlertKind kind, string title) =>
        new() { Kind = kind, Level = "warn", Title = title, Detail = "d" };

    [Fact]
    public void The_same_condition_keeps_a_stable_id_across_samples()
    {
        var tracker = new AlertTracker(TimeSpan.FromSeconds(20));
        var t0 = DateTimeOffset.UnixEpoch;

        var first = tracker.Update([Alert(LiveAlertKind.Thermal, "GPU chaud")], t0);
        var again = tracker.Update([Alert(LiveAlertKind.Thermal, "GPU chaud")], t0.AddSeconds(2));

        Assert.Equal(first[0].Id, again[0].Id);           // same id -> UI won't re-notify
        Assert.Equal(t0.AddSeconds(2), again[0].LastSeen); // but freshness advances
    }

    [Fact]
    public void A_new_distinct_condition_gets_a_new_id()
    {
        var tracker = new AlertTracker();
        var t0 = DateTimeOffset.UnixEpoch;

        var first = tracker.Update([Alert(LiveAlertKind.Thermal, "GPU chaud")], t0);
        var both = tracker.Update(
            [Alert(LiveAlertKind.Thermal, "GPU chaud"), Alert(LiveAlertKind.ProcessHog, "chrome")], t0.AddSeconds(1));

        Assert.Equal(2, both.Count);
        Assert.NotEqual(first[0].Id, both.Single(a => a.Kind == "ProcessHog").Id);
    }

    [Fact]
    public void An_alert_no_longer_reported_expires_after_the_ttl()
    {
        var tracker = new AlertTracker(TimeSpan.FromSeconds(10));
        var t0 = DateTimeOffset.UnixEpoch;

        tracker.Update([Alert(LiveAlertKind.CpuContention, "CPU saturé")], t0);
        var afterGap = tracker.Update([], t0.AddSeconds(15)); // condition gone, TTL passed

        Assert.Empty(afterGap);
    }
}
