using Microsoft.EntityFrameworkCore;
using Npgsql;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Library;

namespace NextMovie.Api.Infrastructure.Persistence;

/// <summary>
/// Writes a user's opinions and viewings into our schema.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="MovieCatalog"/>, and here for the same reason:
/// "record that someone rated a film" is a real operation with a real rule
/// attached, and both native rating and the Letterboxd import need it to behave
/// identically.
/// <para>
/// That rule is ADR-0006's: <b>a rating implies a viewing</b>. It lives here, in
/// one place, rather than being remembered at each call site — a recommendation
/// engine that kept suggesting a film someone had rated would be obviously
/// broken, and the way that happens is one caller forgetting the second write.
/// </para>
/// </remarks>
internal sealed class UserLibrary(NextMovieDbContext db, ILogger<UserLibrary> logger)
{
    private const string RatingUniqueIndex = "ix_ratings_user_id_movie_id";

    /// <summary>
    /// Records what a user thinks of a film, creating a viewing if there is none.
    /// </summary>
    /// <remarks>
    /// Saves internally, and deliberately: the rating and the viewing it implies
    /// must land together or not at all, and leaving that to callers is how half
    /// of the invariant ends up committed on its own.
    /// </remarks>
    public async Task<Rating> RateAsync(
        Guid userId,
        Guid movieId,
        decimal value,
        LibrarySource source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ApplyRatingAsync(userId, movieId, value, source, now, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsConcurrentFirstRating(exception))
        {
            // Two requests rated the same film at once — a double-clicked star,
            // usually. The second attempt finds the row the first created and
            // revises it, which is what the user meant either way.
            logger.LogInformation(
                "Concurrent first rating for user {UserId} film {MovieId}; revising instead",
                userId,
                movieId);

            db.ChangeTracker.Clear();

            return await ApplyRatingAsync(userId, movieId, value, source, now, cancellationToken);
        }
    }

    /// <summary>
    /// Removes a user's rating of a film, if there is one.
    /// </summary>
    /// <returns>Whether a rating was actually removed.</returns>
    /// <remarks>
    /// The viewing is left alone. Changing your mind about a rating is not a
    /// claim that you never saw the film.
    /// </remarks>
    public async Task<bool> UnrateAsync(Guid userId, Guid movieId, CancellationToken cancellationToken)
    {
        var rating = await db.Ratings
            .FirstOrDefaultAsync(r => r.UserId == userId && r.MovieId == movieId, cancellationToken);

        if (rating is null)
        {
            return false;
        }

        db.Ratings.Remove(rating);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<Rating> ApplyRatingAsync(
        Guid userId,
        Guid movieId,
        decimal value,
        LibrarySource source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rating = await db.Ratings
            .FirstOrDefaultAsync(r => r.UserId == userId && r.MovieId == movieId, cancellationToken);

        if (rating is null)
        {
            rating = new Rating
            {
                UserId = userId,
                MovieId = movieId,
                Value = value,
                Source = source,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Ratings.Add(rating);
        }
        else
        {
            rating.Revise(value, source, now);
        }

        var alreadyWatched = await db.WatchHistory
            .AnyAsync(entry => entry.UserId == userId && entry.MovieId == movieId, cancellationToken);

        if (!alreadyWatched)
        {
            // Null date, not today's: rating a film from memory says nothing
            // about when it was seen, and inventing a date would sort and average
            // wrongly forever after.
            db.WatchHistory.Add(new WatchHistoryEntry
            {
                UserId = userId,
                MovieId = movieId,
                WatchedOn = null,
                Source = source,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return rating;
    }

    private static bool IsConcurrentFirstRating(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: RatingUniqueIndex,
        };
}
