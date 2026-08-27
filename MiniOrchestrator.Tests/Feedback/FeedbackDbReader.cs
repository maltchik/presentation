using Microsoft.Data.Sqlite;

namespace MiniOrchestrator.Tests.Feedback;

internal sealed record StoredFeedbackRow(string Id, string Release, long Rating, string? Comment, string ReceivedAt);

/// <summary>
/// Direct SQLite access used by developer-owned integration tests for the ACs the story says
/// cannot be verified through the API alone (AC-1, AC-9, AC-10), since there is deliberately no
/// read endpoint.
/// </summary>
internal static class FeedbackDbReader
{
    public static List<StoredFeedbackRow> ReadAllFeedback(SqliteConnection connection)
    {
        var rows = new List<StoredFeedbackRow>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, \"release\", rating, comment, received_at FROM feedback;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new StoredFeedbackRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4)));
        }

        return rows;
    }

    public static int CountFeedback(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM feedback;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static int CountRateLimit(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM feedback_rate_limit;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static List<string> GetFeedbackColumnNames(SqliteConnection connection)
    {
        var columns = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(feedback);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        return columns;
    }

    /// <summary>Seeds a feedback row directly with a caller-controlled received_at, bypassing
    /// the API, so retention purge tests (AC-10) can precisely control record age.</summary>
    public static void InsertRawFeedback(
        SqliteConnection connection, string id, string release, int rating, string? comment, string receivedAtIso)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO feedback (id, "release", rating, comment, received_at)
            VALUES ($id, $release, $rating, $comment, $receivedAt);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$release", release);
        command.Parameters.AddWithValue("$rating", rating);
        command.Parameters.AddWithValue("$comment", (object?)comment ?? DBNull.Value);
        command.Parameters.AddWithValue("$receivedAt", receivedAtIso);
        command.ExecuteNonQuery();
    }

    public static void InsertRawRateLimit(SqliteConnection connection, string hmacKey, string receivedAtIso)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO feedback_rate_limit (hmac_key, received_at) VALUES ($key, $receivedAt);";
        command.Parameters.AddWithValue("$key", hmacKey);
        command.Parameters.AddWithValue("$receivedAt", receivedAtIso);
        command.ExecuteNonQuery();
    }

    public static List<string> ReadRateLimitHmacKeys(SqliteConnection connection)
    {
        var keys = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT hmac_key FROM feedback_rate_limit;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            keys.Add(reader.GetString(0));
        return keys;
    }
}
