namespace FeedbackService.Feedback;

/// <summary>The exact error code strings pinned by the story's endpoint contract (400/429).</summary>
public static class FeedbackErrorCodes
{
    public const string ReleaseRequired = "release_required";
    public const string ReleaseInvalid = "release_invalid";
    public const string RatingRequired = "rating_required";
    public const string RatingOutOfRange = "rating_out_of_range";
    public const string CommentTooLong = "comment_too_long";
    public const string SubmissionLimitReached = "submission_limit_reached";

    // --- Extensions beyond the story's six pinned codes ---
    // The story pins the error envelope shape ({ "error", "message" }) for 400/401/429 but only
    // defines codes for 400/429 validation failures. These two codes cover response paths the
    // story requires (envelope on 401) or that are a defensive necessity (503 under write
    // contention) but for which it defines no vocabulary. They are deliberately NOT part of the
    // "exactly these six" set and must never be returned for a 400/429 validation failure.

    /// <summary>401: no valid session. Not one of the six pinned codes.</summary>
    public const string AuthenticationRequired = "authentication_required";

    /// <summary>503: the SQLite write lock could not be acquired within the busy timeout. Not one
    /// of the six pinned codes.</summary>
    public const string StorageBusy = "storage_busy";
}
