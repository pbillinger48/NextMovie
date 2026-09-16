using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Recommendations;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Features.Streaming;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Watchlist;

/// <summary>
/// Returns the films the signed-in user has saved.
/// </summary>
/// <remarks>
/// A query over responses rather than a table of its own: the watchlist
/// <em>is</em> <see cref="ResponseKind.Saved"/>, newest first (ADR-0011). Two
/// tables would let "saved" and "on the watchlist" disagree.
/// <para>
/// Availability is resolved for the whole list in one pass, so the page answers
/// the question people actually open it with — not "what did I mean to watch" but
/// "what can I watch tonight".
/// </para>
/// </remarks>
public static class GetWatchlist
{
    /// <summary>
    /// How many saved films are looked up at once.
    /// </summary>
    /// <remarks>
    /// Availability costs an upstream call per film on a cold cache, so an
    /// unbounded watchlist would be an unbounded page load. Generous enough that
    /// nobody realistic meets it, and present so that somebody unrealistic does
    /// not take the API down.
    /// </remarks>
    private const int MaxFilms = 100;

    /// <summary>Registers the watchlist endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/me/watchlist", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(GetWatchlist))
            .WithSummary("Get the signed-in user's watchlist")
            .WithDescription(
                "Returns the films the user has saved, most recently saved first, "
                + "each with where it can be watched in their region.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<WatchlistResponse>, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        AvailabilityCatalog availability,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return TypedResults.Problem(
                title: "Not signed in",
                detail: "This session is no longer valid. Sign in again.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new
            {
                candidate.Region,
                ProviderIds = candidate.StreamingProviders
                    .Select(subscription => subscription.StreamingProviderId)
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return TypedResults.Problem(
                title: "Not signed in",
                detail: "This session is no longer valid. Sign in again.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var saved = await db.RecommendationResponses
            .AsNoTracking()
            .Where(response => response.UserId == userId && response.Kind == ResponseKind.Saved)
            .OrderByDescending(response => response.RespondedAt)
            .Take(MaxFilms)
            .Select(response => new
            {
                response.RespondedAt,
                Movie = response.Movie,
                Genres = response.Movie.Genres.Select(genre => genre.Name).ToList(),
            })
            .ToListAsync(cancellationToken);

        var films = saved.Select(entry => entry.Movie).ToList();
        var known = await availability.ForFilmsAsync(films, user.Region, cancellationToken);
        var subscribed = user.ProviderIds.ToHashSet();

        return TypedResults.Ok(new WatchlistResponse(
        [
            .. saved.Select(entry => ToResponse(entry.Movie, entry.Genres, entry.RespondedAt, known, subscribed)),
        ]));
    }

    private static SavedFilm ToResponse(
        Movie movie,
        IReadOnlyList<string> genres,
        DateTimeOffset savedAt,
        IReadOnlyDictionary<Guid, MovieAvailability> known,
        IReadOnlySet<int> subscribed)
    {
        var watch = known.TryGetValue(movie.Id, out var stored)
            ? WatchOptions.From([.. stored.Offers], subscribed, stored.Link)
            : WatchOptions.Unknown;

        return new SavedFilm(
            MovieId: movie.Id,
            Title: movie.Title,
            PosterPath: movie.PosterPath,
            ReleaseDate: movie.ReleaseDate,
            Runtime: movie.Runtime,
            AverageRating: movie.AverageRating,
            Genres: [.. genres.Order()],
            SavedAt: savedAt,
            Watch: new WatchingOptions(
                StreamingOn: watch.StreamingOn,
                StreamingElsewhere: watch.StreamingElsewhere,
                RentOrBuy: watch.RentOrBuy,
                CanStreamNow: watch.CanStreamNow,
                Known: watch.Known,
                Link: watch.Link));
    }
}

/// <summary>Films saved for later.</summary>
/// <param name="Films">Most recently saved first. Empty when nothing is saved.</param>
public sealed record WatchlistResponse(IReadOnlyList<SavedFilm> Films);

/// <summary>One saved film.</summary>
/// <param name="MovieId">NextMovie identifier.</param>
/// <param name="Title">Display title.</param>
/// <param name="PosterPath">Relative TMDb poster path.</param>
/// <param name="ReleaseDate">Release date, when known.</param>
/// <param name="Runtime">Runtime in minutes, when known.</param>
/// <param name="AverageRating">TMDb community rating 0–10.</param>
/// <param name="Genres">Genre names, alphabetically.</param>
/// <param name="SavedAt">When it was saved.</param>
/// <param name="Watch">Where the user can watch it, in their region.</param>
public sealed record SavedFilm(
    Guid MovieId,
    string Title,
    string? PosterPath,
    DateOnly? ReleaseDate,
    int? Runtime,
    double? AverageRating,
    IReadOnlyList<string> Genres,
    DateTimeOffset SavedAt,
    WatchingOptions Watch);
