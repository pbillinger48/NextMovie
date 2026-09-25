using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Library;
using NextMovie.Api.Domain.Recommendations.Evaluation;
using NextMovie.Api.Features.Recommendations;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Eval;

/// <summary>One taste, and what the recommender offered it.</summary>
/// <param name="Taste">The genre the cohort's library was drawn from.</param>
/// <param name="Library">How many films that taste was built from.</param>
/// <param name="Titles">What came back, in rank order.</param>
/// <param name="Films">The same list as a set, for comparing.</param>
/// <param name="SharedWithOthers">How many of its films another cohort also got.</param>
/// <param name="Queried">Whether TMDb was asked for the best films of this taste at all.</param>
/// <param name="Contending">
/// Candidates carrying this taste that survived the quality floor — the films that
/// actually competed for a slot.
/// </param>
/// <param name="PoolSize">How many films competed in total.</param>
/// <param name="PassedOver">
/// The best-scoring film of this taste that did not make the list, against the
/// weakest that did. Null when the taste had nothing in the pool, or when nothing
/// of it was passed over.
/// </param>
/// <param name="OnTaste">
/// How many carry the genre the reader was built from. Overlap can only say that
/// two lists differ; this says whether a list is <em>about</em> the reader it was
/// made for, which is a different and more demanding question.
/// </param>
internal sealed record Cohort(
    string Taste,
    int Library,
    IReadOnlyList<string> Titles,
    IReadOnlySet<Guid> Films,
    int SharedWithOthers,
    int OnTaste,
    bool Queried,
    int Contending,
    int PoolSize,
    LostRace? PassedOver);

/// <summary>Why a film of the reader's own taste lost its place.</summary>
/// <param name="LoserTitle">The best on-taste film that was left out.</param>
/// <param name="Loser">What it scored.</param>
/// <param name="WinnerTitle">The weakest film that made the list instead.</param>
/// <param name="Winner">What that scored.</param>
internal sealed record LostRace(
    string LoserTitle,
    ContendingFilm Loser,
    string WinnerTitle,
    ContendingFilm Winner);

/// <summary>What the personalisation check found.</summary>
/// <param name="Cohorts">One per taste.</param>
/// <param name="Overlap">How alike their whole lists were.</param>
/// <param name="Head">
/// How alike their opening slots were. Reported separately because a list whose
/// tail diverges and whose head does not is personalised in the part nobody
/// reads.
/// </param>
/// <param name="HeadSize">How many opening slots <paramref name="Head"/> covers.</param>
/// <param name="UniversalTitles">Films every cohort was offered.</param>
/// <param name="Elapsed">Wall clock.</param>
internal sealed record PersonalisationReport(
    IReadOnlyList<Cohort> Cohorts,
    Overlap Overlap,
    Overlap Head,
    int HeadSize,
    IReadOnlyList<string> UniversalTitles,
    TimeSpan Elapsed);

/// <summary>
/// Asks whether the recommender is reading taste or reciting the canon.
/// </summary>
/// <remarks>
/// Hold-out recall cannot tell those apart: most people have seen the canon, so a
/// recommender that returns it scores well whether or not it read anything about
/// them. This builds deliberately contrasting readers and checks whether their
/// answers differ.
/// <para>
/// Each cohort's library is drawn from <em>real</em> ratings — one person's loved
/// films of a single genre — rather than invented. A synthetic library of films
/// nobody rated would test the engine against data it will never see.
/// </para>
/// </remarks>
internal sealed class PersonalisationCheck(
    NextMovieDbContext db,
    RecommendationEngine engine,
    ILogger<PersonalisationCheck> logger)
{
    /// <summary>
    /// Fewest loved films a taste needs before it is worth building a reader from.
    /// </summary>
    /// <remarks>
    /// The engine seeds from five films. A cohort with fewer has a taste the
    /// engine cannot really read, and its list would say more about the shortage
    /// than about personalisation.
    /// </remarks>
    private const int MinimumLibrary = 5;

    /// <summary>
    /// How many opening slots count as "the head" of a list.
    /// </summary>
    /// <remarks>
    /// Three, because that is roughly what somebody reads before deciding the
    /// page is useful. A list that personalises slots four to twelve and not the
    /// first three has personalised the part nobody looks at.
    /// </remarks>
    private const int HeadSize = 3;

    public async Task<PersonalisationReport?> RunAsync(
        EvaluationOptions options,
        CancellationToken cancellationToken)
    {
        if (await db.Database.GetPendingMigrationsAsync(cancellationToken) is { } pending
            && pending.ToList() is { Count: > 0 } outstanding)
        {
            logger.LogError(
                "The database is {Count} migration(s) behind ({Names}). Run: "
                + "dotnet ef database update --project apps/api/NextMovie.Api",
                outstanding.Count,
                string.Join(", ", outstanding));

            return null;
        }

        var source = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.NormalizedEmail == User.NormalizeEmail(options.Email),
                cancellationToken);

        if (source is null)
        {
            logger.LogError("No account for {Email}", options.Email);

            return null;
        }

        var byGenre = await LovedByGenreAsync(source.Id, options.LovedAtLeast, cancellationToken);

        var usable = byGenre.Where(genre => genre.Value.Count >= MinimumLibrary).ToList();

        var tastes = options.Tastes.Count > 0
            ? [.. usable.Where(genre => options.Tastes.Contains(genre.Key, StringComparer.OrdinalIgnoreCase))]

            // Largest first by default: more films means a taste the engine can
            // actually read. It is also the weakest version of this test, because
            // the biggest genres are the ones that share the most films with each
            // other — name contrasting genres with --tastes for a sharper answer.
            : usable.OrderByDescending(genre => genre.Value.Count).Take(options.Cohorts).ToList();

        foreach (var missing in options.Tastes.Where(wanted =>
            !tastes.Any(taste => string.Equals(taste.Key, wanted, StringComparison.OrdinalIgnoreCase))))
        {
            // Named and skipped rather than silently absent: a typo would
            // otherwise quietly shrink the comparison and nobody would know.
            logger.LogWarning(
                "Skipping '{Taste}': no genre of that name with at least {Minimum} loved films",
                missing,
                MinimumLibrary);
        }

        if (tastes.Count < 2)
        {
            logger.LogError(
                "Only {Count} genre(s) have {Minimum} or more loved films. Comparing needs at least two",
                tastes.Count,
                MinimumLibrary);

            return null;
        }

        var genreIds = await db.Genres
            .AsNoTracking()
            .ToDictionaryAsync(genre => genre.Name, genre => genre.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var started = TimeProvider.System.GetTimestamp();
        var results = new List<(string Taste, int Library, IReadOnlyList<Recommendation> Films, RecommendationTrace Trace)>();

        foreach (var (taste, films) in tastes)
        {
            var trace = new RecommendationTrace();
            var recommendations = await ForTasteAsync(taste, films, options.Count, trace, cancellationToken);

            results.Add((taste, films.Count, recommendations, trace));

            logger.LogInformation(
                "{Taste}: built from {Library} films, {Fetched} fetched, {Pool} competing, offered {Offered}",
                taste,
                films.Count,
                trace.Fetched,
                trace.Contending.Count,
                recommendations.Count);
        }

        var lists = results
            .Select(result => (IReadOnlySet<Guid>)result.Films.Select(film => film.Movie.Id).ToHashSet())
            .ToList();

        var overlap = OverlapMetrics.Compare(lists);

        // The same comparison over the opening slots only. Averaging across a
        // whole list hides a head that converges while the tail spreads — which
        // is the failure worth catching, since the head is what gets read.
        var heads = results
            .Select(result => (IReadOnlySet<Guid>)result.Films
                .Take(HeadSize)
                .Select(film => film.Movie.Id)
                .ToHashSet())
            .ToList();

        var head = OverlapMetrics.Compare(heads);

        // Titles come from the trace, not the database. Every cohort's run is
        // rolled back, so the films that competed no longer have rows by the time
        // this assembles a report about them.
        var titles = results
            .SelectMany(result => result.Trace.Contending)
            .DistinctBy(film => film.MovieId)
            .ToDictionary(film => film.MovieId, film => film.Title);

        return new PersonalisationReport(
            Cohorts:
            [
                .. results.Select((result, index) => new Cohort(
                    result.Taste,
                    result.Library,
                    [.. result.Films.Select(film => film.Movie.Title)],
                    lists[index],
                    OverlapMetrics.SharedWithAny(lists[index], lists),
                    result.Films.Count(film => film.Movie.Genres.Any(genre =>
                        string.Equals(genre.Name, result.Taste, StringComparison.OrdinalIgnoreCase))),
                    Queried: genreIds.TryGetValue(result.Taste, out var id)
                        && result.Trace.DiscoveryGenres.Contains(id),
                    Contending: genreIds.TryGetValue(result.Taste, out var contendingId)
                        ? result.Trace.Contending.Count(film => film.GenreIds.Contains(contendingId))
                        : 0,
                    PoolSize: result.Trace.Contending.Count,
                    PassedOver: PassedOver(result.Trace, result.Films, genreIds, result.Taste))),
            ],
            Overlap: overlap,
            Head: head,
            HeadSize: HeadSize,
            UniversalTitles: [.. overlap.Universal.Select(film => titles.GetValueOrDefault(film, "?")).Order()],
            Elapsed: TimeProvider.System.GetElapsedTime(started));
    }

    /// <summary>
    /// The closest thing to a direct answer this tool can give: the best film of
    /// the reader's own taste that lost, and the weakest that beat it.
    /// </summary>
    /// <remarks>
    /// Deliberately the <em>weakest</em> winner rather than the strongest. The
    /// question worth answering is what the losing film failed to clear, and the
    /// top of the list is nowhere near that bar.
    /// </remarks>
    private static LostRace? PassedOver(
        RecommendationTrace trace,
        IReadOnlyList<Recommendation> shown,
        IReadOnlyDictionary<string, int> genreIds,
        string taste)
    {
        if (!genreIds.TryGetValue(taste, out var genreId))
        {
            return null;
        }

        var made = shown.Select(film => film.Movie.Id).ToHashSet();

        // Contending is in rank order, so the first match is the best.
        var loser = trace.Contending.FirstOrDefault(film =>
            film.GenreIds.Contains(genreId) && !made.Contains(film.MovieId));

        var winner = trace.Contending.LastOrDefault(film => made.Contains(film.MovieId));

        return loser is null || winner is null
            ? null
            : new LostRace(loser.Title, loser, winner.Title, winner);
    }

    /// <summary>The source user's loved films, grouped by genre.</summary>
    private async Task<Dictionary<string, List<Guid>>> LovedByGenreAsync(
        Guid userId,
        decimal lovedAtLeast,
        CancellationToken cancellationToken)
    {
        var loved = await db.Ratings
            .AsNoTracking()
            .Where(rating => rating.UserId == userId && rating.Value >= lovedAtLeast)
            .Select(rating => new
            {
                rating.MovieId,
                Genres = rating.Movie.Genres.Select(genre => genre.Name).ToList(),
            })
            .ToListAsync(cancellationToken);

        var byGenre = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);

        foreach (var film in loved)
        {
            foreach (var genre in film.Genres)
            {
                // A film lands in every genre it carries, so tastes are not
                // disjoint — which is honest. Nobody's love of crime films is
                // cleanly separable from their love of drama, and forcing a
                // single genre per film would invent a precision the data does
                // not have.
                if (!byGenre.TryGetValue(genre, out var films))
                {
                    byGenre[genre] = films = [];
                }

                films.Add(film.MovieId);
            }
        }

        return byGenre;
    }

    /// <summary>
    /// Invents a reader who loves exactly these films, asks what it should watch,
    /// then undoes all of it.
    /// </summary>
    private async Task<IReadOnlyList<Recommendation>> ForTasteAsync(
        string taste,
        IReadOnlyList<Guid> library,
        int count,
        RecommendationTrace trace,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var reader = new User
            {
                // Unique per run, and rolled back regardless — but a collision
                // with a real account would abort the run rather than corrupt
                // anything, and a readable address makes that obvious in a log.
                Email = $"cohort-{taste.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}@evaluation.invalid",
                DisplayName = $"{taste} reader",
            };

            db.Users.Add(reader);

            foreach (var movieId in library)
            {
                db.Ratings.Add(new Rating
                {
                    UserId = reader.Id,
                    MovieId = movieId,
                    Value = 5.0m,
                    Source = LibrarySource.Native,
                });

                // Viewings too. The taste profile weighs revealed preference —
                // what somebody actually watches — at least as heavily as what
                // they rate, so a reader with ratings and no history would be
                // read as half a person.
                db.WatchHistory.Add(new WatchHistoryEntry
                {
                    UserId = reader.Id,
                    MovieId = movieId,
                    Source = LibrarySource.Native,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();

            return await engine.RecommendAsync(reader.Id, count, cancellationToken, trace);
        }
        finally
        {
            // Always. These readers do not exist and must not survive the run.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}
