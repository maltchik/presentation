using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FeedbackService.Feedback;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// AC-5: release must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$ and is stored verbatim (no
/// trimming, no case normalisation). Missing/empty/whitespace -&gt; release_required. Present but
/// non-matching -&gt; release_invalid.
/// </summary>
public sealed class ReleaseValidationTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public ReleaseValidationTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static string BuildJsonWithRawRelease(string releaseRawJson, int rating = 5) =>
        $$"""{"release":{{releaseRawJson}},"rating":{{rating}}}""";

    private async Task<HttpResponseMessage> PostAsync(string release, string subject) =>
        await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release), subject);

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task ReleaseAtOrUnderMaxLength_IsAccepted(int length)
    {
        var release = new string('a', length);
        var response = await PostAsync(release, $"user-len-{length}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseOverMaxLength_IsInvalid()
    {
        var release = new string('a', 21);
        var response = await PostAsync(release, "user-len-21");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseInvalid, body!.Error);
    }

    [Theory]
    [InlineData(".1.0")]
    [InlineData("-1.0")]
    [InlineData("_1.0")]
    [InlineData("1. 0")]
    [InlineData("1 0")]
    public async Task ReleaseWithLeadingPunctuationOrEmbeddedSpace_IsInvalid(string release)
    {
        var response = await PostAsync(release, $"user-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseInvalid, body!.Error);
    }

    [Fact]
    public async Task ReleaseWithTrailingNewline_IsInvalid()
    {
        // Regression for the reviewer-reproduced defect: in .NET, `$` matches at end-of-string OR
        // immediately before a trailing '\n', so the old ^...$ pattern let a trailing newline -
        // a control character - through and stored it verbatim.
        var response = await PostAsync("1.0\n", "user-trailing-newline");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseInvalid, body!.Error);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(0, FeedbackDbReader.CountFeedback(connection));
    }

    [Fact]
    public async Task ReleaseAtMaxLengthWithTrailingNewline_IsInvalid()
    {
        // Regression: a 20-char release plus a trailing '\n' (21 UTF-16 code units) used to still
        // match ^...{0,19}$, defeating the 20-char cap the pattern exists to enforce.
        var release = new string('a', 20) + "\n";
        var response = await PostAsync(release, "user-trailing-newline-max-length");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseInvalid, body!.Error);
    }

    [Fact]
    public async Task MissingRelease_ReturnsReleaseRequired()
    {
        var response = await _client.PostFeedbackAsync("""{"rating":5}""", "user-missing");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseRequired, body!.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceRelease_ReturnsReleaseRequired(string release)
    {
        var response = await PostAsync(release, $"user-empty-{release.Length}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseRequired, body!.Error);
    }

    [Fact]
    public async Task NullRelease_ReturnsReleaseRequired()
    {
        var response = await _client.PostFeedbackAsync("""{"release":null,"rating":5}""", "user-null-release");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseRequired, body!.Error);
    }

    [Fact]
    public async Task NonStringRelease_ReturnsReleaseInvalid()
    {
        var response = await _client.PostFeedbackAsync(BuildJsonWithRawRelease("123"), "user-numeric-release");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.ReleaseInvalid, body!.Error);
    }

    [Fact]
    public async Task Release_IsStoredVerbatim_NoTrimmingOrCaseNormalisation()
    {
        const string release = "MyRelease-2.4A";
        var response = await PostAsync(release, "user-verbatim");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var row = FeedbackDbReader.ReadAllFeedback(connection).Single();
        Assert.Equal(release, row.Release);
    }
}
