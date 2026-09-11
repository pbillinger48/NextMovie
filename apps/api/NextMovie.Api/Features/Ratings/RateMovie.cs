using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Library;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Ratings;

/// <summary>
/// Records what the signed-in user thinks of a film.
/// </summary>
/// <remarks>
/// <c>PUT</c> rather than <c>POST</c>: a person has one opinion of a film at a
/// time, so sending 4.5 twice must leave them rating it 4.5 rather than rating it
/// twice. The endpoint is idempotent because the resource is singular.
/// <para>
/// Native entry is a first-class writer here, not a special case of import
/// (ADR-0006). The rating it stores carries <see cref="LibrarySource.Native"/>,
/// which is what stops a later Letterboxd import overwriting it.
/// </para>
/// </remarks>
public static class RateMovie
{
    /// <summary>Registers the rating endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/movies/{id:guid}/rating", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(RateMovie))
            .WithSummary("Rate a film")
            .WithDescription(
                "Records the signed-in user's rating of a film, on a 0.5–5.0 half-star "
                + "scale. Rating a film also records that it was watched.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<MovieRating>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        RateMovieRequest request,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        UserLibrary library,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return RatingResults.NoLongerSignedIn();
        }

        if (RatingScale.Validate(request.Rating) is { } error)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Rating)] = [error],
            });
        }

        // Checked before writing because the foreign key would otherwise fail as
        // a 500. A film nobody has searched for is genuinely not in the
        // catalogue, and that is a 404 rather than a server fault.
        if (!await db.Movies.AnyAsync(movie => movie.Id == id, cancellationToken))
        {
            return RatingResults.FilmNotFound();
        }

        var rating = await library.RateAsync(
            userId,
            id,
            request.Rating!.Value,
            LibrarySource.Native,
            time.GetUtcNow(),
            cancellationToken);

        return TypedResults.Ok(new MovieRating(
            MovieId: rating.MovieId,
            Rating: rating.Value,
            RatedAt: rating.UpdatedAt));
    }
}

/// <summary>A rating to record.</summary>
/// <param name="Rating">0.5 to 5.0, in half-stars.</param>
public sealed record RateMovieRequest(decimal? Rating);

/// <summary>The signed-in user's rating of a film.</summary>
/// <param name="MovieId">The film rated.</param>
/// <param name="Rating">0.5 to 5.0, in half-stars.</param>
/// <param name="RatedAt">When the rating was last set or changed.</param>
public sealed record MovieRating(Guid MovieId, decimal Rating, DateTimeOffset RatedAt);

/// <summary>Responses shared by the rating slices.</summary>
internal static class RatingResults
{
    public static ProblemHttpResult NoLongerSignedIn() => TypedResults.Problem(
        title: "Not signed in",
        detail: "This session is no longer valid. Sign in again.",
        statusCode: StatusCodes.Status401Unauthorized);

    public static ProblemHttpResult FilmNotFound() => TypedResults.Problem(
        title: "Film not found",
        detail: "No film with that identifier is in the catalogue.",
        statusCode: StatusCodes.Status404NotFound);
}
