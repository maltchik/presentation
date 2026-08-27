using System.Net;
using System.Net.Http.Json;
using FeedbackService.Feedback;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// Decision 3: error precedence when a request violates several rules at once, and malformed
/// JSON handling. Decision 4 (cap not consumed by 400s) is covered in SubmissionLimitTests.
/// </summary>
public sealed class ErrorPrecedenceTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public ErrorPrecedenceTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task UnauthenticatedWithInvalidBody_Returns401_NotBadRequest()
    {
        var response = await _client.PostFeedbackAsync("""{"rating":999}""", subject: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BadReleaseAndBadRating_ReleaseErrorWins()
    {
        var response = await _client.PostFeedbackAsync("""{"release":"!!!","rating":999}""", "user-a");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseInvalid, body!.Error);
    }

    [Fact]
    public async Task MissingReleaseAndBadRating_ReleaseRequiredWins()
    {
        var response = await _client.PostFeedbackAsync("""{"rating":999}""", "user-a2");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseRequired, body!.Error);
    }

    [Fact]
    public async Task BadRatingAndOverlongComment_RatingErrorWins()
    {
        var longComment = new string('x', 501);
        var json = $$"""{"release":"1.0","rating":999,"comment":"{{longComment}}"}""";

        var response = await _client.PostFeedbackAsync(json, "user-b");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.RatingOutOfRange, body!.Error);
    }

    [Fact]
    public async Task OverCapAndBadRating_ReturnsBadRequest_NotTooManyRequests()
    {
        const string subject = "user-c";
        const string release = "1.0";

        for (var i = 0; i < 3; i++)
        {
            var ok = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), subject);
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var response = await _client.PostFeedbackAsync($$"""{"release":"{{release}}","rating":999}""", subject);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.RatingOutOfRange, body!.Error);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("\"just a string\"")]
    [InlineData("{\"release\": \"1.0\", }")]
    public async Task MalformedOrNonObjectBody_ReturnsReleaseRequired(string rawJson)
    {
        var response = await _client.PostFeedbackAsync(rawJson, "user-d");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseRequired, body!.Error);
    }
}
