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
    /// Applies an imported rating and viewing, without trampling what the user typed.
    /// </summary>
    /// <returns>
    /// Whether anything was written. False means an existing native rating was
    /// left alone.
    /// </returns>
    /// <remarks>
    /// The whole reason ADR-0006 put <c>Source</c> on these tables. A re-import
    /// must be safe to run repeatedly, and "safe" specifically means it never
    /// overwrites an opinion the user entered by hand — they changed their mind
    /// since exporting, and the CSV is the stale copy.
    /// <para>
    /// Idempotent in the other direction too: a viewing is only added when there
    /// is not already one for the same film on the same date, so importing the
    /// same diary twice does not double everybody's rewatches.
    /// </para>
    /// </remarks>
    public async Task<bool> ImportAsync(
        Guid userId,
        Guid movieId,
        decimal? value,
        DateOnly? watchedOn,
        bool isLoggedViewing,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await db.Ratings
            .FirstOrDefaultAsync(r => r.UserId == userId && r.MovieId == movieId, cancellationToken);

        var nativeRatingWins = existing is { Source: LibrarySource.Native };

        if (value is not null && !nativeRatingWins)
        {
            if (existing is null)
            {
                db.Ratings.Add(new Rating
                {
                    UserId = userId,
                    MovieId = movieId,
                    Value = value.Value,
                    Source = LibrarySource.LetterboxdImport,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.Revise(value.Value, LibrarySource.LetterboxdImport, now);
            }
        }

        await RecordViewingAsync(userId, movieId, watchedOn, isLoggedViewing, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return !nativeRatingWins;
    }

    /// <summary>
    /// Records an imported viewing, without inventing rewatches.
    /// </summary>
    /// <remarks>
    /// Three Letterboxd exports describe the same library in different ways, and
    /// importing more than one of them must not multiply a person's viewing
    /// history. The rules, in the order they apply:
    /// <list type="bullet">
    /// <item>
    /// A <b>list</b> row (watched.csv, ratings.csv) says only "they have seen
    /// this". If any viewing of the film exists, it adds nothing.
    /// </item>
    /// <item>
    /// A <b>diary</b> row says "they saw this on this day". An existing viewing
    /// on the same day is the same viewing. A film known only from a list gets
    /// that row <b>upgraded</b> with the real date, because the list row and the
    /// diary row describe one viewing, not two.
    /// </item>
    /// <item>
    /// Anything else is a genuine rewatch and is inserted.
    /// </item>
    /// </list>
    /// Without the upgrade rule, importing watched.csv and then diary.csv turned
    /// 768 viewings into 1,080 on a real library — 297 films apparently watched
    /// twice that were watched once.
    /// </remarks>
    private async Task RecordViewingAsync(
        Guid userId,
        Guid movieId,
        DateOnly? watchedOn,
        bool isLoggedViewing,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await db.WatchHistory
            .Where(entry => entry.UserId == userId && entry.MovieId == movieId)
            .ToListAsync(cancellationToken);

        if (!isLoggedViewing)
        {
            if (existing.Count == 0)
            {
                db.WatchHistory.Add(NewViewing(userId, movieId, watchedOn, isLoggedViewing: false, now));
            }

            return;
        }

        // Only a *logged* viewing on the same day is a duplicate. A list row
        // that happens to carry the same date is not — its date means "added on",
        // and it is still waiting to be claimed by the diary entry it came from.
        //
        // Checking it the other way round loses viewings: the list row survives
        // unclaimed, and the film's next diary entry consumes it as an upgrade
        // instead of being recorded. Three films in a real library lost a genuine
        // rewatch exactly that way.
        if (existing.Any(entry => entry.IsLoggedViewing && entry.WatchedOn == watchedOn))
        {
            return;
        }

        var fromAList = existing.FirstOrDefault(entry => !entry.IsLoggedViewing);

        if (fromAList is not null)
        {
            // The same viewing, now with the date it actually happened.
            fromAList.WatchedOn = watchedOn;
            fromAList.IsLoggedViewing = true;

            return;
        }

        db.WatchHistory.Add(NewViewing(userId, movieId, watchedOn, isLoggedViewing: true, now));
    }

    private static WatchHistoryEntry NewViewing(
        Guid userId,
        Guid movieId,
        DateOnly? watchedOn,
        bool isLoggedViewing,
        DateTimeOffset now) => new()
    {
        UserId = userId,
        MovieId = movieId,
        WatchedOn = watchedOn,
        IsLoggedViewing = isLoggedViewing,
        Source = LibrarySource.LetterboxdImport,
        CreatedAt = now,
    };

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
