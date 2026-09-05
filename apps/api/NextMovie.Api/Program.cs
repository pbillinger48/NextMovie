using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Features.Health;
using NextMovie.Api.Infrastructure.Persistence;

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

// RFC 7807 ProblemDetails for every error response, matching the error format
// documented in docs/api.md.
builder.Services.AddProblemDetails();
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

app.Run();

/// <summary>
/// Exposed so integration tests can reference the entry point via
/// <c>WebApplicationFactory&lt;Program&gt;</c>; top-level statements otherwise
/// generate an internal <c>Program</c> class.
/// </summary>
public partial class Program;
