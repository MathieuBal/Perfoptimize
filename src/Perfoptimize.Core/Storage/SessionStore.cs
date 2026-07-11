using Microsoft.Data.Sqlite;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Optimization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perfoptimize.Core.Storage;

/// <summary>One stored monitoring session.</summary>
public sealed class SessionRecord
{
    public long Id { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public string? GameName { get; init; }
    public BottleneckType Bottleneck { get; init; }
    public PerformanceSummary Summary { get; init; } = new();
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
            INSERT INTO sessions (started_at, game_name, bottleneck, summary_json)
            VALUES ($startedAt, $gameName, $bottleneck, $summaryJson);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$startedAt", record.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$gameName", (object?)record.GameName ?? DBNull.Value);
        command.Parameters.AddWithValue("$bottleneck", record.Bottleneck.ToString());
        command.Parameters.AddWithValue("$summaryJson", JsonSerializer.Serialize(record.Summary, JsonOptions));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Most recent sessions first, optionally filtered by game.</summary>
    public IReadOnlyList<SessionRecord> GetRecent(int limit = 10, string? gameName = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = gameName is null
            ? "SELECT id, started_at, game_name, bottleneck, summary_json FROM sessions ORDER BY started_at DESC LIMIT $limit"
            : "SELECT id, started_at, game_name, bottleneck, summary_json FROM sessions WHERE game_name = $gameName ORDER BY started_at DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        if (gameName is not null)
            command.Parameters.AddWithValue("$gameName", gameName);

        var records = new List<SessionRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new SessionRecord
            {
                Id = reader.GetInt64(0),
                StartedAt = DateTimeOffset.Parse(reader.GetString(1)),
                GameName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Bottleneck = Enum.TryParse<BottleneckType>(reader.GetString(3), out var type)
                    ? type
                    : BottleneckType.Unknown,
                Summary = JsonSerializer.Deserialize<PerformanceSummary>(reader.GetString(4), JsonOptions) ?? new(),
            });
        }
        return records;
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
