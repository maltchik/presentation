using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FeedbackService.Feedback;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// A held SQLite write lock must surface as a well-formed 503 (with the pinned error envelope),
/// not an unhandled 500 after stalling out busy_timeout. Uses a small configured
/// BusyTimeoutMilliseconds so the test is fast and deterministic rather than waiting out the
/// production 5s default. Note: Microsoft.Data.Sqlite's busy-wait has whole-second granularity
/// (see FeedbackDatabase), so a sub-second configured value still rounds up to 1 second here.
/// </summary>
public sealed class StorageContentionTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory;
    private readonly HttpClient _client;

    public StorageContentionTests()
    {
        _factory = new FeedbackServiceFactory(
            additionalConfiguration: new Dictionary<string, string?> { ["Feedback:BusyTimeoutMilliseconds"] = "300" });
        _client = _factory.CreateClient(); // starts the host: creates the schema and sets WAL on the file
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task WriteLockHeldByAnotherConnection_Returns503WithEnvelope_AndStoresNothing()
    {
        using var lockingConnection = _factory.OpenDirectConnection();
        using (var beginCommand = lockingConnection.CreateCommand())
        {
            beginCommand.CommandText = "BEGIN IMMEDIATE;";
            beginCommand.ExecuteNonQuery();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // The app's own write connection will wait to acquire the write lock this test is
            // holding, then raise SQLITE_BUSY - which Program.cs must catch and turn into a 503,
            // not a bare 500.
            var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), "user-contended");
            stopwatch.Stop();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
            Assert.NotNull(body);
            // Not one of the story's six pinned codes - see FeedbackErrorCodes.StorageBusy.
            Assert.Equal(FeedbackErrorCodes.StorageBusy, body!.Error);
            Assert.False(string.IsNullOrWhiteSpace(body.Message));

            // Bounds the wait to roughly the configured timeout, not the driver's 30-second
            // default - guards against the exact defect this test exists to catch (a
            // `PRAGMA busy_timeout` statement that Microsoft.Data.Sqlite silently ignores).
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed} to fail over.");
        }
        finally
        {
            using var rollbackCommand = lockingConnection.CreateCommand();
            rollbackCommand.CommandText = "ROLLBACK;";
            rollbackCommand.ExecuteNonQuery();
        }

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(0, FeedbackDbReader.CountFeedback(connection));
    }
}
