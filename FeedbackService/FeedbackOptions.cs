namespace FeedbackService;

/// <summary>
/// Configuration for the feedback service, bound from the "Feedback" configuration section
/// (e.g. appsettings.json, environment variables, or values injected by tests).
/// </summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>
    /// Path to the SQLite database file. Configurable so that tests can point each
    /// <c>WebApplicationFactory</c> instance at its own isolated database file.
    /// </summary>
    public string DbPath { get; set; } = "feedback.db";

    /// <summary>
    /// Key used to derive the per-caller/per-release HMAC used for rate limiting (AC-8).
    /// If not configured, a random key is generated at startup (see Program.cs), which means
    /// the submission cap resets whenever the process restarts.
    /// </summary>
    public string? RateLimitKey { get; set; }

    /// <summary>Maximum number of stored submissions per caller, per release (AC-8). Must be at
    /// least 1 - validated at startup in Program.cs (0 or negative would reject every
    /// submission).</summary>
    public int SubmissionLimitPerRelease { get; set; } = 3;

    /// <summary>Retention window; records older than this are purged (AC-10). Must be at least 1
    /// - validated at startup in Program.cs (0 or negative would purge the entire store on the
    /// very first run).</summary>
    public int RetentionMonths { get; set; } = 24;

    /// <summary>How long a write waits to acquire the SQLite write lock before failing with
    /// SQLITE_BUSY (mapped to 503/storage_busy). Configurable so tests can force contention to
    /// surface quickly instead of waiting out the production default. Expressed in milliseconds
    /// for precision, but internally rounded UP to whole seconds (minimum 1) in
    /// <see cref="Db.FeedbackDatabase"/>, because Microsoft.Data.Sqlite's own busy-wait mechanism
    /// (connection DefaultTimeout / SqliteCommand.CommandTimeout) only has second-level
    /// granularity - a raw `PRAGMA busy_timeout` statement is not honoured by it.</summary>
    public int BusyTimeoutMilliseconds { get; set; } = 5000;
}
