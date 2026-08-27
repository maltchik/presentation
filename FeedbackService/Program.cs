using System.Security.Claims;
using FeedbackService;
using FeedbackService.Auth;
using FeedbackService.Db;
using FeedbackService.Feedback;
using FeedbackService.Retention;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// Resolved lazily (first time something asks the container for FeedbackOptions) rather than
// bound eagerly here, because WebApplicationFactory (used by the test suite) only merges its
// configuration overrides - e.g. a per-test SQLite DbPath - into builder.Configuration as part
// of the builder.Build() call below. Binding eagerly at this point would read stale/default
// configuration in tests. It IS still resolved eagerly at startup (see the InitializeAsync call
// below), so a validation failure here still surfaces immediately rather than on first request.
builder.Services.AddSingleton(sp =>
{
    var feedbackOptions = new FeedbackOptions();
    sp.GetRequiredService<IConfiguration>().GetSection(FeedbackOptions.SectionName).Bind(feedbackOptions);
    if (string.IsNullOrWhiteSpace(feedbackOptions.DbPath))
        feedbackOptions.DbPath = Path.Combine(builder.Environment.ContentRootPath, "feedback.db");

    // Fail fast on nonsensical configuration rather than silently misbehaving at runtime: a cap
    // of 0 (or negative) would turn every submission into a 429, and a non-positive retention
    // window would purge the entire store the moment the purge first runs.
    if (feedbackOptions.SubmissionLimitPerRelease < 1)
        throw new InvalidOperationException(
            $"{FeedbackOptions.SectionName}:{nameof(FeedbackOptions.SubmissionLimitPerRelease)} must be at least 1.");
    if (feedbackOptions.RetentionMonths < 1)
        throw new InvalidOperationException(
            $"{FeedbackOptions.SectionName}:{nameof(FeedbackOptions.RetentionMonths)} must be at least 1.");
    if (feedbackOptions.BusyTimeoutMilliseconds < 0)
        throw new InvalidOperationException(
            $"{FeedbackOptions.SectionName}:{nameof(FeedbackOptions.BusyTimeoutMilliseconds)} must not be negative.");

    return feedbackOptions;
});
builder.Services.AddSingleton<FeedbackDatabase>();
builder.Services.AddSingleton<RateLimitKeyProvider>();
builder.Services.AddSingleton<FeedbackRepository>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<RetentionPurgeHostedService>();

// See FeedbackService.Auth.PlaceholderSessionAuthenticationHandler for why this scheme exists
// and how it is meant to be replaced by the web app's real session/auth mechanism.
builder.Services
    .AddAuthentication(PlaceholderSessionAuthentication.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, PlaceholderSessionAuthenticationHandler>(
        PlaceholderSessionAuthentication.SchemeName, _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.GetRequiredService<FeedbackDatabase>().InitializeAsync();

// Serves the KAN-1 widget assets (feedback-widget.js/css, demo.html) from wwwroot. Placed before
// auth middleware: these are static files, not the /api/feedback endpoint, and must be reachable
// without a session.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/feedback", async (
        HttpContext context,
        FeedbackRepository repository,
        RateLimitKeyProvider rateLimitKeyProvider,
        CancellationToken cancellationToken) =>
    {
        string rawBody;
        using (var reader = new StreamReader(context.Request.Body))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        var parseResult = FeedbackRequestParser.Parse(rawBody);
        if (!parseResult.IsValid)
        {
            return Results.Json(
                new FeedbackErrorResponse(parseResult.ErrorCode!, FeedbackErrorMessages.For(parseResult.ErrorCode!)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Guaranteed present: the endpoint requires authorization, so a claims principal with a
        // subject claim always exists by the time this handler runs.
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Authenticated request is missing a subject claim.");

        var hmacKey = rateLimitKeyProvider.ComputeKey(subject, parseResult.Release);

        FeedbackCreatedResponse? created;
        try
        {
            created = await repository.TryInsertAsync(
                parseResult.Release, parseResult.Rating, parseResult.Comment, hmacKey, cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) // SQLITE_BUSY / SQLITE_LOCKED
        {
            // The write lock could not be acquired within busy_timeout (see FeedbackDatabase).
            // Surface this as a well-formed 503 rather than letting it escape as an unhandled 500.
            return Results.Json(
                new FeedbackErrorResponse(FeedbackErrorCodes.StorageBusy, FeedbackErrorMessages.For(FeedbackErrorCodes.StorageBusy)),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (created is null)
        {
            return Results.Json(
                new FeedbackErrorResponse(
                    FeedbackErrorCodes.SubmissionLimitReached,
                    FeedbackErrorMessages.For(FeedbackErrorCodes.SubmissionLimitReached)),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return Results.Json(created, statusCode: StatusCodes.Status201Created);
    })
    .RequireAuthorization();

app.Run();

// Exposes Program to MiniOrchestrator.Tests via WebApplicationFactory<Program>.
public partial class Program { }
