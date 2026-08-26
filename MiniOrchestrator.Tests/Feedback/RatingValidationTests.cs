using System.Net;
using System.Net.Http.Json;
using FeedbackService.Feedback;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>AC-2: rating required. AC-3: rating must be an integer in 1..5.</summary>
public sealed class RatingValidationTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public RatingValidationTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task MissingRating_ReturnsRatingRequired()
    {
        var response = await _client.PostFeedbackAsync("""{"release":"1.0"}""", subject: "user-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.RatingRequired, body!.Error);
    }

    [Fact]
    public async Task NullRating_ReturnsRatingRequired()
    {
        var response = await _client.PostFeedbackAsync("""{"release":"1.0","rating":null}""", subject: "user-2");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.RatingRequired, body!.Error);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    public async Task BoundaryIntegerRatings_AreAccepted(string ratingLiteral)
    {
        var json = $$"""{"release":"1.0","rating":{{ratingLiteral}}}""";
        var response = await _client.PostFeedbackAsync(json, subject: $"user-boundary-{ratingLiteral}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("-1")]
    [InlineData("3.5")]
    [InlineData("\"4\"")]
    [InlineData("true")]
    // A literal with a decimal point or exponent is rejected even when the numeric value it
    // represents is integral and in range: decision 6 says "non-integer -> out_of_range", judged
    // by the JSON literal, not by the value after a lossy round-trip.
    [InlineData("4.0")]
    [InlineData("4e0")]
    [InlineData("4E0")]
    // Regression for the reviewer-reproduced defect: TryGetDecimal loses precision past
    // ~28-29 significant digits, so `value != Math.Truncate(value)` used to be false here,
    // silently coercing this to rating 3.
    [InlineData("3.0000000000000000000000000000000001")]
    // Confirmed-correct robustness cases (huge exponent, huge integer): must never 500, and must
    // still be rejected as out_of_range rather than accidentally required/coerced.
    [InlineData("1e400")]
    [InlineData("100000000000000000000000000000000000000000")]
    public async Task InvalidRatings_ReturnRatingOutOfRange(string ratingLiteral)
    {
        var json = $$"""{"release":"1.0","rating":{{ratingLiteral}}}""";
        var response = await _client.PostFeedbackAsync(json, subject: $"user-invalid-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.Equal(FeedbackErrorCodes.RatingOutOfRange, body!.Error);
    }

    [Fact]
    public async Task InvalidRating_DoesNotStoreAnything()
    {
        const string subject = "user-no-store";
        await _client.PostFeedbackAsync("""{"release":"1.0","rating":0}""", subject);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(0, FeedbackDbReader.CountFeedback(connection));
    }
}
