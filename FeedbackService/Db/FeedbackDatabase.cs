using Microsoft.Data.Sqlite;

namespace FeedbackService.Db;

/// <summary>
/// Owns the SQLite schema for the feedback store: connection creation, idempotent
/// table setup, and retention purge (AC-10). No EF Core / migrations tooling, per the
/// repo's low-dependency convention.
/// </summary>
public sealed class FeedbackDatabase
{
    private readonly string _connectionString;

    public FeedbackDatabase(FeedbackOptions options)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(options.DbPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // IMPORTANT: Microsoft.Data.Sqlite does NOT honour a raw `PRAGMA busy_timeout` statement
        // for its own busy-wait/retry loop - it installs its own managed busy handler (tied to
        // SqliteCommand.CommandTimeout) on every command execution, which supersedes a
        // PRAGMA-installed one. The only supported way to bound that wait is DefaultTimeout on
        // the connection string (whole seconds only - ADO.NET's CommandTimeout is int seconds).
        // Round up so a sub-second configured value (e.g. in tests) still gets a bounded wait
        // rather than falling back to the driver's 30-second default. 0 is not used because
        // ADO.NET treats CommandTimeout = 0 as "no limit".
        var defaultTimeoutSeconds = Math.Max(1, (int)Math.Ceiling(options.BusyTimeoutMilliseconds / 1000.0));

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DbPath,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = defaultTimeoutSeconds,
        }.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // WAL lets readers and a writer proceed concurrently (the default rollback-journal mode
        // blocks readers behind an in-progress writer too); journal_mode is persisted in the
        // database file header, so this is a cheap no-op once already set.
        using var walPragma = connection.CreateCommand();
        walPragma.CommandText = "PRAGMA journal_mode = 'WAL';";
        walPragma.ExecuteNonQuery();

        return connection;
    }

    /// <summary>Creates the schema if it does not already exist. Safe to call repeatedly.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();

        // "release" is a keyword-adjacent identifier in SQLite; quote it defensively.
        // The feedback table intentionally has no identity/user column (AC-9).
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS feedback (
              id          TEXT PRIMARY KEY,
              "release"   TEXT NOT NULL,
              rating      INTEGER NOT NULL,
              comment     TEXT NULL,
              received_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS feedback_rate_limit (
              hmac_key    TEXT NOT NULL,
              received_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_feedback_rate_limit_key
              ON feedback_rate_limit (hmac_key);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Permanently deletes feedback and rate-limit rows received before <paramref name="cutoffUtc"/>
    /// (AC-10). Both tables use the same ISO-8601 "O" string format for received_at, which sorts
    /// lexicographically the same as chronologically, so a plain text comparison is correct.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var cutoffText = FeedbackTimeFormat.ToStorageString(cutoffUtc);

        using var connection = CreateConnection();
        using var transaction = connection.BeginTransaction();

        var deleted = 0;

        using (var feedbackCommand = connection.CreateCommand())
        {
            feedbackCommand.Transaction = transaction;
            feedbackCommand.CommandText = """DELETE FROM feedback WHERE received_at < $cutoff;""";
            feedbackCommand.Parameters.AddWithValue("$cutoff", cutoffText);
            deleted += await feedbackCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var rateLimitCommand = connection.CreateCommand())
        {
            rateLimitCommand.Transaction = transaction;
            rateLimitCommand.CommandText = """DELETE FROM feedback_rate_limit WHERE received_at < $cutoff;""";
            rateLimitCommand.Parameters.AddWithValue("$cutoff", cutoffText);
            deleted += await rateLimitCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return deleted;
    }
}
