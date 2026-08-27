using System.Globalization;

namespace FeedbackService.Db;

/// <summary>
/// Single source of truth for how UTC timestamps are formatted when stored as SQLite TEXT.
/// The format is fixed-width and always UTC, so lexicographic string ordering matches
/// chronological ordering (used by the retention purge's WHERE received_at &lt; @cutoff).
/// </summary>
public static class FeedbackTimeFormat
{
    // Round-trip ("O") format: yyyy-MM-ddTHH:mm:ss.fffffffZ - fixed width, UTC, ISO-8601.
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public static string ToStorageString(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Unspecified)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        else if (utc.Kind == DateTimeKind.Local)
            utc = utc.ToUniversalTime();

        return utc.ToString(Format, CultureInfo.InvariantCulture);
    }

    public static DateTime Parse(string storageString) =>
        DateTime.ParseExact(storageString, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
