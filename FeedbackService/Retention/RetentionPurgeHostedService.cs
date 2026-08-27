using FeedbackService.Db;

namespace FeedbackService.Retention;

/// <summary>
/// Runs the retention purge (AC-10) once at startup, then once a day thereafter, for as long as
/// the host is running. Deletes feedback rows - and the associated rate-limit rows, whose keys
/// are "discarded by the retention purge" per the story - received more than
/// <see cref="FeedbackOptions.RetentionMonths"/> months ago.
/// </summary>
public sealed class RetentionPurgeHostedService : BackgroundService
{
    private readonly FeedbackDatabase _database;
    private readonly FeedbackOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RetentionPurgeHostedService> _logger;

    public RetentionPurgeHostedService(
        FeedbackDatabase database, FeedbackOptions options, TimeProvider timeProvider, ILogger<RetentionPurgeHostedService> logger)
    {
        _database = database;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PurgeOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(TimeSpan.FromDays(1), _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs a single purge pass using the pinned decision-5 cutoff formula
    /// (<c>UtcNow.AddMonths(-RetentionMonths)</c>, "more than N months old"). Public - rather than
    /// private, exercised only through the background loop above - so tests can invoke it
    /// directly (with an injected <see cref="TimeProvider"/>) and observe this exact cutoff
    /// computation deterministically, since the hosted service's own daily timer cannot be
    /// observed from a test without waiting a day.
    /// </summary>
    public async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddMonths(-_options.RetentionMonths);
            var deleted = await _database.PurgeOlderThanAsync(cutoff, cancellationToken);
            if (deleted > 0)
                _logger.LogInformation("Retention purge removed {DeletedCount} row(s) older than {Cutoff:O}.", deleted, cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention purge failed.");
        }
    }
}
