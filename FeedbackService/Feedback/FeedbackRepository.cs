using FeedbackService.Db;

namespace FeedbackService.Feedback;

/// <summary>
/// Data access for feedback submissions. Enforces the AC-8 cap and the AC-1 storage guarantees
/// inside a single SQLite transaction so a rejected (over-cap) attempt never partially writes.
/// </summary>
public sealed class FeedbackRepository
{
    private readonly FeedbackDatabase _database;
    private readonly int _submissionLimit;

    public FeedbackRepository(FeedbackDatabase database, FeedbackOptions options)
    {
        _database = database;
        _submissionLimit = options.SubmissionLimitPerRelease;
    }

    /// <summary>
    /// Attempts to store a submission. Returns <c>null</c> if the caller has already reached the
    /// per-release submission cap (AC-8) - in that case nothing is written.
    /// </summary>
    public async Task<FeedbackCreatedResponse?> TryInsertAsync(
        string release, int rating, string? comment, string hmacKey, CancellationToken cancellationToken)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM feedback_rate_limit WHERE hmac_key = $key;";
            countCommand.Parameters.AddWithValue("$key", hmacKey);
            var count = (long)(await countCommand.ExecuteScalarAsync(cancellationToken))!;

            if (count >= _submissionLimit)
            {
                transaction.Rollback();
                return null;
            }
        }

        var id = Guid.NewGuid().ToString("N");
        var receivedAt = DateTime.UtcNow;
        var receivedAtText = FeedbackTimeFormat.ToStorageString(receivedAt);

        using (var insertFeedback = connection.CreateCommand())
        {
            insertFeedback.Transaction = transaction;
            insertFeedback.CommandText = """
                INSERT INTO feedback (id, "release", rating, comment, received_at)
                VALUES ($id, $release, $rating, $comment, $receivedAt);
                """;
            insertFeedback.Parameters.AddWithValue("$id", id);
            insertFeedback.Parameters.AddWithValue("$release", release);
            insertFeedback.Parameters.AddWithValue("$rating", rating);
            insertFeedback.Parameters.AddWithValue("$comment", (object?)comment ?? DBNull.Value);
            insertFeedback.Parameters.AddWithValue("$receivedAt", receivedAtText);
            await insertFeedback.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var insertRateLimit = connection.CreateCommand())
        {
            insertRateLimit.Transaction = transaction;
            insertRateLimit.CommandText =
                "INSERT INTO feedback_rate_limit (hmac_key, received_at) VALUES ($key, $receivedAt);";
            insertRateLimit.Parameters.AddWithValue("$key", hmacKey);
            insertRateLimit.Parameters.AddWithValue("$receivedAt", receivedAtText);
            await insertRateLimit.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();

        return new FeedbackCreatedResponse(id, release, receivedAt);
    }
}
