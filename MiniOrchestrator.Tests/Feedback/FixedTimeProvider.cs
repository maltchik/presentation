namespace MiniOrchestrator.Tests.Feedback;

/// <summary>A <see cref="TimeProvider"/> that always reports a fixed instant, so tests can drive
/// <see cref="FeedbackService.Retention.RetentionPurgeHostedService"/>'s own cutoff computation
/// (decision 5: <c>UtcNow.AddMonths(-RetentionMonths)</c>) deterministically.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
