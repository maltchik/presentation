using System.Text.Json;
using System.Text.RegularExpressions;

namespace FeedbackService.Feedback;

/// <summary>
/// Parses and validates a raw JSON request body for POST /api/feedback.
///
/// Parses directly against <see cref="JsonElement"/> rather than deserialising into a typed
/// DTO so that the rating field's JSON representation (absent, null, integer, fractional number,
/// string, boolean) can be distinguished precisely (AC-2, AC-3, AC-6 of the story's decisions).
///
/// Validation order matches the story's pinned precedence: release, then rating, then comment.
/// Malformed/unparseable JSON is treated as "release_required" (decision 3).
/// </summary>
public static class FeedbackRequestParser
{
    // \A...\z (not ^...$): in .NET, $ matches at end-of-string OR immediately before a trailing
    // '\n', which would let "1.0\n" through (and let a trailing newline push a 20-char release to
    // 21 code units while still matching). \A/\z anchor to the true start/end only.
    private static readonly Regex ReleasePattern =
        new(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,19}\z", RegexOptions.Compiled);

    private const int MaxCommentLength = 500;

    // JSON number grammar only ever uses '.', 'e' or 'E' to express a non-integer literal
    // (fraction or exponent); no other characters can appear in a JSON number's raw text.
    private static readonly char[] NonIntegerLiteralMarkers = { '.', 'e', 'E' };

    public static FeedbackValidationResult Parse(string rawBody)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return FeedbackValidationResult.Failure(FeedbackErrorCodes.ReleaseRequired);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return FeedbackValidationResult.Failure(FeedbackErrorCodes.ReleaseRequired);

            var releaseError = ValidateRelease(root, out var release);
            if (releaseError is not null)
                return FeedbackValidationResult.Failure(releaseError);

            var ratingError = ValidateRating(root, out var rating);
            if (ratingError is not null)
                return FeedbackValidationResult.Failure(ratingError);

            var commentError = ValidateComment(root, out var comment);
            if (commentError is not null)
                return FeedbackValidationResult.Failure(commentError);

            return FeedbackValidationResult.Success(release!, rating, comment);
        }
    }

    private static string? ValidateRelease(JsonElement root, out string? release)
    {
        release = null;

        if (!root.TryGetProperty("release", out var element) || element.ValueKind == JsonValueKind.Null)
            return FeedbackErrorCodes.ReleaseRequired;

        if (element.ValueKind != JsonValueKind.String)
            return FeedbackErrorCodes.ReleaseInvalid;

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return FeedbackErrorCodes.ReleaseRequired;

        if (!ReleasePattern.IsMatch(value))
            return FeedbackErrorCodes.ReleaseInvalid;

        release = value; // stored verbatim - no trimming, no case normalisation (AC-5)
        return null;
    }

    private static string? ValidateRating(JsonElement root, out int rating)
    {
        rating = 0;

        if (!root.TryGetProperty("rating", out var element) || element.ValueKind == JsonValueKind.Null)
            return FeedbackErrorCodes.RatingRequired;

        // Only a genuinely absent/null rating is "required"; every other non-conforming
        // JSON shape (string, bool, array, object, fractional number, out-of-range number)
        // is "out_of_range" (decision 6).
        if (element.ValueKind != JsonValueKind.Number)
            return FeedbackErrorCodes.RatingOutOfRange;

        // Integrality must be decided from the JSON literal itself, not from a lossy numeric
        // round-trip: decimal/double round-tripping loses precision past ~28-29 significant
        // digits, which would let a value like 3.0000000000000000000000000000000001 - or any
        // literal with a decimal point or exponent, e.g. 4.0 or 4e0 - be silently coerced to an
        // "integer" 3/4. Per decision 6, any literal that isn't written as a bare integer is
        // rejected as out_of_range, regardless of the numeric value it represents.
        var raw = element.GetRawText();
        if (raw.IndexOfAny(NonIntegerLiteralMarkers) >= 0)
            return FeedbackErrorCodes.RatingOutOfRange;

        if (!element.TryGetInt32(out var value) || value < 1 || value > 5)
            return FeedbackErrorCodes.RatingOutOfRange;

        rating = value;
        return null;
    }

    private static string? ValidateComment(JsonElement root, out string? comment)
    {
        comment = null;

        if (!root.TryGetProperty("comment", out var element) || element.ValueKind == JsonValueKind.Null)
            return null; // omitted or explicit null -> NULL, always valid

        // The story pins no dedicated "comment has the wrong JSON type" error code. Rather than
        // silently stringifying a number/bool/array/object (which would corrupt stored data -
        // {"a":1} becoming the literal text "{\"a\":1}"), reject it under comment_too_long, the
        // only comment-specific code among the six pinned ones. This mirrors how a non-string,
        // non-null `release` is rejected under release_invalid (its only release-specific code).
        if (element.ValueKind != JsonValueKind.String)
            return FeedbackErrorCodes.CommentTooLong;

        var text = element.GetString();

        if (string.IsNullOrWhiteSpace(text))
            return null; // empty/whitespace-only -> NULL (decision 1)

        if (text.Length > MaxCommentLength)
            return FeedbackErrorCodes.CommentTooLong;

        comment = text; // no truncation, ever
        return null;
    }
}
