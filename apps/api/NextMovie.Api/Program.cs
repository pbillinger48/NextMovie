using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NextMovie.Api.Domain;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Health;
using NextMovie.Api.Features.Movies;
using NextMovie.Api.Features.Users;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.ErrorHandling;
using NextMovie.Api.Infrastructure.OpenApi;
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
// or signing key should stop the process immediately with a clear message, not
// surface as a confusing 401 the first time somebody searches or signs in.
var tmdbOptions = builder.Services
    .AddOptions<TmdbOptions>()
    .Bind(builder.Configuration.GetSection(TmdbOptions.SectionName))
    .ValidateDataAnnotations();

var jwtOptions = builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations();

// ...with one exception. The build-time OpenAPI generator (GetDocument.Insider,
// see OpenApiGenerateDocumentsOnBuild in the csproj) starts the host in-process
// to read endpoint metadata. It has no secrets and needs none, so startup
// validation would fail `dotnet build` on a correctly configured machine and
// make the schema unbuildable in CI. Only that context is exempt; every run
// that actually serves traffic still validates.
if (Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
{
    tmdbOptions.ValidateOnStart();
    jwtOptions.ValidateOnStart();
}

// Injected rather than called statically so tests can move time forward without
// waiting out a real lockout window.
builder.Services.AddSingleton(TimeProvider.System);

// Identity's PBKDF2 hasher, on its own. Registering the interface rather than
// the concrete type keeps the option of swapping the algorithm open, which is
// the whole reason ADR-0003 requires a versioned hash format.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
builder.Services.AddSingleton<RefreshTokenFactory>();
builder.Services.AddScoped<SessionIssuer>();
builder.Services.AddScoped<SessionRevoker>();

// The API accepts bearer tokens and nothing else (ADR-0003). The browser's
// session is a cookie held by the Next.js tier (ADR-0004), which the API is
// deliberately never taught about.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.ConfigureOptions<ConfigureJwtBearerOptions>();
builder.Services.AddAuthorization();

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
builder.Services.AddOpenApi(options =>
    options.AddSchemaTransformer<NumericTypeSchemaTransformer>());

var app = builder.Build();

// Turns unhandled exceptions into ProblemDetails rather than leaking stack
// traces, and gives otherwise-bodyless 4xx/5xx responses a ProblemDetails body.
app.UseExceptionHandler();
app.UseStatusCodePages();

// Bearer tokens are validated by the same configuration that issues them, so
// the two cannot drift apart. /api/v1/users/me is the first endpoint behind it.
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Feature slices register themselves explicitly. Reflection-based endpoint
// discovery would be shorter, but this stays greppable and has no startup magic.
GetHealth.Map(app);
SearchMovies.Map(app);
RegisterUser.Map(app);
LoginUser.Map(app);
RefreshSession.Map(app);
LogoutUser.Map(app);
GetCurrentUser.Map(app);
UpdateCurrentUser.Map(app);

app.Run();

/// <summary>
/// Exposed so integration tests can reference the entry point via
/// <c>WebApplicationFactory&lt;Program&gt;</c>; top-level statements otherwise
/// generate an internal <c>Program</c> class.
/// </summary>
public partial class Program;
