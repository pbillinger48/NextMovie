using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Import;

/// <summary>
/// Reports how an import is going.
/// </summary>
/// <remarks>
/// The endpoint a client polls while the job runs. It reads one row, which is why
/// the counts live on the job rather than being aggregated from items on every
/// poll.
/// </remarks>
public static class GetImportStatus
{
    /// <summary>Registers the import status endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/import/{jobId:guid}", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(GetImportStatus))
            .WithSummary("Check an import")
            .WithDescription("Reports the status and progress of a Letterboxd import.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<ImportJobStatusResponse>, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return ImportResults.NoLongerSignedIn();
        }

        // Filtered by user in the query rather than checked afterwards: somebody
        // else's job is not findable here at all, so this cannot answer "that job
        // exists but is not yours" — which would be an oracle for how many
        // imports other people have run.
        var job = await db.ImportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == jobId && candidate.UserId == userId,
                cancellationToken);

        return job is null
            ? ImportResults.JobNotFound()
            : TypedResults.Ok(ImportJobStatusResponse.From(job));
    }
}

/// <summary>Responses shared by the import slices.</summary>
internal static class ImportResults
{
    public static ProblemHttpResult NoLongerSignedIn() => TypedResults.Problem(
        title: "Not signed in",
        detail: "This session is no longer valid. Sign in again.",
        statusCode: StatusCodes.Status401Unauthorized);

    public static ProblemHttpResult JobNotFound() => TypedResults.Problem(
        title: "Import not found",
        detail: "No import with that identifier belongs to you.",
        statusCode: StatusCodes.Status404NotFound);
}

/// <summary>How an import is going.</summary>
/// <param name="Id">The job, to poll.</param>
/// <param name="Status">Pending, Running, Completed or Failed.</param>
/// <param name="TotalItems">Rows parsed out of the export.</param>
/// <param name="MatchedItems">Rows resolved to a film and applied.</param>
/// <param name="AmbiguousItems">Rows needing a person to choose between candidates.</param>
/// <param name="UnresolvedItems">Rows nothing plausible was found for.</param>
/// <param name="SkippedRows">Rows in the file that carried no title and could not be matched.</param>
/// <param name="FailureReason">Why the import failed, when it did.</param>
/// <param name="CreatedAt">When the export was uploaded.</param>
/// <param name="CompletedAt">When the import finished, if it has.</param>
public sealed record ImportJobStatusResponse(
    Guid Id,
    string Status,
    int TotalItems,
    int MatchedItems,
    int AmbiguousItems,
    int UnresolvedItems,
    int SkippedRows,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    internal static ImportJobStatusResponse From(ImportJob job) => new(
        Id: job.Id,
        Status: job.Status.ToString(),
        TotalItems: job.TotalItems,
        MatchedItems: job.MatchedItems,
        AmbiguousItems: job.AmbiguousItems,
        UnresolvedItems: job.UnresolvedItems,
        SkippedRows: job.SkippedRows,
        FailureReason: job.FailureReason,
        CreatedAt: job.CreatedAt,
        CompletedAt: job.CompletedAt);
}
