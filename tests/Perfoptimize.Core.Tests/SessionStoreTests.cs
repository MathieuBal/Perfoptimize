using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;

namespace Perfoptimize.Core.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"perfoptimize-tests-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static SessionRecord Record(string? game, BottleneckType bottleneck, DateTimeOffset startedAt) =>
        new()
        {
            StartedAt = startedAt,
            GameName = game,
            Bottleneck = bottleneck,
            Summary = new PerformanceSummary
            {
                SampleCount = 30,
                AverageCpuUsagePercent = 55,
                AverageGpuUsagePercent = 97,
                PeakVramUsagePercent = 71,
            },
        };

    [Fact]
    public void Save_and_reload_a_session()
    {
        using var store = new SessionStore(_databasePath);
        var startedAt = new DateTimeOffset(2026, 7, 11, 20, 0, 0, TimeSpan.FromHours(2));

        var id = store.Save(Record("Cyberpunk 2077", BottleneckType.Gpu, startedAt));
        var sessions = store.GetRecent();

        Assert.True(id > 0);
        var session = Assert.Single(sessions);
        Assert.Equal("Cyberpunk 2077", session.GameName);
        Assert.Equal(BottleneckType.Gpu, session.Bottleneck);
        Assert.Equal(startedAt, session.StartedAt);
        Assert.Equal(97, session.Summary.AverageGpuUsagePercent);
    }

    [Fact]
    public void GetRecent_orders_newest_first_and_respects_limit()
    {
        using var store = new SessionStore(_databasePath);
        var t0 = DateTimeOffset.Now;
        for (var i = 0; i < 5; i++)
            store.Save(Record("Elden Ring", BottleneckType.Cpu, t0.AddMinutes(i)));

        var sessions = store.GetRecent(limit: 3);

        Assert.Equal(3, sessions.Count);
        Assert.True(sessions[0].StartedAt > sessions[1].StartedAt);
        Assert.True(sessions[1].StartedAt > sessions[2].StartedAt);
    }

    [Fact]
    public void GetRecent_can_filter_by_game_and_keeps_system_sessions_apart()
    {
        using var store = new SessionStore(_databasePath);
        store.Save(Record("Elden Ring", BottleneckType.Cpu, DateTimeOffset.Now));
        store.Save(Record(null, BottleneckType.None, DateTimeOffset.Now.AddMinutes(1)));
        store.Save(Record("Cyberpunk 2077", BottleneckType.Gpu, DateTimeOffset.Now.AddMinutes(2)));

        var filtered = store.GetRecent(gameName: "Elden Ring");

        var session = Assert.Single(filtered);
        Assert.Equal("Elden Ring", session.GameName);
        Assert.Equal(3, store.GetRecent().Count);
    }

    [Fact]
    public void Database_persists_between_store_instances()
    {
        using (var store = new SessionStore(_databasePath))
            store.Save(Record("Elden Ring", BottleneckType.Vram, DateTimeOffset.Now));

        using var reopened = new SessionStore(_databasePath);
        Assert.Single(reopened.GetRecent());
    }
}
