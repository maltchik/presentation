using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniOrchestrator.Tests.Feedback;

internal static class FeedbackTestHelpers
{
    /// <summary>Matches the default JSON conventions used by minimal APIs (camelCase, case-insensitive reads).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task<HttpResponseMessage> PostFeedbackAsync(this HttpClient client, string rawJson, string? subject)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/feedback")
        {
            Content = new StringContent(rawJson, Encoding.UTF8, "application/json"),
        };
        if (subject is not null)
            request.Headers.Add(TestAuthenticationHandler.SubjectHeaderName, subject);

        return client.SendAsync(request);
    }

    /// <summary>Builds a valid feedback request body. Pass <paramref name="comment"/> as null to omit it.</summary>
    public static string ValidJson(string release = "1.0", int rating = 5, string? comment = null)
    {
        var obj = new JsonObject
        {
            ["release"] = release,
            ["rating"] = rating,
        };
        if (comment is not null)
            obj["comment"] = comment;

        return obj.ToJsonString();
    }
}
