using FeedbackService;
using FeedbackService.Db;
using FeedbackService.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// AC-10: a record whose received time is more than 24 months in the past is permanently deleted
/// when the retention purge runs; purge runs at startup and daily thereafter.
///
/// There is no read endpoint or trigger-purge endpoint, so these tests seed rows directly via
/// SQLite with a controlled received_at, then invoke the same purge routine the hosted service
/// uses (resolved from the running host's DI container) to get a deterministic result instead of
/// waiting for the daily timer.
/// </summary>
public sealed class RetentionPurgeTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public RetentionPurgeTests() => _client = _factory.CreateClient(); // starts the host, creates schema

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public void RetentionPurgeHostedService_IsRegistered_SoItRunsAtStartupAndOnTheDailyTimer()
    {
        var hostedServices = _factory.Services.GetServices<IHostedService>();
        Assert.Contains(hostedServices, s => s is RetentionPurgeHostedService);
    }

    [Fact]
    public async Task HostedService_ComputesCutoff_UsingDecision5Formula_AndPurgesAccordingly()
    {
        // Exercises RetentionPurgeHostedService.PurgeOnceAsync directly - the same method its
        // background loop runs at startup and on the daily timer - with an injected TimeProvider,
        // so decision 5's cutoff formula (UtcNow.AddMonths(-RetentionMonths)) is verified as
        // actually computed by the hosted service itself. Every other test in this file calls
        // FeedbackDatabase.PurgeOlderThanAsync with a cutoff the test computes itself, which never
        // exercises RetentionPurgeHostedService's own cutoff computation at all.
        var options = _factory.Services.GetRequiredService<FeedbackOptions>();
        var database = _factory.Services.GetRequiredService<FeedbackDatabase>();

        var fixedNow = new DateTimeOffset(2026, 3, 10, 8, 30, 0, TimeSpan.Zero);
        var cutoff = fixedNow.UtcDateTime.AddMonths(-options.RetentionMonths);

        var purgedId = await SeedFeedbackRowAsync(cutoff.AddDays(-1));
        var survivingId = await SeedFeedbackRowAsync(cutoff.AddDays(1));

        var hostedService = new RetentionPurgeHostedService(
            database, options, new FixedTimeProvider(fixedNow), NullLogger<RetentionPurgeHostedService>.Instance);

        await hostedService.PurgeOnceAsync(CancellationToken.None);

        using var connection = _factory.OpenDirectConnection();
        var rows = FeedbackDbReader.ReadAllFeedback(connection);
        Assert.DoesNotContain(rows, r => r.Id == purgedId);
        Assert.Contains(rows, r => r.Id == survivingId);
    }

    [Fact]
    public async Task RecordOlderThan24Months_IsPurged()
    {
        // Derive both the seeded row's age and the purge cutoff from the same anchor instant, so
        // the boundary comparison isn't at the mercy of wall-clock time passing between seeding
        // and purging (which would otherwise make an "exactly at the cutoff" case flaky).
        var cutoff = DateTime.UtcNow.AddMonths(-24);
        var id = await SeedFeedbackRowAsync(cutoff.AddDays(-1));

        await PurgeAsync(cutoff);

        using var connection = _factory.OpenDirectConnection();
        Assert.DoesNotContain(FeedbackDbReader.ReadAllFeedback(connection), r => r.Id == id);
    }

    [Fact]
    public async Task RecordExactly24MonthsOld_Survives()
    {
        var cutoff = DateTime.UtcNow.AddMonths(-24);
        var id = await SeedFeedbackRowAsync(cutoff);

        await PurgeAsync(cutoff);

        using var connection = _factory.OpenDirectConnection();
        Assert.Contains(FeedbackDbReader.ReadAllFeedback(connection), r => r.Id == id);
    }

    [Fact]
    public async Task RecordJustUnder24MonthsOld_Survives()
    {
        var cutoff = DateTime.UtcNow.AddMonths(-24);
        var id = await SeedFeedbackRowAsync(cutoff.AddDays(1));

        await PurgeAsync(cutoff);

        using var connection = _factory.OpenDirectConnection();
        Assert.Contains(FeedbackDbReader.ReadAllFeedback(connection), r => r.Id == id);
    }

    [Fact]
    public async Task ExpiredRateLimitRows_AreAlsoPurged()
    {
        var cutoff = DateTime.UtcNow.AddMonths(-24);
        using (var seedConnection = _factory.OpenDirectConnection())
        {
            FeedbackDbReader.InsertRawRateLimit(seedConnection, "some-hmac-key", FeedbackTimeFormat.ToStorageString(cutoff.AddDays(-1)));
        }

        using (var beforePurgeConnection = _factory.OpenDirectConnection())
            Assert.Equal(1, FeedbackDbReader.CountRateLimit(beforePurgeConnection));

        await PurgeAsync(cutoff);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(0, FeedbackDbReader.CountRateLimit(connection));
    }

    [Fact]
    public async Task FreshRateLimitRows_SurvivePurge()
    {
        var cutoff = DateTime.UtcNow.AddMonths(-24);
        using (var seedConnection = _factory.OpenDirectConnection())
        {
            FeedbackDbReader.InsertRawRateLimit(seedConnection, "fresh-hmac-key", FeedbackTimeFormat.ToStorageString(DateTime.UtcNow.AddDays(-1)));
        }

        await PurgeAsync(cutoff);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(1, FeedbackDbReader.CountRateLimit(connection));
    }

    private async Task<string> SeedFeedbackRowAsync(DateTime receivedAtUtc)
    {
        var id = Guid.NewGuid().ToString("N");
        using var connection = _factory.OpenDirectConnection();
        FeedbackDbReader.InsertRawFeedback(
            connection, id, "1.0", 5, comment: null, FeedbackTimeFormat.ToStorageString(receivedAtUtc));
        await Task.CompletedTask;
        return id;
    }

    private async Task PurgeAsync(DateTime cutoff)
    {
        var database = _factory.Services.GetRequiredService<FeedbackDatabase>();
        await database.PurgeOlderThanAsync(cutoff);
    }
}
