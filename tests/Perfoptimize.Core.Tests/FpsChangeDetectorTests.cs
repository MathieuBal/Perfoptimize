using Perfoptimize.Core.Live;

namespace Perfoptimize.Core.Tests;

public class FpsChangeDetectorTests
{
    private static IReadOnlyList<FpsReading> Series(params double[] fps) =>
        fps.Select((f, i) => new FpsReading(DateTimeOffset.UnixEpoch.AddSeconds(i), f)).ToList();

    [Fact]
    public void No_readings_means_no_answer()
    {
        var (stable, change) = FpsChangeDetector.Analyze(Series());
        Assert.Null(stable);
        Assert.Null(change);
    }

    [Fact]
    public void A_steady_framerate_reports_a_stable_value_and_no_change()
    {
        var (stable, change) = FpsChangeDetector.Analyze(Series(50, 50, 51, 49, 50, 50, 51, 50));
        Assert.Equal(50, stable!.Value, precision: 0);
        Assert.Null(change);
    }

    [Fact]
    public void A_sustained_jump_is_reported_as_a_before_after_change()
    {
        // Five frames at ~48, then a settled three at ~56 (a setting was lowered).
        var (stable, change) = FpsChangeDetector.Analyze(Series(48, 48, 48, 48, 48, 56, 56, 56));

        Assert.Equal(56, stable!.Value, precision: 0);
        Assert.NotNull(change);
        Assert.Equal(48, change!.Before);
        Assert.Equal(56, change.After);
        Assert.True(change.DeltaPercent > 10);
    }

    [Fact]
    public void A_sustained_drop_is_reported_with_a_negative_delta()
    {
        var (_, change) = FpsChangeDetector.Analyze(Series(60, 60, 60, 60, 60, 50, 50, 50));
        Assert.NotNull(change);
        Assert.True(change!.DeltaPercent < 0);
    }

    [Fact]
    public void A_tiny_difference_below_the_threshold_is_not_a_change()
    {
        var (_, change) = FpsChangeDetector.Analyze(Series(50, 50, 50, 50, 50, 52, 52, 52));
        Assert.Null(change); // ~4% < 5% threshold
    }

    [Fact]
    public void A_mid_transition_window_does_not_fire()
    {
        // Recent window still climbing (spread too wide) -> wait for it to settle.
        var (_, change) = FpsChangeDetector.Analyze(Series(48, 48, 48, 48, 48, 48, 52, 60));
        Assert.Null(change);
    }
}
