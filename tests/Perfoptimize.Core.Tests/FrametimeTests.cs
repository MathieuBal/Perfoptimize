using Perfoptimize.Core.Monitoring.Frametimes;

namespace Perfoptimize.Core.Tests;

public class FrametimeTests
{
    [Fact]
    public void Analyze_computes_average_and_low_fps()
    {
        // 99 smooth frames at 10 ms (100 FPS) + 1 hitch at 50 ms (20 FPS).
        var frametimes = Enumerable.Repeat(10.0, 99).Append(50.0).ToList();

        var stats = FrametimeAnalyzer.Analyze(frametimes);

        Assert.NotNull(stats);
        Assert.Equal(100, stats.FrameCount);
        Assert.Equal(1000.0 / 10.4, (double)stats.AverageFps, precision: 0);
        Assert.Equal(20.0, stats.OnePercentLowFps); // worst 1% = the 50 ms frame
        Assert.Equal(50.0, stats.WorstFrametimeMs);
        Assert.Equal(1, stats.StutterCount);        // 50 ms ≥ 2 × median (10 ms)
        Assert.Equal(1.0, stats.StutterPercent);
    }

    [Fact]
    public void Analyze_of_perfectly_stable_capture_reports_no_stutter()
    {
        var stats = FrametimeAnalyzer.Analyze(Enumerable.Repeat(16.7, 300).ToList());

        Assert.NotNull(stats);
        Assert.Equal(0, stats.StutterCount);
        Assert.Equal(stats.AverageFps, stats.OnePercentLowFps);
    }

    [Fact]
    public void Analyze_ignores_invalid_values_and_handles_empty_input()
    {
        Assert.Null(FrametimeAnalyzer.Analyze([]));
        Assert.Null(FrametimeAnalyzer.Analyze([0, -5, double.NaN]));
    }

    [Fact]
    public void Analyze_downsamples_a_time_ordered_fps_series_capped_at_90_points()
    {
        // 300 stable 16.67 ms frames (~60 FPS) get bucketed down to 90 points,
        // each still reporting ~60 FPS.
        var stats = FrametimeAnalyzer.Analyze(Enumerable.Repeat(16.67, 300).ToList());

        Assert.NotNull(stats);
        Assert.Equal(90, stats.FpsSeries.Count);
        Assert.All(stats.FpsSeries, fps => Assert.Equal(60.0, fps, precision: 0));
    }

    [Fact]
    public void Analyze_keeps_one_fps_point_per_frame_when_below_the_cap()
    {
        // 10 ms frames = 100 FPS; fewer frames than buckets means one point each.
        var stats = FrametimeAnalyzer.Analyze(Enumerable.Repeat(10.0, 5).ToList());

        Assert.NotNull(stats);
        Assert.Equal(5, stats.FpsSeries.Count);
        Assert.All(stats.FpsSeries, fps => Assert.Equal(100.0, fps));
    }

    [Fact]
    public void Analyze_fps_series_preserves_the_order_of_a_mid_capture_dip()
    {
        // A slow frame in the middle must show up as a dip in the middle of the
        // series, not be reordered away.
        var frametimes = Enumerable.Repeat(10.0, 20)
            .Concat(Enumerable.Repeat(40.0, 20))
            .Concat(Enumerable.Repeat(10.0, 20))
            .ToList();

        var stats = FrametimeAnalyzer.Analyze(frametimes);

        Assert.NotNull(stats);
        var series = stats.FpsSeries;
        Assert.True(series[0] > series[series.Count / 2]);          // starts fast, dips in the middle
        Assert.True(series[^1] > series[series.Count / 2]);         // recovers at the end
    }

    [Fact]
    public void ReadFrametimes_parses_presentmon_v1_columns()
    {
        const string csv = """
            Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI,MsUntilRenderComplete,MsUntilDisplayed
            game.exe,1234,0x0,DXGI,1,0,1,Hardware,16.67,16.61,0.5,14.1,25.3
            game.exe,1234,0x0,DXGI,1,0,1,Hardware,33.42,33.40,0.6,30.0,42.1
            """;

        var frametimes = PresentMonCsv.ReadFrametimes(csv);

        Assert.Equal([16.67, 33.42], frametimes);
    }

    [Fact]
    public void ReadFrametimes_parses_presentmon_v2_columns()
    {
        const string csv = """
            Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,CPUStartTime,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime
            game.exe,1234,0x0,DXGI,1,0,1,Hardware,0.0,8.33,6.1,2.2,4.0,7.9,7.0,0.9,12.0,8.3
            """;

        var frametimes = PresentMonCsv.ReadFrametimes(csv);

        Assert.Equal([8.33], frametimes);
    }

    [Fact]
    public void ReadFrametimes_can_filter_by_process()
    {
        const string csv = """
            Application,ProcessID,MsBetweenPresents
            game.exe,1234,16.67
            overlay.exe,99,8.00
            game.exe,1234,17.00
            """;

        Assert.Equal([16.67, 17.00], PresentMonCsv.ReadFrametimes(csv, "game.exe"));
        Assert.Equal([8.00], PresentMonCsv.ReadFrametimes(csv, "OVERLAY.EXE")); // case-insensitive
        Assert.Equal(3, PresentMonCsv.ReadFrametimes(csv).Count);
    }

    [Fact]
    public void ReadFrametimes_returns_empty_for_unrelated_csv()
    {
        Assert.Empty(PresentMonCsv.ReadFrametimes("a,b,c\n1,2,3"));
        Assert.Empty(PresentMonCsv.ReadFrametimes(""));
    }
}
