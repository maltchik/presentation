using System.Net;
using System.Net.Http.Json;
using FeedbackService.Feedback;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// AC-8: once a caller has had 3 submissions stored for a given release, a 4th for that same
/// release is rejected with 429/submission_limit_reached and nothing is stored. The cap is per
/// caller per release. Decision 4: rejected (400) attempts never consume the cap.
/// </summary>
public sealed class SubmissionLimitTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public SubmissionLimitTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task ThreeSubmissionsSucceed_FourthForSameCallerAndRelease_Returns429AndStoresNothing()
    {
        const string subject = "user-cap";
        const string release = "1.0";

        for (var i = 0; i < 3; i++)
        {
            var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), subject);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var fourth = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), subject);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        var body = await fourth.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.SubmissionLimitReached, body!.Error);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(3, FeedbackDbReader.CountFeedback(connection));
    }

    [Fact]
    public async Task SameCaller_DifferentRelease_StillSucceedsAfterCapReached()
    {
        const string subject = "user-cap-2";

        for (var i = 0; i < 3; i++)
            await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), subject);

        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("2.0", 5), subject);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DifferentCaller_SameRelease_StillSucceedsAfterCapReachedByAnotherCaller()
    {
        const string release = "1.0";

        for (var i = 0; i < 3; i++)
            await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), "user-cap-3a");

        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), "user-cap-3b");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RejectedAttempts_DoNotConsumeTheCap()
    {
        const string subject = "user-cap-4";
        const string release = "1.0";

        // Three 400s (bad rating) followed by four valid POSTs: the first three of those four
        // succeed and the fourth is 429 - because rejected attempts never counted against the cap.
        for (var i = 0; i < 3; i++)
        {
            var rejected = await _client.PostFeedbackAsync($$"""{"release":"{{release}}","rating":0}""", subject);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        for (var i = 0; i < 3; i++)
        {
            var accepted = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), subject);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        var fourthValid = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), subject);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthValid.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(3, FeedbackDbReader.CountFeedback(connection));
    }
}
