namespace FeedbackService.Feedback;

/// <summary>Error body shape: { "error": "<code>", "message": "<human-readable text>" }.</summary>
public sealed record FeedbackErrorResponse(string Error, string Message);

/// <summary>Success body shape: { "id", "release", "receivedAt" }.</summary>
public sealed record FeedbackCreatedResponse(string Id, string Release, DateTime ReceivedAt);
