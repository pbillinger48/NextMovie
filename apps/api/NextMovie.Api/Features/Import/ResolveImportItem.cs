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

        // Every pending row for the same film, across every import this user has
        // run — not just the one they clicked. Importing watched.csv and then
        // ratings.csv queues the same unmatched film twice, and being asked the
        // same question again because of how the export was split is the tool's
        // problem, not the user's.
        foreach (var sibling in await FindSameFilmAsync(db, item, userId, cancellationToken))
        {
            // Each row carries its own rating and date: the watched.csv row has
            // no rating and the ratings.csv row does, and both matter.
            await library.ImportAsync(
                userId,
                movie.Id,
                sibling.Rating,
                sibling.WatchedOn,
                sibling.IsLoggedViewing,
                now,
                cancellationToken);

            DecrementReviewCount(sibling.ImportJob, sibling.Status);
            sibling.ImportJob.MatchedItems++;

            sibling.Status = ImportItemStatus.Matched;
            sibling.MatchedMovieId = movie.Id;
            sibling.MatchMethod = MatchMethod.Manual;
            sibling.ResolvedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ImportJobStatusResponse.From(item.ImportJob));
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

        var now = time.GetUtcNow();

        foreach (var sibling in await FindSameFilmAsync(db, item, userId, cancellationToken))
        {
            DecrementReviewCount(sibling.ImportJob, sibling.Status);

            // Dismissed, not deleted. The row stays as a record that the export
            // contained it and a person decided against it — which is the
            // difference between "we skipped 20 rows" and data quietly going
            // missing.
            sibling.Status = ImportItemStatus.Dismissed;
            sibling.ResolvedAt = now;
        }

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

    /// <summary>
    /// Every row still awaiting review that means the same film as this one,
    /// including the row itself.
    /// </summary>
    /// <remarks>
    /// Matched on Letterboxd's film URI where the export gave one — the spike
    /// found it stable across exports, which is exactly what makes it the right
    /// key for this. Where it is absent, title and year are the best available
    /// substitute.
    /// </remarks>
    private static async Task<List<ImportItem>> FindSameFilmAsync(
        NextMovieDbContext db,
        ImportItem item,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var pending = db.ImportItems
            .Include(candidate => candidate.ImportJob)
            .Where(candidate => candidate.ImportJob.UserId == userId
                && (candidate.Status == ImportItemStatus.Ambiguous
                    || candidate.Status == ImportItemStatus.Unresolved));

        return item.FilmUri is { Length: > 0 } filmUri
            ? await pending.Where(candidate => candidate.FilmUri == filmUri).ToListAsync(cancellationToken)
            : await pending
                .Where(candidate => candidate.Name == item.Name && candidate.Year == item.Year)
                .ToListAsync(cancellationToken);
    }

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
