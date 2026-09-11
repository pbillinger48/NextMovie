using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Ratings;

/// <summary>
/// Lists everything the signed-in user has rated.
/// </summary>
/// <remarks>
/// Carries enough of each film to render a list without a second call per row —
/// title, poster and year — which is the shape every client wants and the one
/// that avoids an N+1 across the network.
/// </remarks>
public static class GetMyRatings
{
    /// <summary>
    /// The most ratings returned in one page.
    /// </summary>
    /// <remarks>
    /// A Letterboxd import can produce thousands, and an unbounded list would
    /// eventually be a slow query returning a response nobody renders. Paging
    /// beyond this is a follow-up; the cap is here now so the endpoint cannot
    /// quietly become that.
    /// </remarks>
    private const int MaxResults = 200;

    /// <summary>Registers the ratings list endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/me/ratings", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(GetMyRatings))
            .WithSummary("List the signed-in user's ratings")
            .WithDescription("Returns rated films, most recently rated first.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<MyRatingsResponse>, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return RatingResults.NoLongerSignedIn();
        }

        var rated = await db.Ratings
            .AsNoTracking()
            .Where(rating => rating.UserId == userId)
            .OrderByDescending(rating => rating.UpdatedAt)
            .Take(MaxResults)
            .Select(rating => new RatedMovie(
                rating.MovieId,
                rating.Movie.Title,
                rating.Movie.PosterPath,
                rating.Movie.ReleaseDate,
                rating.Value,
                rating.UpdatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new MyRatingsResponse(rated));
    }
}

/// <summary>A page of the user's ratings.</summary>
/// <param name="Ratings">Rated films, most recently rated first.</param>
public sealed record MyRatingsResponse(IReadOnlyList<RatedMovie> Ratings);

/// <summary>A film the user has rated.</summary>
/// <param name="MovieId">NextMovie identifier.</param>
/// <param name="Title">Display title.</param>
/// <param name="PosterPath">Relative TMDb poster path.</param>
/// <param name="ReleaseDate">Release date, when known.</param>
/// <param name="Rating">0.5 to 5.0, in half-stars.</param>
/// <param name="RatedAt">When the rating was last set or changed.</param>
public sealed record RatedMovie(
    Guid MovieId,
    string Title,
    string? PosterPath,
    DateOnly? ReleaseDate,
    decimal Rating,
    DateTimeOffset RatedAt);
