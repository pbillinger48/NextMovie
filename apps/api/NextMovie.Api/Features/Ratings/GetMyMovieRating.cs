using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Ratings;

/// <summary>
/// Reports what the signed-in user thinks of one film.
/// </summary>
/// <remarks>
/// A film page needs this and cannot get it from the ratings list, which is
/// capped — a library of eight hundred films would push most ratings off the end
/// of it.
/// <para>
/// Deliberately separate from the film details endpoint rather than folded into
/// it. Details are readable by anyone; folding a per-user answer into them would
/// make an anonymous-friendly response depend on who is asking, which is how a
/// cache eventually serves one person's opinion to somebody else.
/// </para>
/// </remarks>
public static class GetMyMovieRating
{
    /// <summary>Registers the single-film rating endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/movies/{id:guid}/rating", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(GetMyMovieRating))
            .WithSummary("Get your rating of a film")
            .WithDescription("Returns the signed-in user's rating of a film, if they have rated it.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<MovieRating>, ProblemHttpResult>> HandleAsync(
        Guid id,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return RatingResults.NoLongerSignedIn();
        }

        var rating = await db.Ratings
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId && candidate.MovieId == id)
            .Select(candidate => new MovieRating(candidate.MovieId, candidate.Value, candidate.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        // 404 for "you have not rated this", which is a different statement from
        // a rating of zero — a distinction ADR-0006 keeps deliberately.
        return rating is null
            ? TypedResults.Problem(
                title: "Not rated",
                detail: "You have not rated that film.",
                statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(rating);
    }
}
