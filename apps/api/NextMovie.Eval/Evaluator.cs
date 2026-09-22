using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Recommendations.Evaluation;
using NextMovie.Api.Features.Recommendations;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Eval;

/// <summary>What one full evaluation produced.</summary>
/// <param name="Library">How many films the user has rated at all.</param>
/// <param name="Loved">How many of those count as favourites.</param>
/// <param name="HiddenPerTrial">How many were hidden in each split.</param>
/// <param name="Aggregate">Recall, MRR and hit rate across the trials.</param>
/// <param name="Quality">Quality of the final trial's list, which is a real list of films.</param>
/// <param name="Sample">The final trial's recommendations, for reading rather than counting.</param>
/// <param name="HitTitles">Which hidden films were recovered in the final trial.</param>
/// <param name="Elapsed">Wall clock, so the cost of a run is visible.</param>
internal sealed record EvaluationReport(
    int Library,
    int Loved,
    int HiddenPerTrial,
    Aggregate Aggregate,
    QualityProfile Quality,
    IReadOnlyList<SampleFilm> Sample,
    IReadOnlyList<string> HitTitles,
    TimeSpan Elapsed);

/// <summary>One recommendation, as a human would want to read it.</summary>
internal sealed record SampleFilm(int Rank, string Title, double? Rating, bool WasHidden, bool CanStreamNow);

/// <summary>
/// Runs the real recommender against a real library with its best films hidden.
/// </summary>
/// <remarks>
/// Every trial runs inside a transaction that is always rolled back (ADR-0012).
/// Hiding films means deleting rows, and the engine writes an impression for
/// everything it serves: an evaluation that damaged the library it evaluates
/// would be worse than none, and impressions from a run nobody saw would poison
/// the interaction log ADR-0011 exists to collect.
/// </remarks>
internal sealed class Evaluator(NextMovieDbContext db, RecommendationEngine engine, ILogger<Evaluator> logger)
{
    /// <summary>
    /// The rating below which a recommendation is a failure of the quality floor.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>RecommendationScorer.MinimumRating</c>. Restated rather than
    /// referenced so that changing the floor does not silently change what the
    /// evaluation considers a violation — the number here is the standard being
    /// held to, not a copy of the implementation's opinion of itself.
    /// </remarks>
    private const double QualityFloor = 6.5;

    public async Task<EvaluationReport?> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        // Checked first, and by name. Migrations are never applied automatically
        // (see the README), so a developer's database routinely trails the model
        // by a branch or two — and without this the symptom is a Postgres
        // "column does not exist" forty lines into a stack trace.
        var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);

        if (pending.ToList() is { Count: > 0 } outstanding)
        {
            logger.LogError(
                "The database is {Count} migration(s) behind ({Names}). Run: "
                + "dotnet ef database update --project apps/api/NextMovie.Api",
                outstanding.Count,
                string.Join(", ", outstanding));

            return null;
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.NormalizedEmail == User.NormalizeEmail(options.Email), cancellationToken);

        if (user is null)
        {
            logger.LogError("No account for {Email}", options.Email);

            return null;
        }

        var library = await db.Ratings.CountAsync(rating => rating.UserId == user.Id, cancellationToken);

        var loved = await db.Ratings
            .AsNoTracking()
            .Where(rating => rating.UserId == user.Id && rating.Value >= options.LovedAtLeast)
            .Select(rating => rating.MovieId)
            .ToListAsync(cancellationToken);

        if (loved.Count < 2)
        {
            logger.LogError(
                "{Email} has {Count} films rated {Loved} or higher. Evaluation needs at least two: "
                + "one to hide and one to recommend from",
                options.Email,
                loved.Count,
                options.LovedAtLeast);

            return null;
        }

        var started = TimeProvider.System.GetTimestamp();

        var trials = new List<Trial>(options.Trials);
        var hidden = 0;
        IReadOnlyList<EvaluatedFilm> lastList = [];
        IReadOnlySet<Guid> lastHidden = new HashSet<Guid>();
        var titles = new Dictionary<Guid, string>();

        for (var trial = 0; trial < options.Trials; trial++)
        {
            // A seed per trial, derived from the one the user gave. Reusing the
            // same seed would run the identical split N times and report its
            // result as an average of N independent ones.
            var holdOut = HoldOut.Choose(loved, options.HoldOut, options.Seed + trial).ToHashSet();
            hidden = holdOut.Count;

            var (scored, films, named) = await TrialAsync(user.Id, holdOut, options.Count, cancellationToken);

            trials.Add(scored);
            lastList = films;
            lastHidden = holdOut;
            titles = named;

            logger.LogInformation(
                "Trial {Trial}/{Total}: hid {Hidden}, recovered {Found}",
                trial + 1,
                options.Trials,
                holdOut.Count,
                scored.HitRanks.Count);
        }

        return new EvaluationReport(
            Library: library,
            Loved: loved.Count,
            HiddenPerTrial: hidden,
            Aggregate: RecommendationMetrics.Summarise(trials),
            Quality: RecommendationMetrics.Profile(lastList, QualityFloor),
            Sample:
            [
                .. lastList.Select(film => new SampleFilm(
                    film.Rank,
                    titles.GetValueOrDefault(film.MovieId, "?"),
                    film.Rating,
                    lastHidden.Contains(film.MovieId),
                    film.CanStreamNow)),
            ],
            HitTitles:
            [
                .. lastList
                    .Where(film => lastHidden.Contains(film.MovieId))
                    .Select(film => titles.GetValueOrDefault(film.MovieId, "?")),
            ],
            Elapsed: TimeProvider.System.GetElapsedTime(started));
    }

    /// <summary>
    /// Hides a set of films, asks for recommendations, then undoes all of it.
    /// </summary>
    private async Task<(Trial Scored, IReadOnlyList<EvaluatedFilm> Films, Dictionary<Guid, string> Titles)> TrialAsync(
        Guid userId,
        IReadOnlySet<Guid> holdOut,
        int count,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var hiding = holdOut.ToArray();

            // Both, not just ratings. The engine excludes anything watched, so a
            // film hidden from ratings alone would still be filtered out by its
            // viewing and could never be recovered — the evaluation would
            // faithfully measure zero recall forever.
            await db.Ratings
                .Where(rating => rating.UserId == userId && hiding.Contains(rating.MovieId))
                .ExecuteDeleteAsync(cancellationToken);

            await db.WatchHistory
                .Where(entry => entry.UserId == userId && hiding.Contains(entry.MovieId))
                .ExecuteDeleteAsync(cancellationToken);

            // Responses too: a dismissed or saved film is excluded by ADR-0011,
            // which would be a second invisible reason a hidden film never comes
            // back.
            await db.RecommendationResponses
                .Where(response => response.UserId == userId && hiding.Contains(response.MovieId))
                .ExecuteDeleteAsync(cancellationToken);

            db.ChangeTracker.Clear();

            var recommendations = await engine.RecommendAsync(userId, count, cancellationToken);

            var films = recommendations
                .Select(recommendation => new EvaluatedFilm(
                    recommendation.Movie.Id,
                    recommendation.Rank,
                    recommendation.Movie.AverageRating,
                    [.. recommendation.Movie.Genres.Select(genre => genre.Id)],
                    recommendation.Watch.CanStreamNow))
                .ToList();

            var titles = recommendations.ToDictionary(
                recommendation => recommendation.Movie.Id,
                recommendation => recommendation.Movie.Title);

            return (RecommendationMetrics.Score(films, holdOut), films, titles);
        }
        finally
        {
            // Always. Not on failure, not conditionally — there is no path on
            // which this run's writes should survive.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}
