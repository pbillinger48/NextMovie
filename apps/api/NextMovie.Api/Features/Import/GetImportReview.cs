using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Import;

/// <summary>
/// Lists the rows of an import that need a person.
/// </summary>
/// <remarks>
/// The other half of the matcher's refusal to guess. It declines roughly 7 rows
/// per 800 films, which the spike sized as "a modest screen, not a major
/// feature" — and this is the endpoint behind that screen.
/// <para>
/// Candidate films were written into the catalogue when the import ran, so this
/// is a local join rather than a burst of TMDb lookups at exactly the moment
/// somebody is waiting for a page.
/// </para>
/// </remarks>
public static class GetImportReview
{
    /// <summary>Registers the reconciliation endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/import/{jobId:guid}/review", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(GetImportReview))
            .WithSummary("List import rows needing review")
            .WithDescription(
                "Returns the rows of an import that could not be resolved confidently, "
                + "with the films they might mean.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<ImportReviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return ImportResults.NoLongerSignedIn();
        }

        var owned = await db.ImportJobs.AnyAsync(
            job => job.Id == jobId && job.UserId == userId,
            cancellationToken);

        if (!owned)
        {
            return ImportResults.JobNotFound();
        }

        var items = await db.ImportItems
            .AsNoTracking()
            .Where(item => item.ImportJobId == jobId
                && (item.Status == ImportItemStatus.Ambiguous || item.Status == ImportItemStatus.Unresolved))
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

        // One query for every candidate across every row, rather than one per
        // row. Seven rows of four candidates is not a performance problem today;
        // an N+1 that ships is one tomorrow.
        var tmdbIds = items.SelectMany(item => item.CandidateTmdbIds).Distinct().ToArray();

        var films = await db.Movies
            .AsNoTracking()
            .Where(movie => tmdbIds.Contains(movie.TmdbId))
            .Select(movie => new ImportCandidate(
                movie.Id,
                movie.TmdbId,
                movie.Title,
                movie.ReleaseDate,
                movie.PosterPath,
                movie.AverageRating))
            .ToDictionaryAsync(candidate => candidate.TmdbId, cancellationToken);

        var review = items
            .Select(item => new ImportReviewItem(
                item.Id,
                item.Name,
                item.Year,
                item.Rating,
                item.WatchedOn,
                item.Status.ToString(),
                [.. item.CandidateTmdbIds
                    .Select(films.GetValueOrDefault)
                    .OfType<ImportCandidate>()]))
            .ToList();

        return TypedResults.Ok(new ImportReviewResponse(review));
    }
}

/// <summary>Import rows awaiting a decision.</summary>
/// <param name="Items">Rows needing review, by title.</param>
public sealed record ImportReviewResponse(IReadOnlyList<ImportReviewItem> Items);

/// <summary>One row a person has to decide about.</summary>
/// <param name="ItemId">The row, to resolve or dismiss.</param>
/// <param name="Name">Title exactly as the export gave it.</param>
/// <param name="Year">Year the export gave, when it gave one.</param>
/// <param name="Rating">The rating waiting to be applied, if the export carried one.</param>
/// <param name="WatchedOn">The viewing date from the export, when it had one.</param>
/// <param name="Status">
/// <c>Ambiguous</c> when several films were plausible, <c>Unresolved</c> when
/// none were — which often means the row is television rather than a film.
/// </param>
/// <param name="Candidates">The films this row might mean. Empty when nothing was found.</param>
public sealed record ImportReviewItem(
    Guid ItemId,
    string Name,
    int? Year,
    decimal? Rating,
    DateOnly? WatchedOn,
    string Status,
    IReadOnlyList<ImportCandidate> Candidates);

/// <summary>A film an ambiguous row might mean.</summary>
/// <param name="MovieId">NextMovie identifier, to send back when choosing.</param>
/// <param name="TmdbId">TMDb identifier.</param>
/// <param name="Title">Display title.</param>
/// <param name="ReleaseDate">Release date, when known.</param>
/// <param name="PosterPath">Relative TMDb poster path.</param>
/// <param name="AverageRating">TMDb community rating, which is usually what distinguishes these.</param>
public sealed record ImportCandidate(
    Guid MovieId,
    int TmdbId,
    string Title,
    DateOnly? ReleaseDate,
    string? PosterPath,
    double? AverageRating);
