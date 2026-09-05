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
/// and on a CI runner with none. TMDb options are validated at startup, so the
/// host cannot build without a token even for endpoints that never call TMDb.
/// </remarks>
public sealed class NextMovieApiFactory : WebApplicationFactory<Program>
{
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

                // Syntactically valid and never connected to: the endpoints
                // exercised through this factory do not touch the database.
                // Database behaviour is tested against real PostgreSQL via
                // Testcontainers instead.
                ["ConnectionStrings:NextMovieDb"] =
                    "Host=127.0.0.1;Port=1;Database=nextmovie_unused;Username=test;Password=test",
            });
        });
    }
}
