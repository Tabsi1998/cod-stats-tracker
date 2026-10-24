using System.Globalization;
using System.Text.Json;
using CodStats.Contracts;
using Microsoft.Data.Sqlite;

namespace CodStats.Desktop.Storage;

/// <summary>Where a local match stands with the server.</summary>
public enum SyncState
{
    /// <summary>Not on the server yet (new, or waiting for a retry).</summary>
    Waiting,
    /// <summary>Corrected locally after it was uploaded; the correction still has to go up.</summary>
    CorrectionWaiting,
    Synced,
    /// <summary>The server refused it; the reason is kept and it is not sent again unless the player corrects it.</summary>
    Rejected,
}

public sealed record LocalMatch(
    Guid Id,
    Game Game,
    string? Mode,
    DateTimeOffset PlayedAt,
    int? Kills,
    int? Deaths,
    int? Assists,
    MatchResult? Result,
    int? Placement,
    string? Map,
    int? Score,
    double Confidence,
    ReviewStatus ReviewStatus,
    string? RecognizerVersion,
    SyncState SyncState,
    string? SyncError,
    MatchStatus Status = MatchStatus.Completed,
    MatchDetails? Details = null)
{
    public MatchUpload ToUpload() => new(Id, Game, Mode, PlayedAt, Kills, Deaths, Assists, Result, Placement, Map, Score,
        Confidence, ReviewStatus, RecognizerVersion, Status, Details is { IsEmpty: false } ? Details : null);

    public MatchCorrectionRequest ToCorrection() => new(Mode, PlayedAt, Kills, Deaths, Assists, Result, Placement, Map, Score);

    public MatchFacts ToFacts() => new(Id, Game, PlayedAt, Kills, Deaths, Assists, Result, Placement, Status, Mode, Map, Score,
        Details?.Eliminations, Details?.Xp);
}

/// <summary>The local SQLite database. It holds only this player's own matches.</summary>
public sealed class MatchStore(string path)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Initialise()
    {
        using var connection = Open();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS matches (
                id TEXT PRIMARY KEY,
                game TEXT NOT NULL,
                mode TEXT,
                played_at TEXT NOT NULL,
                kills INTEGER,
                deaths INTEGER,
                assists INTEGER,
                result TEXT,
                placement INTEGER,
                map TEXT,
                score INTEGER,
                confidence REAL NOT NULL,
                review_status TEXT NOT NULL,
                recognizer_version TEXT,
                sync_state TEXT NOT NULL,
                sync_error TEXT,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS matches_played_at ON matches (played_at);
            CREATE INDEX IF NOT EXISTS matches_sync_state ON matches (sync_state);
            PRAGMA journal_mode = WAL;
            """);
        // Columns added in 0.2.0; databases of older clients get them here, their matches count as completed.
        var columns = new List<string>();
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(matches)";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }
        if (!columns.Contains("status"))
        {
            Execute(connection, "ALTER TABLE matches ADD COLUMN status TEXT NOT NULL DEFAULT 'completed'");
        }
        if (!columns.Contains("details"))
        {
            Execute(connection, "ALTER TABLE matches ADD COLUMN details TEXT");
        }
    }

    /// <summary>Stores a new match. Returns false when a match with the same id or the same result already exists.</summary>
    public bool Add(LocalMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        using var connection = Open();
        if (IsDuplicate(connection, match))
        {
            return false;
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO matches (id, game, mode, played_at, kills, deaths, assists, result, placement, map, score,
                confidence, review_status, recognizer_version, sync_state, sync_error, created_at, status, details)
            VALUES ($id, $game, $mode, $played, $kills, $deaths, $assists, $result, $placement, $map, $score,
                $confidence, $review, $recognizer, $sync, NULL, $created, $status, $details)
            """;
        Bind(command, match);
        command.Parameters.AddWithValue("$sync", match.SyncState.ToString());
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>The player's correction. A synced match keeps its id and goes up again as a correction.</summary>
    public void Correct(LocalMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE matches SET mode = $mode, played_at = $played, kills = $kills, deaths = $deaths, assists = $assists,
                result = $result, placement = $placement, map = $map, score = $score, review_status = $review,
                status = CASE WHEN $result IS NULL THEN status ELSE 'completed' END,
                sync_error = NULL,
                sync_state = CASE sync_state WHEN 'Synced' THEN 'CorrectionWaiting' WHEN 'CorrectionWaiting' THEN 'CorrectionWaiting' ELSE 'Waiting' END
            WHERE id = $id
            """;
        Bind(command, match with { ReviewStatus = ReviewStatus.Corrected });
        command.ExecuteNonQuery();
    }

    public void SetSyncState(Guid id, SyncState state, string? error = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE matches SET sync_state = $state, sync_error = $error WHERE id = $id";
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<LocalMatch> All() => Query("SELECT * FROM matches ORDER BY played_at");

    /// <summary>Matches the server should get: reviewed or safely recognised, not yet on the server.</summary>
    public IReadOnlyList<LocalMatch> ToUpload(int limit) =>
        Query($"SELECT * FROM matches WHERE sync_state = 'Waiting' AND review_status <> 'pending' ORDER BY played_at LIMIT {limit}");

    public IReadOnlyList<LocalMatch> CorrectionsToSend() =>
        Query("SELECT * FROM matches WHERE sync_state = 'CorrectionWaiting' ORDER BY played_at");

    public int Count(SyncState state)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM matches WHERE sync_state = $state";
        command.Parameters.AddWithValue("$state", state.ToString());
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Removes all local matches, after the player deleted the profile on the server.</summary>
    public void Clear()
    {
        using var connection = Open();
        Execute(connection, "DELETE FROM matches");
    }

    private static bool IsDuplicate(SqliteConnection connection, LocalMatch match)
    {
        using var command = connection.CreateCommand();
        // Same id, or the same result within three minutes: the result screen was read twice.
        command.CommandText = """
            SELECT played_at FROM matches
            WHERE id = $id OR (game = $game AND kills IS $kills AND deaths IS $deaths AND placement IS $placement
                AND result IS $result AND score IS $score)
            """;
        command.Parameters.AddWithValue("$id", match.Id.ToString());
        command.Parameters.AddWithValue("$game", WireNames.Of(match.Game));
        command.Parameters.AddWithValue("$kills", (object?)match.Kills ?? DBNull.Value);
        command.Parameters.AddWithValue("$deaths", (object?)match.Deaths ?? DBNull.Value);
        command.Parameters.AddWithValue("$placement", (object?)match.Placement ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", match.Result is { } r ? WireNames.Of(r) : DBNull.Value);
        command.Parameters.AddWithValue("$score", (object?)match.Score ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var played = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            if ((played - match.PlayedAt).Duration() <= TimeSpan.FromMinutes(3))
            {
                return true;
            }
        }
        return false;
    }

    private static void Bind(SqliteCommand command, LocalMatch match)
    {
        command.Parameters.AddWithValue("$id", match.Id.ToString());
        command.Parameters.AddWithValue("$game", WireNames.Of(match.Game));
        command.Parameters.AddWithValue("$mode", (object?)match.Mode ?? DBNull.Value);
        command.Parameters.AddWithValue("$played", match.PlayedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kills", (object?)match.Kills ?? DBNull.Value);
        command.Parameters.AddWithValue("$deaths", (object?)match.Deaths ?? DBNull.Value);
        command.Parameters.AddWithValue("$assists", (object?)match.Assists ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", match.Result is { } r ? WireNames.Of(r) : DBNull.Value);
        command.Parameters.AddWithValue("$placement", (object?)match.Placement ?? DBNull.Value);
        command.Parameters.AddWithValue("$map", (object?)match.Map ?? DBNull.Value);
        command.Parameters.AddWithValue("$score", (object?)match.Score ?? DBNull.Value);
        command.Parameters.AddWithValue("$confidence", match.Confidence);
        command.Parameters.AddWithValue("$review", WireNames.Of(match.ReviewStatus));
        command.Parameters.AddWithValue("$recognizer", (object?)match.RecognizerVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", WireNames.Of(match.Status));
        command.Parameters.AddWithValue("$details", match.Details is { IsEmpty: false } details ? JsonSerializer.Serialize(details, Json) : DBNull.Value);
    }

    private List<LocalMatch> Query(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var matches = new List<LocalMatch>();
        while (reader.Read())
        {
            matches.Add(new LocalMatch(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Parse<Game>(reader.GetString(reader.GetOrdinal("game"))),
                NullableString(reader, "mode"),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("played_at")), CultureInfo.InvariantCulture),
                NullableInt(reader, "kills"),
                NullableInt(reader, "deaths"),
                NullableInt(reader, "assists"),
                NullableString(reader, "result") is { } result ? Parse<MatchResult>(result) : null,
                NullableInt(reader, "placement"),
                NullableString(reader, "map"),
                NullableInt(reader, "score"),
                reader.GetDouble(reader.GetOrdinal("confidence")),
                Parse<ReviewStatus>(reader.GetString(reader.GetOrdinal("review_status"))),
                NullableString(reader, "recognizer_version"),
                Enum.Parse<SyncState>(reader.GetString(reader.GetOrdinal("sync_state"))),
                NullableString(reader, "sync_error"),
                Parse<MatchStatus>(reader.GetString(reader.GetOrdinal("status"))),
                NullableString(reader, "details") is { } details ? JsonSerializer.Deserialize<MatchDetails>(details, Json) : null));
        }
        return matches;
    }

    private static T Parse<T>(string text) where T : struct, Enum =>
        WireNames.TryParse<T>(text, out var value) ? value : throw new FormatException($"unknown {typeof(T).Name} '{text}'");

    private static string? NullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? NullableInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
