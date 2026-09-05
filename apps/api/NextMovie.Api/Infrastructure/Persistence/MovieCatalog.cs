using Microsoft.EntityFrameworkCore;
using Npgsql;
using NextMovie.Api.Domain;
using NextMovie.Api.Infrastructure.Tmdb;

namespace NextMovie.Api.Infrastructure.Persistence;

/// <summary>
/// Writes films discovered from TMDb into our own catalogue.
/// </summary>
/// <remarks>
/// Deliberately not a generic repository — EF Core already is one. This exists
/// because "absorb a batch of TMDb films into our schema" is a real operation
/// with real rules (deduplicate by TMDb id, resolve genres, preserve relevance
/// order), and both search and the future Letterboxd import need it.
/// </remarks>
internal sealed class MovieCatalog(NextMovieDbContext db, ILogger<MovieCatalog> logger)
{
    /// <summary>
    /// Inserts films we have not seen and refreshes those we have, returning them
    /// in the order supplied.
    /// </summary>
    /// <remarks>
    /// Input order is preserved because TMDb returns search results by relevance.
    /// Reading them back in database order would quietly destroy the ranking,
    /// which is the most valuable part of a search response.
    /// </remarks>
    public async Task<IReadOnlyList<Movie>> UpsertAsync(
        IReadOnlyList<MappedMovie> incoming,
        CancellationToken cancellationToken)
    {
        if (incoming.Count == 0)
        {
            return [];
        }

        // TMDb can return the same film twice across edge cases; keep the first.
        var deduplicated = incoming
            .GroupBy(m => m.Movie.TmdbId)
            .Select(g => g.First())
            .ToList();

        var tmdbIds = deduplicated.Select(m => m.Movie.TmdbId).ToArray();

        var existing = await db.Movies
            .Include(m => m.Genres)
            .Where(m => tmdbIds.Contains(m.TmdbId))
            .ToDictionaryAsync(m => m.TmdbId, cancellationToken);

        // Reference data, ~19 rows. One query beats a lookup per film.
        var genresById = await db.Genres.ToDictionaryAsync(g => g.Id, cancellationToken);

        foreach (var mapped in deduplicated)
        {
            var genres = ResolveGenres(mapped, genresById);

            if (existing.TryGetValue(mapped.Movie.TmdbId, out var current))
            {
                Refresh(current, mapped.Movie, genres);
            }
            else
            {
                foreach (var genre in genres)
                {
                    mapped.Movie.Genres.Add(genre);
                }

                db.Movies.Add(mapped.Movie);
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another request inserted one of these films between our read and
            // our write. Its row carries the same TMDb data ours would have, so
            // this is a benign race rather than a failure: discard our pending
            // changes and read the winner's rows instead.
            logger.LogInformation(
                ex,
                "Concurrent insert detected while upserting {Count} films; re-reading",
                deduplicated.Count);

            db.ChangeTracker.Clear();
        }

        var stored = await db.Movies
            .Include(m => m.Genres)
            .Where(m => tmdbIds.Contains(m.TmdbId))
            .ToDictionaryAsync(m => m.TmdbId, cancellationToken);

        // Rebuild in the caller's order, skipping anything that somehow failed
        // to persist rather than returning a hole in the list.
        return [.. deduplicated
            .Select(m => stored.GetValueOrDefault(m.Movie.TmdbId))
            .OfType<Movie>()];
    }

    private List<Genre> ResolveGenres(MappedMovie mapped, IReadOnlyDictionary<int, Genre> genresById)
    {
        var resolved = new List<Genre>(mapped.GenreIds.Count);

        foreach (var genreId in mapped.GenreIds.Distinct())
        {
            if (genresById.TryGetValue(genreId, out var genre))
            {
                resolved.Add(genre);
            }
            else
            {
                // TMDb added a genre we have not seeded. Dropping it loses a
                // little metadata; failing the search would lose the film.
                logger.LogWarning(
                    "Unknown TMDb genre {GenreId} on film {TmdbId}; skipping",
                    genreId,
                    mapped.Movie.TmdbId);
            }
        }

        return resolved;
    }

    /// <summary>Copies refreshed TMDb data onto an existing row.</summary>
    /// <remarks>
    /// Id and CreatedAt are never touched: the surrogate key is ours forever, and
    /// CreatedAt records when we first saw the film, not when TMDb last changed it.
    /// </remarks>
    private static void Refresh(Movie current, Movie incoming, List<Genre> genres)
    {
        current.Title = incoming.Title;
        current.OriginalTitle = incoming.OriginalTitle;
        current.Overview = incoming.Overview;
        current.PosterPath = incoming.PosterPath;
        current.BackdropPath = incoming.BackdropPath;
        current.ReleaseDate = incoming.ReleaseDate;
        current.AverageRating = incoming.AverageRating;
        current.Popularity = incoming.Popularity;
        current.Language = incoming.Language;

        // Runtime and Status are absent from search results. Overwriting them
        // with null here would erase data a details fetch had already filled in.
        if (incoming.Runtime is not null)
        {
            current.Runtime = incoming.Runtime;
        }

        if (incoming.Status is not null)
        {
            current.Status = incoming.Status;
        }

        current.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var genre in genres.Where(g => !current.Genres.Any(existing => existing.Id == g.Id)))
        {
            current.Genres.Add(genre);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
