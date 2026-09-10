using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Infrastructure.Tmdb;

namespace NextMovie.Api.Features.Movies;

/// <summary>
/// Returns everything the catalogue knows about one film.
/// </summary>
/// <remarks>
/// Read-through, like search: the film is served from our catalogue, and TMDb is
/// consulted only to fill in what search could not supply — runtime and status —
/// or to refresh details that have gone stale.
/// <para>
/// If TMDb is unreachable the film is still served, from whatever we already
/// hold. The catalogue exists precisely so that a third party being down degrades
/// the experience rather than ending it.
/// </para>
/// </remarks>
public static class GetMovieDetails
{
    /// <summary>
    /// How long enriched details are considered current.
    /// </summary>
    /// <remarks>
    /// Ratings and popularity move continuously; runtime and status almost never
    /// do. A week keeps the volatile fields roughly honest without putting a TMDb
    /// round trip on most reads — after the first fetch, the overwhelming
    /// majority of requests for a film are served entirely from our database.
    /// Purely a tunable: shortening it costs latency, lengthening it costs
    /// freshness, and neither changes the design.
    /// </remarks>
    private static readonly TimeSpan DetailsStayFresh = TimeSpan.FromDays(7);

    /// <summary>Registers the film details endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        // The route takes our identifier, not TMDb's. Accepting a TMDb id here
        // would make a third party's numbering part of our public contract.
        app.MapGet("/api/v1/movies/{id:guid}", HandleAsync)
            .WithName(nameof(GetMovieDetails))
            .WithSummary("Get a film")
            .WithDescription(
                "Returns a film from the NextMovie catalogue, enriching it from TMDb "
                + "when its details are missing or stale.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<MovieDetails>, ProblemHttpResult>> HandleAsync(
        Guid id,
        NextMovieDbContext db,
        ITmdbClient tmdb,
        MovieCatalog catalog,
        TimeProvider time,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var movie = await db.Movies
            .Include(candidate => candidate.Genres)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (movie is null)
        {
            // Only films someone has already searched for are in the catalogue.
            // This is genuinely "we do not have that", not "TMDb does not".
            return TypedResults.Problem(
                title: "Film not found",
                detail: "No film with that identifier is in the catalogue.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var now = time.GetUtcNow();

        if (NeedsEnriching(movie, now))
        {
            movie = await EnrichAsync(movie, db, tmdb, catalog, now, logger, cancellationToken);
        }

        return TypedResults.Ok(ToDetails(movie));
    }

    private static bool NeedsEnriching(Movie movie, DateTimeOffset now) =>
        movie.DetailsRefreshedAt is not { } refreshedAt || now - refreshedAt > DetailsStayFresh;

    private static async Task<Movie> EnrichAsync(
        Movie movie,
        NextMovieDbContext db,
        ITmdbClient tmdb,
        MovieCatalog catalog,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var details = await tmdb.GetMovieAsync(movie.TmdbId, cancellationToken);
            var mapped = TmdbMovieMapper.ToDomain(details, now);

            if (mapped is null)
            {
                logger.LogWarning(
                    "TMDb returned unusable details for film {TmdbId}; serving what we have",
                    movie.TmdbId);

                return movie;
            }

            // The same upsert search uses, so there is one place that knows how
            // TMDb data becomes catalogue data — including preserving the fields
            // a details fetch supplies and a search does not.
            var stored = await catalog.UpsertAsync([mapped], cancellationToken);

            return stored.Count > 0 ? stored[0] : movie;
        }
        catch (TmdbException exception)
        {
            // Deliberately swallowed. A film we already hold is worth serving
            // with a stale runtime; failing the request because a third party is
            // unavailable would throw away the entire reason for keeping our own
            // catalogue. Note this also covers TMDb 404ing a film it has since
            // removed — our copy outlives theirs.
            logger.LogWarning(
                exception,
                "Could not refresh details for film {TmdbId}; serving the stored copy",
                movie.TmdbId);

            // The context may hold half-applied changes from the failed attempt.
            db.ChangeTracker.Clear();

            return movie;
        }
    }

    private static MovieDetails ToDetails(Movie movie) => new(
        Id: movie.Id,
        TmdbId: movie.TmdbId,
        Title: movie.Title,
        OriginalTitle: movie.OriginalTitle,
        Overview: movie.Overview,
        PosterPath: movie.PosterPath,
        BackdropPath: movie.BackdropPath,
        ReleaseDate: movie.ReleaseDate,
        Runtime: movie.Runtime,
        AverageRating: movie.AverageRating,
        Popularity: movie.Popularity,
        Language: movie.Language,
        Status: movie.Status,
        Genres: [.. movie.Genres.Select(genre => genre.Name).Order()]);
}

/// <summary>Everything the catalogue holds about a film.</summary>
/// <remarks>
/// <c>docs/api.md</c> also promised streaming availability and a recommendation
/// explanation here. Neither exists yet — streaming availability has no schema
/// and no region concept, and the recommendation engine is deferred — so neither
/// is returned rather than stubbed. The doc has been corrected to match.
/// </remarks>
/// <param name="Id">NextMovie identifier.</param>
/// <param name="TmdbId">TMDb identifier, exposed for attribution and deep links.</param>
/// <param name="Title">Display title.</param>
/// <param name="OriginalTitle">Title in the original language, when it differs.</param>
/// <param name="Overview">Synopsis, when TMDb has one.</param>
/// <param name="PosterPath">Relative TMDb poster path; combine with a TMDb image base URL to render.</param>
/// <param name="BackdropPath">Relative TMDb backdrop path.</param>
/// <param name="ReleaseDate">Release date, when known.</param>
/// <param name="Runtime">Runtime in minutes. Null when TMDb does not know it.</param>
/// <param name="AverageRating">TMDb community rating 0–10. Null when the film has no votes.</param>
/// <param name="Popularity">TMDb popularity score. Only meaningful compared against other films.</param>
/// <param name="Language">ISO 639-1 code of the original language.</param>
/// <param name="Status">TMDb release status, e.g. <c>Released</c>.</param>
/// <param name="Genres">Genre names, alphabetically.</param>
public sealed record MovieDetails(
    Guid Id,
    int TmdbId,
    string Title,
    string? OriginalTitle,
    string? Overview,
    string? PosterPath,
    string? BackdropPath,
    DateOnly? ReleaseDate,
    int? Runtime,
    double? AverageRating,
    double? Popularity,
    string? Language,
    string? Status,
    IReadOnlyList<string> Genres);
