namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// Nonsensical configuration (a submission cap or retention window &lt;= 0) must fail fast at
/// startup rather than silently misbehave at runtime (every submission 429ing, or the entire
/// store being purged on the first run).
/// </summary>
public sealed class FeedbackOptionsValidationTests
{
    [Theory]
    [InlineData("Feedback:SubmissionLimitPerRelease", "0")]
    [InlineData("Feedback:SubmissionLimitPerRelease", "-1")]
    [InlineData("Feedback:RetentionMonths", "0")]
    [InlineData("Feedback:RetentionMonths", "-1")]
    [InlineData("Feedback:BusyTimeoutMilliseconds", "-1")]
    public void InvalidConfiguration_ThrowsAtStartup_InsteadOfMisbehavingAtRuntime(string key, string value)
    {
        using var factory = new FeedbackServiceFactory(
            additionalConfiguration: new Dictionary<string, string?> { [key] = value });

        // WebApplicationFactory builds/starts the host - running Program.cs's fail-fast
        // validation - the first time the server is touched.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
