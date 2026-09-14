using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Import;

/// <summary>
/// Applies a person's decision about an import row.
/// </summary>
/// <remarks>
/// Choosing a film here does exactly what a confident match would have done
/// during the import — the same rating, the same viewing, through the same
/// <see cref="UserLibrary"/> — and records <see cref="MatchMethod.Manual"/> so the
/// row's history says a person decided rather than an algorithm.
/// </remarks>
public static class ResolveImportItem
{
    /// <summary>Registers the reconcile endpoints.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/import/items/{itemId:guid}/resolve", ResolveAsync)
            .RequireAuthorization()
            .WithName(nameof(ResolveImportItem))
            .WithSummary("Choose the film an import row meant")
            .WithDescription(
                "Applies the row's rating and viewing to the chosen film, as a confident "
                + "match would have done during the import.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/v1/import/items/{itemId:guid}/dismiss", DismissAsync)
            .RequireAuthorization()
            .WithName(nameof(DismissImportItem))
            .WithSummary("Dismiss an import row")
            .WithDescription(
                "Marks a row as deliberately not imported — a television series, or a "
                + "film the user does not want.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<ImportJobStatusResponse>, ValidationProblem, ProblemHttpResult>> ResolveAsync(
        Guid itemId,
        ResolveImportItemRequest request,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        UserLibrary library,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return ImportResults.NoLongerSignedIn();
        }

        var item = await FindReviewableAsync(db, itemId, userId, cancellationToken);

        if (item is null)
        {
            return ImportResults.ItemNotFound();
        }

        // Any film in the catalogue, not only the candidates offered. Somebody
        // who searched and found the right film should not be told their answer
        // is not on the list — that would be the tool arguing with the person it
        // asked.
        var movie = await db.Movies
            .FirstOrDefaultAsync(candidate => candidate.Id == request.MovieId, cancellationToken);

        if (movie is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.MovieId)] = ["No film with that identifier is in the catalogue."],
            });
        }

        var now = time.GetUtcNow();

        await library.ImportAsync(userId, movie.Id, item.Rating, item.WatchedOn, now, cancellationToken);

        var job = item.ImportJob;

        // The counts move together: a row leaves the review pile exactly as it
        // joins the matched one, so the totals still add up afterwards.
        DecrementReviewCount(job, item.Status);
        job.MatchedItems++;

        item.Status = ImportItemStatus.Matched;
        item.MatchedMovieId = movie.Id;
        item.MatchMethod = MatchMethod.Manual;
        item.ResolvedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ImportJobStatusResponse.From(job));
    }

    private static async Task<Results<Ok<ImportJobStatusResponse>, ValidationProblem, ProblemHttpResult>> DismissAsync(
        Guid itemId,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return ImportResults.NoLongerSignedIn();
        }

        var item = await FindReviewableAsync(db, itemId, userId, cancellationToken);

        if (item is null)
        {
            return ImportResults.ItemNotFound();
        }

        DecrementReviewCount(item.ImportJob, item.Status);

        // Dismissed, not deleted. The row stays as a record that the export
        // contained it and a person decided against it — which is the difference
        // between "we skipped 20 rows" and data quietly going missing.
        item.Status = ImportItemStatus.Dismissed;
        item.ResolvedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ImportJobStatusResponse.From(item.ImportJob));
    }

    /// <remarks>
    /// Joined through the job's owner, so another user's row is not findable
    /// here at all rather than found and then refused.
    /// </remarks>
    private static Task<ImportItem?> FindReviewableAsync(
        NextMovieDbContext db,
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken) =>
        db.ImportItems
            .Include(item => item.ImportJob)
            .FirstOrDefaultAsync(
                item => item.Id == itemId
                    && item.ImportJob.UserId == userId
                    && (item.Status == ImportItemStatus.Ambiguous
                        || item.Status == ImportItemStatus.Unresolved),
                cancellationToken);

    private static void DecrementReviewCount(ImportJob job, ImportItemStatus status)
    {
        if (status == ImportItemStatus.Ambiguous)
        {
            job.AmbiguousItems--;
        }
        else
        {
            job.UnresolvedItems--;
        }
    }
}

/// <summary>Marker for the dismiss endpoint's name.</summary>
internal static class DismissImportItem;

/// <summary>The film an import row meant.</summary>
/// <param name="MovieId">NextMovie identifier of the chosen film.</param>
public sealed record ResolveImportItemRequest(Guid MovieId);
