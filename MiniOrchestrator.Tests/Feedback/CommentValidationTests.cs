using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FeedbackService.Feedback;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// AC-4: comment must be &lt;= 500 UTF-16 code units (String.Length), never truncated.
/// Decision 1: empty/whitespace-only comment normalises to NULL.
/// </summary>
public sealed class CommentValidationTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public CommentValidationTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static string BuildJson(string release, int rating, string comment)
    {
        var obj = new JsonObject { ["release"] = release, ["rating"] = rating, ["comment"] = comment };
        return obj.ToJsonString();
    }

    [Theory]
    [InlineData(499)]
    [InlineData(500)]
    public async Task CommentAtOrUnderLimit_IsAcceptedAndStoredWithoutTruncation(int length)
    {
        var comment = new string('x', length);
        var subject = $"user-{length}";
        var response = await _client.PostFeedbackAsync(BuildJson("1.0", 5, comment), subject);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var row = FeedbackDbReader.ReadAllFeedback(connection).Single();
        Assert.Equal(length, row.Comment!.Length);
        Assert.Equal(comment, row.Comment);
    }

    [Fact]
    public async Task CommentOverLimit_ReturnsCommentTooLong_AndStoresNothing()
    {
        var comment = new string('x', 501);
        var response = await _client.PostFeedbackAsync(BuildJson("1.0", 5, comment), "user-501");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.CommentTooLong, body!.Error);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(0, FeedbackDbReader.CountFeedback(connection));
    }

    [Fact]
    public async Task TwoHundredFiftyEmoji_Is500Utf16Units_AndIsAccepted()
    {
        // Each emoji here is a surrogate pair -> 2 UTF-16 code units each -> 500 total.
        var comment = string.Concat(Enumerable.Repeat("\U0001F600", 250));
        Assert.Equal(500, comment.Length);

        var response = await _client.PostFeedbackAsync(BuildJson("1.0", 5, comment), "user-emoji-500");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var row = FeedbackDbReader.ReadAllFeedback(connection).Single();
        Assert.Equal(comment, row.Comment);
        Assert.Equal(500, row.Comment!.Length);
    }

    [Fact]
    public async Task TwoHundredFiftyOneEmoji_Is502Utf16Units_AndIsRejected()
    {
        var comment = string.Concat(Enumerable.Repeat("\U0001F600", 251));
        Assert.Equal(502, comment.Length);

        var response = await _client.PostFeedbackAsync(BuildJson("1.0", 5, comment), "user-emoji-502");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.CommentTooLong, body!.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceComment_NormalisesToNull(string comment)
    {
        var response = await _client.PostFeedbackAsync(BuildJson("1.0", 5, comment), $"user-blank-{comment.Length}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var row = FeedbackDbReader.ReadAllFeedback(connection).Single();
        Assert.Null(row.Comment);
    }

    [Fact]
    public async Task CommentOmitted_IsIndistinguishableFromEmptyString_BothNull()
    {
        var omittedResponse = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), "user-omitted");
        var emptyResponse = await _client.PostFeedbackAsync(BuildJson("2.0", 5, ""), "user-empty");

        Assert.Equal(HttpStatusCode.Created, omittedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, emptyResponse.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var rows = FeedbackDbReader.ReadAllFeedback(connection);
        Assert.All(rows, r => Assert.Null(r.Comment));
    }

    [Theory]
    [InlineData("""{"a":1}""")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("[1,2]")]
    public async Task NonStringComment_IsRejected_NotSilentlyStringified(string commentRawJson)
    {
        // Regression for the reviewer-reproduced defect: a non-string comment used to be
        // stringified via GetRawText() and stored verbatim (e.g. {"a":1} -> the literal text
        // "{\"a\":1}"), corrupting stored data instead of failing validation. There is no
        // dedicated error code for this in the story's six, so it is rejected under
        // comment_too_long (see FeedbackRequestParser) - the same extension pattern used for a
        // non-string release under release_invalid.
        var json = $$"""{"release":"1.0","rating":5,"comment":{{commentRawJson}}}""";
        var response = await _client.PostFeedbackAsync(json, $"user-nonstring-comment-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.CommentTooLong, body!.Error);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(0, FeedbackDbReader.CountFeedback(connection));
    }
}
