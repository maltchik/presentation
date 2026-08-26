using System.Net;
using System.Net.Http.Json;
using FeedbackService.Feedback;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>AC-1: valid submission -&gt; 201 with the right shape, and the stored row matches
/// what was sent. There is no read endpoint, so read-back is verified directly against SQLite.</summary>
public sealed class SubmissionAcceptanceTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public SubmissionAcceptanceTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task ValidSubmission_Returns201WithExpectedShape_AndPersistsVerbatim()
    {
        var json = FeedbackTestHelpers.ValidJson(release: "2.4", rating: 4, comment: "the new nav hides search");

        var response = await _client.PostFeedbackAsync(json, subject: "user-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<FeedbackCreatedResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Id));
        Assert.Equal("2.4", body.Release);
        Assert.True((DateTime.UtcNow - body.ReceivedAt.ToUniversalTime()).Duration() < TimeSpan.FromMinutes(1));

        using var connection = _factory.OpenDirectConnection();
        var row = Assert.Single(FeedbackDbReader.ReadAllFeedback(connection));
        Assert.Equal(body.Id, row.Id);
        Assert.Equal("2.4", row.Release);
        Assert.Equal(4, row.Rating);
        Assert.Equal("the new nav hides search", row.Comment);
    }

    [Fact]
    public async Task ValidSubmission_WithCommentOmitted_StoresNullCommentAndReadsBackOtherFields()
    {
        var json = FeedbackTestHelpers.ValidJson(release: "1.0", rating: 3, comment: null);

        var response = await _client.PostFeedbackAsync(json, subject: "user-2");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackCreatedResponse>(FeedbackTestHelpers.JsonOptions);

        using var connection = _factory.OpenDirectConnection();
        var row = Assert.Single(FeedbackDbReader.ReadAllFeedback(connection));
        Assert.Equal(body!.Id, row.Id);
        Assert.Equal("1.0", row.Release);
        Assert.Equal(3, row.Rating);
        Assert.Null(row.Comment);
    }

    [Fact]
    public async Task ValidSubmission_WithCommentPresent_ReadsBackSameComment()
    {
        var json = FeedbackTestHelpers.ValidJson(release: "3.0-beta", rating: 1, comment: "some feedback text");

        var response = await _client.PostFeedbackAsync(json, subject: "user-3");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var row = Assert.Single(FeedbackDbReader.ReadAllFeedback(connection));
        Assert.Equal("some feedback text", row.Comment);
        Assert.Equal("3.0-beta", row.Release);
        Assert.Equal(1, row.Rating);
    }
}
