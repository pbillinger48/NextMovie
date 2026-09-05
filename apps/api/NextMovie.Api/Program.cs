using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NextMovie.Api.Features.Health;
using NextMovie.Api.Features.Movies;
using NextMovie.Api.Infrastructure.ErrorHandling;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Infrastructure.Tmdb;

var builder = WebApplication.CreateBuilder(args);

// Structured logging without an extra dependency. Serilog stays deferred until
// there is a real sink (Application Insights, Seq) that justifies it.
// Human-readable locally; machine-parseable JSON everywhere else.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}
else
{
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
}

// PostgreSQL via EF Core. Snake-case naming keeps identifiers in PostgreSQL's
// native style (movies.tmdb_id), so hand-written SQL and psql sessions never
// need to quote them. Migrations are never applied automatically — see the
// README; running DDL from application startup races across instances on deploy.
builder.Services.AddDbContext<NextMovieDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("NextMovieDb"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<MovieCatalog>();

// Bound and validated at startup rather than on first use: a missing TMDb token
// should stop the process immediately with a clear message, not surface as a
// confusing 401 the first time somebody searches.
builder.Services
    .AddOptions<TmdbOptions>()
    .Bind(builder.Configuration.GetSection(TmdbOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<ITmdbClient, TmdbClient>((serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;

        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.ApiReadAccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    })
    // Timeout, retry with exponential backoff and jitter, and a circuit breaker.
    // The breaker matters most: without it, a TMDb outage means every request
    // retries into an already-failing service and we amplify their incident.
    .AddStandardResilienceHandler();

// RFC 7807 ProblemDetails for every error response, matching the error format
// documented in docs/api.md.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TmdbExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Turns unhandled exceptions into ProblemDetails rather than leaking stack
// traces, and gives otherwise-bodyless 4xx/5xx responses a ProblemDetails body.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Feature slices register themselves explicitly. Reflection-based endpoint
// discovery would be shorter, but this stays greppable and has no startup magic.
GetHealth.Map(app);
SearchMovies.Map(app);

app.Run();

/// <summary>
/// Exposed so integration tests can reference the entry point via
/// <c>WebApplicationFactory&lt;Program&gt;</c>; top-level statements otherwise
/// generate an internal <c>Program</c> class.
/// </summary>
public partial class Program;
