namespace FeedbackService.Feedback;

/// <summary>
/// Outcome of validating/parsing an incoming feedback submission. Either <see cref="ErrorCode"/>
/// is set (validation failed) or the three value properties are populated (validation passed).
/// </summary>
public sealed class FeedbackValidationResult
{
    public string? ErrorCode { get; private init; }
    public string Release { get; private init; } = string.Empty;
    public int Rating { get; private init; }
    public string? Comment { get; private init; }

    public bool IsValid => ErrorCode is null;

    public static FeedbackValidationResult Failure(string errorCode) => new() { ErrorCode = errorCode };

    public static FeedbackValidationResult Success(string release, int rating, string? comment) =>
        new() { Release = release, Rating = rating, Comment = comment };
}
