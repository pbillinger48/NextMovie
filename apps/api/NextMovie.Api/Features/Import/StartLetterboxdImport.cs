using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Letterboxd;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Import;

/// <summary>
/// Accepts a Letterboxd export and queues it for matching.
/// </summary>
/// <remarks>
/// The file is parsed here, synchronously, and only then queued (ADR-0007). A
/// malformed CSV should fail while the user is still looking at the upload form
/// rather than two minutes later inside a job status — parsing is milliseconds,
/// and it is only the TMDb lookups that are slow.
/// <para>
/// The file itself is not stored. Once it is rows, it has no further use, and
/// keeping a copy of somebody's viewing history is a liability rather than an
/// asset.
/// </para>
/// </remarks>
public static class StartLetterboxdImport
{
    /// <summary>
    /// Largest export accepted.
    /// </summary>
    /// <remarks>
    /// A 3,000-film export is roughly a quarter of a megabyte, so this is two
    /// orders of magnitude of headroom — generous for a real user and still a
    /// bound on what an unauthenticated-sized mistake can cost us.
    /// </remarks>
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Most rows accepted from one export.
    /// </summary>
    /// <remarks>
    /// The largest plausible Letterboxd library is a few thousand films. This is
    /// a guard against a file that is technically CSV and practically a denial of
    /// service, not a judgement about anybody's viewing habits.
    /// </remarks>
    private const int MaxRows = 20_000;

    /// <summary>Registers the import upload endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/import/letterboxd", HandleAsync)
            .RequireAuthorization()

            // Minimal APIs demand antiforgery for form uploads by default, which
            // assumes a cookie-authenticated browser. This API only ever accepts
            // bearer tokens (ADR-0003) — the browser's cookie lives in the
            // Next.js tier and never reaches here (ADR-0004) — so there is no
            // ambient credential for a cross-site form to abuse.
            .DisableAntiforgery()
            .WithName(nameof(StartLetterboxdImport))
            .WithSummary("Import a Letterboxd export")
            .WithDescription(
                "Accepts a Letterboxd CSV export (watched, ratings or diary), parses it, "
                + "and queues it for matching against TMDb. Returns a job to poll.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Accepted<ImportJobStatusResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        IFormFile file,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        LetterboxdCsvReader reader,
        TimeProvider time,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return ImportResults.NoLongerSignedIn();
        }

        if (Validate(file) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await using var contents = file.OpenReadStream();
        var parsed = reader.Read(contents);

        if (parsed.Entries.Count == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(file)] =
                [
                    "That file contained no films. Export your data from Letterboxd and "
                    + "upload watched.csv, ratings.csv or diary.csv.",
                ],
            });
        }

        if (parsed.Entries.Count > MaxRows)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(file)] = [$"That file contains more than {MaxRows:N0} rows."],
            });
        }

        var now = time.GetUtcNow();

        var job = new ImportJob
        {
            UserId = userId,
            TotalItems = parsed.Entries.Count,
            SkippedRows = parsed.SkippedRows,
            CreatedAt = now,
        };

        foreach (var entry in parsed.Entries)
        {
            job.Items.Add(new ImportItem
            {
                ImportJobId = job.Id,
                Name = entry.Name,
                Year = entry.Year,
                FilmUri = entry.FilmUri,
                Rating = entry.Rating,
                WatchedOn = entry.WatchedOn,
            });
        }

        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Queued Letterboxd import {JobId} with {Items} rows ({Skipped} skipped)",
            job.Id,
            job.TotalItems,
            job.SkippedRows);

        // 202, not 201: the work has been accepted, not completed. The Location
        // header points at where the answer will appear.
        return TypedResults.Accepted(
            $"/api/v1/import/{job.Id}",
            ImportJobStatusResponse.From(job));
    }

    private static Dictionary<string, string[]> Validate(IFormFile file)
    {
        var errors = new Dictionary<string, string[]>();

        if (file.Length == 0)
        {
            errors[nameof(file)] = ["That file is empty."];
        }
        else if (file.Length > MaxUploadBytes)
        {
            errors[nameof(file)] = [$"That file is larger than {MaxUploadBytes / (1024 * 1024)} MB."];
        }
        else if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            // Checked on the name rather than the content type: browsers report
            // CSV as text/csv, application/vnd.ms-excel and octet-stream
            // depending on the operating system, so the header is the less
            // reliable signal of the two.
            errors[nameof(file)] = ["Upload a .csv file exported from Letterboxd."];
        }

        return errors;
    }
}
