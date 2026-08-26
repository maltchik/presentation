namespace FeedbackService.Feedback;

/// <summary>Human-readable text paired with each pinned error code.</summary>
public static class FeedbackErrorMessages
{
    public static string For(string errorCode) => errorCode switch
    {
        FeedbackErrorCodes.ReleaseRequired => "A release identifier is required.",
        FeedbackErrorCodes.ReleaseInvalid => "The release identifier is not valid.",
        FeedbackErrorCodes.RatingRequired => "A rating is required.",
        FeedbackErrorCodes.RatingOutOfRange => "The rating must be an integer between 1 and 5.",
        // Also returned for a comment that isn't a JSON string at all (see FeedbackRequestParser).
        FeedbackErrorCodes.CommentTooLong => "The comment must be a string of 500 characters or fewer.",
        FeedbackErrorCodes.SubmissionLimitReached => "The submission limit for this release has been reached.",
        FeedbackErrorCodes.AuthenticationRequired => "Authentication is required to submit feedback.",
        FeedbackErrorCodes.StorageBusy => "The feedback store is temporarily busy. Please try again.",
        _ => "The request is invalid.",
    };
}
