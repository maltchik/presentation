using System.Net;
using System.Net.Http.Json;
using FeedbackService.Feedback;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>AC-7: no valid session -&gt; 401, nothing stored. The contract pins the same
/// { "error", "message" } envelope for 400/401/429.</summary>
public sealed class AuthenticationTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public AuthenticationTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task UnauthenticatedRequest_Returns401AndStoresNothing()
    {
        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), subject: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        Assert.Equal(0, FeedbackDbReader.CountFeedback(connection));
        Assert.Equal(0, FeedbackDbReader.CountRateLimit(connection));
    }

    [Fact]
    public async Task UnauthenticatedRequest_ReturnsTheContractPinnedErrorEnvelope()
    {
        // Regression: .RequireAuthorization() alone produces an empty 401 body with no
        // Content-Type. The contract requires { "error", "message" } on every 400/401/429.
        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), subject: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);

        var body = await response.Content.ReadFromJsonAsync<FeedbackErrorResponse>(FeedbackTestHelpers.JsonOptions);
        Assert.NotNull(body);
        // Not one of the story's six pinned codes (it defines no auth-specific code) - see
        // FeedbackErrorCodes.AuthenticationRequired.
        Assert.Equal(FeedbackErrorCodes.AuthenticationRequired, body!.Error);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }
}
