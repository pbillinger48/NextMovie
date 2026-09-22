using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Features.Recommendations;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Infrastructure.Tmdb;

namespace NextMovie.Api.Infrastructure.Startup;

/// <summary>
/// Everything the recommendation engine needs to run.
/// </summary>
/// <remarks>
/// Extracted from <c>Program.cs</c> so the API and the offline evaluator
/// (ADR-0012) register it once between them. The evaluator runs the real engine
/// against the real database and the real TMDb — an evaluator wired up by hand
/// would drift the moment the engine gained a dependency, and would then be
/// measuring something subtly different from what ships.
/// <para>
/// Deliberately not "everything the API needs". Authentication, OpenAPI and
/// problem details have nothing to do with recommending films, and pulling them
/// in would make this the second definition of the whole application rather than
/// the single definition of one subsystem.
/// </para>
/// </remarks>
internal static class RecommendationServices
{
    /// <param name="services">The container.</param>
    /// <param name="configuration">Supplies the connection string and TMDb token.</param>
    /// <param name="validateOnStart">
    /// Whether a missing TMDb token should stop the process immediately. False for
    /// the build-time OpenAPI generator, which starts the host with no secrets and
    /// would otherwise make the schema unbuildable on a correctly configured
    /// machine.
    /// </param>
    public static IServiceCollection AddRecommendationEngine(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStart = true)
    {
        services.AddDbContext<NextMovieDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("NextMovieDb"))
            .UseSnakeCaseNamingConvention());

        var tmdb = services.AddOptions<TmdbOptions>()
            .Bind(configuration.GetSection(TmdbOptions.SectionName))
            .ValidateDataAnnotations();

        if (validateOnStart)
        {
            tmdb.ValidateOnStart();
        }

        services.AddHttpClient<ITmdbClient, TmdbClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;

                client.BaseAddress = new Uri(options.BaseUrl);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.ApiReadAccessToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            // Timeout, retry with exponential backoff and jitter, and a circuit
            // breaker. The breaker matters most: without it, a TMDb outage means
            // every request retries into an already-failing service and we amplify
            // their incident.
            .AddStandardResilienceHandler();

        services.AddScoped<MovieCatalog>();
        services.AddScoped<RecommendationEngine>();

        // Availability sits behind an interface because it is the piece most
        // likely to be replaced: TMDb's data is free and adequate, but it cannot
        // say when a film leaves a service and does not link to the film on it
        // (ADR-0010).
        services.AddScoped<IAvailabilityProvider, TmdbAvailabilityProvider>();
        services.AddScoped<AvailabilityCatalog>();

        return services;
    }
}
