using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MiniOrchestrator.Tests.Feedback;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> for the feedback service that:
///  - points the service at a uniquely-named SQLite database file in a temp directory, so tests
///    never share state or depend on run order;
///  - replaces the production authentication scheme with <see cref="TestAuthenticationHandler"/>
///    so both authenticated and unauthenticated requests can be driven from tests.
/// Dispose deletes the temp database file (and any SQLite side files).
/// </summary>
public sealed class FeedbackServiceFactory : WebApplicationFactory<Program>
{
    private readonly string _rateLimitKey;
    private readonly IReadOnlyDictionary<string, string?>? _additionalConfiguration;

    public string DbPath { get; }

    public FeedbackServiceFactory(
        string? rateLimitKey = null, IReadOnlyDictionary<string, string?>? additionalConfiguration = null)
    {
        DbPath = Path.Combine(Path.GetTempPath(), "feedback-service-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        _rateLimitKey = rateLimitKey ?? "test-only-rate-limit-key-not-for-production";
        _additionalConfiguration = additionalConfiguration;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Feedback:DbPath"] = DbPath,
                ["Feedback:RateLimitKey"] = _rateLimitKey,
            };

            if (_additionalConfiguration is not null)
            {
                foreach (var (key, value) in _additionalConfiguration)
                    overrides[key] = value;
            }

            config.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
        });
    }

    /// <summary>Opens a direct connection to the underlying SQLite file for developer-owned
    /// integration checks (AC-1, AC-9, AC-10) that have no read endpoint to go through.</summary>
    public SqliteConnection OpenDirectConnection()
    {
        var connection = new SqliteConnection($"Data Source={DbPath};Cache=Private");
        connection.Open();
        return connection;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        // Microsoft.Data.Sqlite pools native connections by default, which keeps a file handle
        // open even after every SqliteConnection using this connection string has been disposed.
        // Clear the pool first so the temp database file can actually be deleted. This clears
        // ALL pooled connections process-wide, not just this factory's - safe here because it
        // only drops idle pooled connections (any connection actively in use by another xunit
        // collection running in parallel is unaffected; it just loses the chance to reuse a
        // pooled native handle next time it opens one), and each factory uses its own uniquely
        // named temp file, so there's no cross-test data risk either way.
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = DbPath + suffix;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; leftover temp files don't affect other tests.
            }
        }
    }
}
