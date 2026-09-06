using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace NextMovie.Api.Tests;

/// <summary>
/// Boots the API for tests that need a real host.
/// </summary>
/// <remarks>
/// Configuration is supplied here rather than inherited from the developer's
/// machine, so the suite behaves identically on a laptop with user-secrets set
/// and on a CI runner with none. TMDb and JWT options are both validated at
/// startup, so the host cannot build without them even for endpoints that use
/// neither.
/// </remarks>
public sealed class NextMovieApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Syntactically valid and never connected to. Endpoints exercised without a
    /// real database must not touch one — if a test using this connection hangs
    /// or throws on connect, the endpoint under test has a dependency the test
    /// did not intend.
    /// </summary>
    private const string UnusedConnectionString =
        "Host=127.0.0.1;Port=1;Database=nextmovie_unused;Username=test;Password=test";

    private readonly string _connectionString;

    /// <summary>Boots the API with no usable database.</summary>
    public NextMovieApiFactory()
        : this(UnusedConnectionString)
    {
    }

    /// <summary>Boots the API against a real database, for endpoints that need one.</summary>
    /// <param name="connectionString">Usually a Testcontainers PostgreSQL instance.</param>
    /// <remarks>
    /// Internal rather than public because xUnit refuses to use a type with more
    /// than one public constructor as a class fixture, and
    /// <c>GetHealthTests</c> takes this as one.
    /// </remarks>
    internal NextMovieApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Satisfies startup validation. No test using this factory makes
                // a real TMDb call — those are covered by mapper unit tests and
                // by a stubbed HTTP handler.
                ["Tmdb:ApiReadAccessToken"] = "test-token-never-sent",

                // A fixed key so tokens are reproducible within a run. Long
                // enough to satisfy the HS256 minimum, which startup validation
                // enforces.
                ["Jwt:SigningKey"] = "test-signing-key-long-enough-for-hmac-sha256",
                ["Jwt:Issuer"] = "nextmovie-api-tests",
                ["Jwt:Audience"] = "nextmovie-tests",

                ["ConnectionStrings:NextMovieDb"] = _connectionString,
            });
        });
    }
}
