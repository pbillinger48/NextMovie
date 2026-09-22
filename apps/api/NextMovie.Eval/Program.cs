using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NextMovie.Api.Infrastructure.Startup;
using NextMovie.Eval;

// Offline evaluation of the recommendation engine (ADR-0012).
//
// Runs the real engine against a real library with some of its best films
// hidden, and reports whether they came back. Not a test: it needs a populated
// database and a live TMDb, and a test that needs those fails in CI for reasons
// that have nothing to do with the code.

if (!EvaluationOptions.TryParse(args, out var options, out var error))
{
    Console.Error.WriteLine(error);

    return 1;
}

// Content root is the binary's own directory, not the working directory. A CLI
// is run from wherever the user happens to be standing, and settings resolved
// against that would be found on one invocation and silently missing on the next.
//
// Args are deliberately not passed to the host: this tool parses its own, and the
// default configuration provider would read `--email parker@example.com` as a
// configuration key.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
});

// By name rather than by environment: this is a local tool run by hand, and
// requiring DOTNET_ENVIRONMENT to be set before it can find the database would be
// a footgun with no upside.
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"),
    optional: true,
    reloadOnChange: false);

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("NextMovieDb")))
{
    // Explicitly, rather than letting Npgsql throw sixty lines deep into a query.
    // The fix is a one-line file, and saying so is more use than a stack trace.
    Console.Error.WriteLine(
        "No connection string. Expected ConnectionStrings:NextMovieDb in "
        + "apps/api/NextMovie.Api/appsettings.Development.json, which is copied here at build time.");

    return 1;
}

// The API's own secrets store, by shared UserSecretsId. The evaluator needs
// exactly the credentials the API has, and a second copy is how one goes stale.
//
// Keyed off a named type rather than `typeof(Program)`: both this project and the
// API generate an implicit Program class, and naming it here picks the wrong one.
builder.Configuration.AddUserSecrets(typeof(EvaluationOptions).Assembly, optional: true);

builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = null;
});

// Warnings and above from the framework. A run makes dozens of HTTP calls and
// hundreds of EF commands, and the interesting output is the report at the end —
// one parameterised INSERT of 36 rows is 4,000 characters of noise around it.
//
// The categories are spelled out because this process inherits the API's
// appsettings, which turns EF command logging up to Information. Log filtering
// picks the longest matching category, so a filter on "Microsoft" loses to
// "Microsoft.EntityFrameworkCore.Database.Command" from configuration — it has to
// be matched exactly to be overridden.
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);

builder.Services.AddRecommendationEngine(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<Evaluator>();
builder.Services.AddScoped<PersonalisationCheck>();

using var host = builder.Build();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // The run holds an open transaction. Cancelling cooperatively lets it roll
    // back rather than leaving the rollback to connection teardown.
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

await using var scope = host.Services.CreateAsyncScope();

if (options.Mode == EvaluationMode.Personalisation)
{
    var check = scope.ServiceProvider.GetRequiredService<PersonalisationCheck>();
    var comparison = await check.RunAsync(options, cancellation.Token);

    if (comparison is null)
    {
        return 1;
    }

    Console.WriteLine(Report.Render(options, comparison));

    return 0;
}

var evaluator = scope.ServiceProvider.GetRequiredService<Evaluator>();
var report = await evaluator.RunAsync(options, cancellation.Token);

if (report is null)
{
    return 1;
}

Console.WriteLine(Report.Render(options, report));

return 0;
