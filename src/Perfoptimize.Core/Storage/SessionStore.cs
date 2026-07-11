using Microsoft.Data.Sqlite;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Optimization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perfoptimize.Core.Storage;

/// <summary>One recommendation captured with the session, so 'apply' can later
/// execute exactly the plan the user saw during monitoring.</summary>
public sealed class StoredRecommendation
{
    public string Title { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>Name of the game setting behind the advice; null for generic advice.</summary>
    public string? SettingName { get; init; }
    public double? ExpectedGainPercent { get; init; }
}

/// <summary>One stored monitoring session.</summary>
public sealed class SessionRecord
{
    public long Id { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public string? GameName { get; init; }
    public BottleneckType Bottleneck { get; init; }
    public PerformanceSummary Summary { get; init; } = new();
    /// <summary>FPS target used during the session; null when none was set.</summary>
    public int? TargetFps { get; init; }
    /// <summary>Measured average FPS (PresentMon); null without a capture.</summary>
    public double? AverageFps { get; init; }
    public double? OnePercentLowFps { get; init; }
    /// <summary>The exact plan shown to the user during the session.</summary>
    public IReadOnlyList<StoredRecommendation> Plan { get; init; } = [];
}

/// <summary>
/// Local SQLite history of monitoring sessions. Over time this lets the
/// assistant compare a machine against its own past runs (and later, share
/// anonymized results between similar configurations).
/// </summary>
public sealed class SessionStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SqliteConnection _connection;

    public SessionStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        Execute("""
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                started_at TEXT NOT NULL,
                game_name TEXT NULL,
                bottleneck TEXT NOT NULL,
                summary_json TEXT NOT NULL
            );
            """);
        MigrateSchema();
    }

    /// <summary>Adds columns introduced after the first release to existing databases.</summary>
    private void MigrateSchema()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(sessions)";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                existing.Add(reader.GetString(1));
        }

        (string Name, string Definition)[] columns =
        [
            ("target_fps", "INTEGER NULL"),
            ("average_fps", "REAL NULL"),
            ("one_percent_low_fps", "REAL NULL"),
            ("plan_json", "TEXT NULL"),
        ];
        foreach (var (name, definition) in columns)
        {
            if (!existing.Contains(name))
                Execute($"ALTER TABLE sessions ADD COLUMN {name} {definition}");
        }
    }

    /// <summary>Default location of the database in the user's local application data.</summary>
    public static string DefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Perfoptimize", "perfoptimize.db");

    public long Save(SessionRecord record)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (started_at, game_name, bottleneck, summary_json,
                                  target_fps, average_fps, one_percent_low_fps, plan_json)
            VALUES ($startedAt, $gameName, $bottleneck, $summaryJson,
                    $targetFps, $averageFps, $onePercentLow, $planJson);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$startedAt", record.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$gameName", (object?)record.GameName ?? DBNull.Value);
        command.Parameters.AddWithValue("$bottleneck", record.Bottleneck.ToString());
        command.Parameters.AddWithValue("$summaryJson", JsonSerializer.Serialize(record.Summary, JsonOptions));
        command.Parameters.AddWithValue("$targetFps", (object?)record.TargetFps ?? DBNull.Value);
        command.Parameters.AddWithValue("$averageFps", (object?)record.AverageFps ?? DBNull.Value);
        command.Parameters.AddWithValue("$onePercentLow", (object?)record.OnePercentLowFps ?? DBNull.Value);
        command.Parameters.AddWithValue("$planJson", record.Plan.Count > 0
            ? JsonSerializer.Serialize(record.Plan, JsonOptions)
            : DBNull.Value);
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Loads one session by id; null when it does not exist.</summary>
    public SessionRecord? GetById(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    /// <summary>Most recent sessions first, optionally filtered by game.</summary>
    public IReadOnlyList<SessionRecord> GetRecent(int limit = 10, string? gameName = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = gameName is null
            ? $"{SelectColumns} ORDER BY started_at DESC LIMIT $limit"
            : $"{SelectColumns} WHERE game_name = $gameName ORDER BY started_at DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        if (gameName is not null)
            command.Parameters.AddWithValue("$gameName", gameName);

        var records = new List<SessionRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            records.Add(ReadRecord(reader));
        return records;
    }

    private const string SelectColumns =
        "SELECT id, started_at, game_name, bottleneck, summary_json, " +
        "target_fps, average_fps, one_percent_low_fps, plan_json FROM sessions";

    private static SessionRecord ReadRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        StartedAt = DateTimeOffset.Parse(reader.GetString(1)),
        GameName = reader.IsDBNull(2) ? null : reader.GetString(2),
        Bottleneck = Enum.TryParse<BottleneckType>(reader.GetString(3), out var type)
            ? type
            : BottleneckType.Unknown,
        Summary = JsonSerializer.Deserialize<PerformanceSummary>(reader.GetString(4), JsonOptions) ?? new(),
        TargetFps = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        AverageFps = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        OnePercentLowFps = reader.IsDBNull(7) ? null : reader.GetDouble(7),
        Plan = reader.IsDBNull(8)
            ? []
            : JsonSerializer.Deserialize<List<StoredRecommendation>>(reader.GetString(8), JsonOptions) ?? [],
    };

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
