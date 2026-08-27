using System.Security.Claims;
using System.Text.Encodings.Web;
using FeedbackService.Feedback;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FeedbackService.Auth;

/// <summary>
/// Names the authentication scheme used as a stand-in for "the web app's existing authenticated
/// session" (the story is greenfield and has no real auth to integrate with yet).
/// </summary>
public static class PlaceholderSessionAuthentication
{
    public const string SchemeName = "PlaceholderSession";
}

/// <summary>
/// Writes the pinned { "error", "message" } envelope for a 401 challenge. The story's contract
/// says 400/401/429 all share this envelope shape, but pins error codes only for 400/429 -
/// <see cref="FeedbackErrorCodes.AuthenticationRequired"/> is an explicit extension, not one of
/// the six pinned codes. Shared by the production placeholder handler and the test project's
/// authentication handler so both exhibit - and both can be asserted against - the same
/// contract-compliant 401 body.
/// </summary>
public static class AuthenticationErrorResponseWriter
{
    public static Task WriteChallengeAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var body = new FeedbackErrorResponse(
            FeedbackErrorCodes.AuthenticationRequired, FeedbackErrorMessages.For(FeedbackErrorCodes.AuthenticationRequired));
        return context.Response.WriteAsJsonAsync(body);
    }
}

/// <summary>
/// PLACEHOLDER authentication handler.
///
/// This stands in for whatever session mechanism the real web app already uses (cookie, JWT
/// bearer validated against an identity provider, etc). It intentionally does the minimum needed
/// to exercise the correct ASP.NET Core authentication/authorization pipeline
/// (AddAuthentication/AddAuthorization/.RequireAuthorization()) so that an unauthenticated
/// request is rejected with 401 before the request body is even read (AC-7).
///
/// It accepts `Authorization: Bearer &lt;subject&gt;` and trusts the token verbatim as the
/// session subject, with no signature/identity-provider validation. Replace this handler with a
/// real scheme (e.g. AddJwtBearer or AddCookie against the app's actual session) when this
/// service is wired into the real web app; nothing else in the pipeline needs to change.
///
/// Tests replace this scheme entirely with a test authentication handler via
/// <c>WebApplicationFactory</c> so both authenticated and unauthenticated cases can be driven,
/// and the subject varied per test.
/// </summary>
public sealed class PlaceholderSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public PlaceholderSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var value = authorizationHeader.ToString();
        const string prefix = "Bearer ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var subject = value[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(subject))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, subject) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        AuthenticationErrorResponseWriter.WriteChallengeAsync(Context);
}
