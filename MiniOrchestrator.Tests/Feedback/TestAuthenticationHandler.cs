using System.Security.Claims;
using System.Text.Encodings.Web;
using FeedbackService.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// Test-only authentication handler registered by <see cref="FeedbackServiceFactory"/>, replacing
/// the production <c>PlaceholderSessionAuthenticationHandler</c> entirely. Reads the caller's
/// "session subject" from a dedicated test header (deliberately distinct from any production
/// convention), so tests can drive both authenticated and unauthenticated requests and vary the
/// subject to exercise the per-caller submission cap (AC-8). Writes the same contract-pinned
/// error envelope on a 401 challenge as the production handler, via the shared writer, so tests
/// can assert against it.
/// </summary>
public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string SubjectHeaderName = "X-Test-Subject";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubjectHeaderName, out var subjectHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var subject = subjectHeader.ToString();
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
