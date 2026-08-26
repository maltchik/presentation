using System.Net;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// AC-9: a stored feedback record contains no user identifier, account reference or device
/// identifier - only rating, comment, release and received time.
/// </summary>
public sealed class PrivacyTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public PrivacyTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task FeedbackTable_HasExactlyTheDocumentedColumns_NoIdentityColumn()
    {
        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), "subject-alpha");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var columns = FeedbackDbReader.GetFeedbackColumnNames(connection);

        Assert.Equal(
            new[] { "id", "release", "rating", "comment", "received_at" },
            columns);
    }

    [Fact]
    public async Task StoredRows_DoNotDefaultAnyFieldToTheCallerIdentity()
    {
        // A real, plausible bug this guards against: some code path defaulting a text column -
        // most plausibly `comment` when it's omitted - to the caller's session subject, e.g. as
        // an unintentional audit trail. (Comparing the subject against `id` (a server-generated
        // GUID), `release` (a caller-supplied-but-fixed-in-this-test value) or `received_at` (a
        // server-generated timestamp) would be tautological: those columns are structurally
        // nothing like an arbitrary subject string and can never coincidentally equal one,
        // regardless of whether the implementation is correct.)
        const string subject = "subject-should-never-appear-in-storage";

        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), subject);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var row = FeedbackDbReader.ReadAllFeedback(connection).Single();

        Assert.Null(row.Comment);
    }

    [Fact]
    public async Task RateLimitTable_StoresOnlyAOneWayHmacKey_NeverThePlainSubject()
    {
        const string subject = "subject-should-not-appear-in-storage";
        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), subject);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var hmacKey = FeedbackDbReader.ReadRateLimitHmacKeys(connection).Single();

        Assert.NotEqual(subject, hmacKey);
        Assert.DoesNotContain(subject, hmacKey, StringComparison.Ordinal);
        // HMACSHA256 -> 32 bytes -> 64 hex characters.
        Assert.Equal(64, hmacKey.Length);
    }

    [Fact]
    public async Task RateLimitKey_IsActuallyKeyed_DifferentSecretsProduceDifferentHmacsForTheSameSubjectAndRelease()
    {
        // Coverage gap: nothing above proves the HMAC is keyed at all. An unsalted
        // SHA256(subject + release) would produce a 64-hex-char value indistinguishable from a
        // correctly keyed HMAC and would pass every other assertion in this file.
        const string subject = "same-subject";
        const string release = "1.0";

        using var factoryA = new FeedbackServiceFactory(rateLimitKey: "test-secret-key-aaaaaaaaaaaaaaaaaaaa");
        using var clientA = factoryA.CreateClient();
        using var factoryB = new FeedbackServiceFactory(rateLimitKey: "test-secret-key-bbbbbbbbbbbbbbbbbbbb");
        using var clientB = factoryB.CreateClient();

        var responseA = await clientA.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), subject);
        var responseB = await clientB.PostFeedbackAsync(FeedbackTestHelpers.ValidJson(release, 5), subject);
        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);

        using var connectionA = factoryA.OpenDirectConnection();
        using var connectionB = factoryB.OpenDirectConnection();
        var hmacA = FeedbackDbReader.ReadRateLimitHmacKeys(connectionA).Single();
        var hmacB = FeedbackDbReader.ReadRateLimitHmacKeys(connectionB).Single();

        Assert.NotEqual(hmacA, hmacB);
    }
}
