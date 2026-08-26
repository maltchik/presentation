using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FeedbackService.Db;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>AC-6: stored submission carries the UTC time it was received, in ISO-8601 format.</summary>
public sealed class ReceivedAtTests : IDisposable
{
    private readonly FeedbackServiceFactory _factory = new();
    private readonly HttpClient _client;

    public ReceivedAtTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task ReceivedAt_InResponse_IsCamelCase_EndsInZ_AndParsesAsUtcCloseToNow()
    {
        // Asserted against the RAW wire JSON, not a round-tripped typed deserialisation: reading
        // into a record and re-serialising with ToString("O") before reparsing would make this
        // pass even if the server had sent a PascalCase body (JsonSerializerDefaults.Web reads
        // case-insensitively) or a timestamp missing its UTC "Z" designator.
        var before = DateTime.UtcNow;
        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), "user-1");
        var after = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"id\":", raw);
        Assert.Contains("\"release\":", raw);
        Assert.Contains("\"receivedAt\":", raw);
        Assert.DoesNotContain("\"Id\":", raw);
        Assert.DoesNotContain("\"Release\":", raw);
        Assert.DoesNotContain("\"ReceivedAt\":", raw);

        var match = Regex.Match(raw, "\"receivedAt\":\"([^\"]+)\"");
        Assert.True(match.Success, $"receivedAt not found in raw body: {raw}");
        var receivedAtRaw = match.Groups[1].Value;

        Assert.EndsWith("Z", receivedAtRaw);

        var receivedAt = DateTime.Parse(
            receivedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        Assert.InRange(receivedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public async Task ReceivedAt_InStore_ParsesAsUtcIso8601AndIsCloseToNow()
    {
        var before = DateTime.UtcNow;
        var response = await _client.PostFeedbackAsync(FeedbackTestHelpers.ValidJson("1.0", 5), "user-2");
        var after = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var connection = _factory.OpenDirectConnection();
        var row = FeedbackDbReader.ReadAllFeedback(connection).Single();

        // ISO-8601 shape check.
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$", row.ReceivedAt);

        var parsed = FeedbackTimeFormat.Parse(row.ReceivedAt);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        Assert.InRange(parsed, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
