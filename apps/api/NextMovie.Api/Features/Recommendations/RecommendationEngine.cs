using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Recommendations;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Infrastructure.Tmdb;
using Dtos = NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Features.Recommendations;

/// <summary>
/// Produces recommendations for one person.
/// </summary>
/// <remarks>
/// The orchestration around the pure domain module: gather history, find
/// candidates, exclude, score, record. The judgement lives in
/// <see cref="TasteProfileBuilder"/> and <see cref="RecommendationScorer"/>, which
/// know nothing about databases or TMDb — this knows nothing about taste.
/// </remarks>
internal sealed class RecommendationEngine(
    NextMovieDbContext db,
    ITmdbClient tmdb,
    MovieCatalog catalog,
    TimeProvider time,
    ILogger<RecommendationEngine> logger)
{
    /// <summary>
    /// How many of the user's favourites are used to find related films.
    /// </summary>
    /// <remarks>
    /// Each seed is a TMDb call on the request path, so this is the main lever on
    /// how slow a recommendation is. Five gives a pool of roughly a hundred
    /// candidates, which is far more than the handful that get shown.
    /// </remarks>
    private const int SeedFilms = 5;

    /// <summary>Films to consider from each seed.</summary>
    private const int CandidatesPerSeed = 20;

    /// <summary>
    /// How much of a genre someone must watch before it is worth seeding from.
    /// </summary>
    /// <remarks>
    /// Low, because the point is to span their range rather than to be selective
    /// — but not zero, or a genre they have seen once would spend one of five
    /// requests on a guess.
    /// </remarks>
    private const double MinimumAppetiteToSeed = 0.3;

    /// <summary>
    /// How many genres are asked for their best films.
    /// </summary>
    /// <remarks>
    /// Each is one TMDb call on the request path, same as a seed. Five covers the
    /// range of what somebody watches without doubling the time a recommendation
    /// takes.
    /// </remarks>
    private const int DiscoverGenres = 5;

    /// <summary>
    /// The largest share of one response that may come from a single genre.
    /// </summary>
    /// <remarks>
    /// Without a cap, a library that is 40% drama produces twelve dramas: the
    /// scores are honest and the list is useless, because a person scanning it
    /// sees one idea repeated. This trades a little fidelity — a few slots go to
    /// films that scored slightly lower — for a list worth reading.
    /// </remarks>
    private const double MaxShareOfOneGenre = 0.34;

    /// <summary>
    /// The rating a film needs before it is used as a seed.
    /// </summary>
    /// <remarks>
    /// Seeds should be films someone actively loved, not merely the best of a
    /// mediocre history. Somebody whose top rating is 3.0 gets no seeds and an
    /// empty result that says so, which is more honest than recommending things
    /// related to a film they thought was fine.
    /// </remarks>
    private const decimal SeedThreshold = 4.0m;

    /// <summary>Builds a ranked list, and records what was served.</summary>
    public async Task<IReadOnlyList<Recommendation>> RecommendAsync(
        Guid userId,
        int count,
        CancellationToken cancellationToken)
    {
        var profile = await BuildProfileAsync(userId, cancellationToken);
        var seeds = await SeedsAsync(userId, profile, cancellationToken);

        if (seeds.Count == 0)
        {
            // Nothing loved enough to reason from. An empty list is the honest
            // answer; inventing one from popular films would be a different
            // product pretending to be this one.
            logger.LogInformation("No seed films for user {UserId}; no recommendations", userId);

            return [];
        }

        var candidates = await CandidatesAsync(seeds, profile, cancellationToken);
        var excluded = await SeenFilmsAsync(userId, cancellationToken);

        var genreNames = await db.Genres
            .AsNoTracking()
            .ToDictionaryAsync(genre => genre.Id, genre => genre.Name, cancellationToken);

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        var pool = candidates
            .Where(film => !excluded.Contains(film.Movie.Id))
            .Select(film => new
            {
                film.Movie,
                Candidate = new RecommendationCandidate(
                    film.Movie.Id,
                    [.. film.Movie.Genres.Select(genre => genre.Id)],
                    film.Movie.Runtime,
                    film.Movie.ReleaseDate,
                    film.Movie.AverageRating,
                    film.Movie.VoteCount),
            })

            // The quality floor, applied before ranking rather than as a penalty
            // inside it. A penalty can be outvoted by anything else in the score;
            // this cannot. Returning six good films beats twelve with duds in it.
            .Where(entry => RecommendationScorer.IsWorthRecommending(entry.Candidate, today))
            .ToList();

        // Worked out across the pool, so a genre shared by every candidate stops
        // deciding between them.
        var informativeness = RecommendationScorer.Informativeness(
            [.. pool.Select(entry => entry.Candidate)]);

        var scored = pool
            .Select(entry => new ScoredMovie(
                entry.Movie,
                RecommendationScorer.Score(entry.Candidate, profile, genreNames, informativeness)))
            .OrderByDescending(entry => entry.Scored.Score)
            .ToList();

        var ranked = Diversify(scored, count)
            .Select((entry, index) => new Recommendation(entry.Movie, entry.Scored, index + 1))
            .ToList();

        await RecordAsync(userId, ranked, cancellationToken);

        return ranked;
    }

    /// <remarks>
    /// Reads viewing as well as rating: what someone watches is most of what the
    /// profile is built from, and most of a real library is unrated.
    /// </remarks>
    private async Task<TasteProfile> BuildProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rated = await db.Ratings
            .AsNoTracking()
            .Where(rating => rating.UserId == userId)
            .Select(rating => new
            {
                rating.Value,
                GenreIds = rating.Movie.Genres.Select(genre => genre.Id).ToList(),
                rating.Movie.Runtime,
                rating.Movie.ReleaseDate,
            })
            .ToListAsync(cancellationToken);

        var watched = await db.WatchHistory
            .AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .Select(entry => entry.Movie.Genres.Select(genre => genre.Id).ToList())
            .ToListAsync(cancellationToken);

        return TasteProfileBuilder.Build(
            [.. rated.Select(film => new RatedFilm(
                film.Value,
                film.GenreIds,
                film.Runtime,
                film.ReleaseDate?.Year))],
            [.. watched.Select(genres => new WatchedFilm(genres))]);
    }

    /// <summary>
    /// The films worth asking TMDb "what else is like this?" about.
    /// </summary>
    /// <remarks>
    /// Spread across the range of genres this person watches, not concentrated at
    /// the top of it. Seeding from their favourite genres sounds right and is
    /// not: on a real library the top five genres by affinity were all
    /// drama-adjacent, every seed was a drama, and every candidate came back a
    /// drama — including for somebody whose five-star films include three
    /// animated ones that were never asked about.
    /// <para>
    /// Sampling across the range means the pool contains genuinely different
    /// films, which is the only point at which variety can enter. Ranking cannot
    /// diversify a pool that was uniform to begin with.
    /// </para>
    /// </remarks>
    private async Task<List<int>> SeedsAsync(
        Guid userId,
        TasteProfile profile,
        CancellationToken cancellationToken)
    {
        var loved = await db.Ratings
            .AsNoTracking()
            .Where(rating => rating.UserId == userId && rating.Value >= SeedThreshold)
            .OrderByDescending(rating => rating.Value)
            .ThenByDescending(rating => rating.UpdatedAt)
            .Select(rating => new
            {
                rating.Movie.TmdbId,
                GenreIds = rating.Movie.Genres.Select(genre => genre.Id).ToList(),
            })
            .ToListAsync(cancellationToken);

        if (loved.Count == 0)
        {
            return [];
        }

        // Genres they actually watch, best-liked first. Ones they have barely
        // touched are left out: a seed from a genre seen twice would spend a
        // request on a guess.
        var wanted = profile.GenreAffinity
            .Where(entry => profile.GenreAppetite.GetValueOrDefault(entry.Key) >= MinimumAppetiteToSeed)
            .OrderByDescending(entry => entry.Value)
            .Select(entry => entry.Key)
            .ToList();

        var seeds = new List<int>();

        for (var slot = 0; slot < SeedFilms && wanted.Count > 0; slot++)
        {
            // Evenly spaced through the list rather than taken from the front, so
            // the seeds span what they watch instead of clustering.
            var genreId = wanted[wanted.Count * slot / SeedFilms];

            var best = loved.FirstOrDefault(film =>
                film.GenreIds.Contains(genreId) && !seeds.Contains(film.TmdbId));

            if (best is not null)
            {
                seeds.Add(best.TmdbId);
            }
        }

        // Somebody whose loved films carry no genres still gets seeds.
        foreach (var film in loved.Where(film => !seeds.Contains(film.TmdbId)))
        {
            if (seeds.Count >= SeedFilms)
            {
                break;
            }

            seeds.Add(film.TmdbId);
        }

        return seeds;
    }

    /// <summary>
    /// Asks TMDb what is related to each seed, and puts the answers in our catalogue.
    /// </summary>
    /// <remarks>
    /// Sequential rather than parallel: five calls at a few hundred milliseconds
    /// each is acceptable on a request, and the upsert that follows each one needs
    /// the <c>DbContext</c>, which is not thread-safe. If this ever needs to be
    /// faster the answer is caching the pool, not racing the database.
    /// </remarks>
    private async Task<List<MovieWithGenres>> CandidatesAsync(
        IReadOnlyList<int> seeds,
        TasteProfile profile,
        CancellationToken cancellationToken)
    {
        var mapped = new Dictionary<int, MappedMovie>();

        // Source one: what TMDb considers related to films this person loved.
        foreach (var seed in seeds)
        {
            await CollectAsync(
                mapped,
                () => tmdb.GetRelatedMoviesAsync(seed, cancellationToken),
                $"films related to {seed}",
                cancellationToken);
        }

        // Source two: the best-reviewed films in the genres they watch.
        //
        // Relatedness alone is not enough for somebody with a large library: it
        // answers "what is like the films you loved", and for eight hundred films
        // watched, most of that answer is films they have already seen. One real
        // library's entire candidate pool contained a single unwatched film rated
        // above 8. This asks the question a recommendation is actually for.
        foreach (var genreId in BestGenres(profile))
        {
            await CollectAsync(
                mapped,
                () => tmdb.DiscoverBestInGenreAsync(
                    genreId,
                    RecommendationScorer.MinimumVotes,
                    cancellationToken),
                $"best films in genre {genreId}",
                cancellationToken);
        }

        if (mapped.Count == 0)
        {
            return [];
        }

        var stored = await catalog.UpsertAsync([.. mapped.Values], cancellationToken);

        return [.. stored.Select(movie => new MovieWithGenres(movie))];
    }

    /// <summary>
    /// The genres worth asking TMDb for its best films in.
    /// </summary>
    /// <remarks>
    /// Spread across what this person watches for the same reason the seeds are:
    /// taking the top few would ask for the best dramas five times over.
    /// </remarks>
    private static IEnumerable<int> BestGenres(TasteProfile profile)
    {
        var wanted = profile.GenreAffinity
            .Where(entry => profile.GenreAppetite.GetValueOrDefault(entry.Key) >= MinimumAppetiteToSeed)
            .OrderByDescending(entry => entry.Value)
            .Select(entry => entry.Key)
            .ToList();

        for (var slot = 0; slot < DiscoverGenres && wanted.Count > 0; slot++)
        {
            yield return wanted[wanted.Count * slot / DiscoverGenres];
        }
    }

    /// <summary>Runs one TMDb query into the candidate pool, tolerating failure.</summary>
    /// <remarks>
    /// One query failing costs its candidates, not the recommendation. With two
    /// sources and several queries each, a pool survives losing some of them.
    /// </remarks>
    private async Task CollectAsync(
        Dictionary<int, MappedMovie> pool,
        Func<Task<Dtos.TmdbSearchResponse>> query,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await query();

            foreach (var dto in response.Results.Take(CandidatesPerSeed))
            {
                if (TmdbMovieMapper.ToDomain(dto) is { } candidate)
                {
                    pool.TryAdd(candidate.Movie.TmdbId, candidate);
                }
            }
        }
        catch (TmdbException exception)
        {
            logger.LogWarning(exception, "Could not fetch {Description}", description);
        }
    }

    /// <summary>Everything this person has already seen or judged.</summary>
    private async Task<HashSet<Guid>> SeenFilmsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var watched = await db.WatchHistory
            .AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .Select(entry => entry.MovieId)
            .ToListAsync(cancellationToken);

        var rated = await db.Ratings
            .AsNoTracking()
            .Where(rating => rating.UserId == userId)
            .Select(rating => rating.MovieId)
            .ToListAsync(cancellationToken);

        return [.. watched, .. rated];
    }

    /// <summary>
    /// Takes the best films while stopping any one genre filling the list.
    /// </summary>
    /// <remarks>
    /// Greedy, best-first, skipping a film whose genres have already had their
    /// share. Anything skipped stays available: if the cap makes the list
    /// impossible to fill, the remaining best-scoring films fill it rather than
    /// returning fewer than asked for. Variety is a preference here, not a rule
    /// that can leave somebody with nothing.
    /// </remarks>
    private static List<ScoredMovie> Diversify(IReadOnlyList<ScoredMovie> scored, int count)
    {
        var cap = Math.Max(1, (int)Math.Ceiling(count * MaxShareOfOneGenre));
        var used = new Dictionary<int, int>();
        var chosen = new List<ScoredMovie>();
        var passedOver = new List<ScoredMovie>();

        foreach (var entry in scored)
        {
            if (chosen.Count >= count)
            {
                break;
            }

            var genreIds = entry.Movie.Genres.Select(genre => genre.Id).ToList();

            if (genreIds.Any(genreId => used.GetValueOrDefault(genreId) >= cap))
            {
                passedOver.Add(entry);
                continue;
            }

            chosen.Add(entry);

            foreach (var genreId in genreIds)
            {
                used[genreId] = used.GetValueOrDefault(genreId) + 1;
            }
        }

        // Backfill in score order with what the cap turned away.
        chosen.AddRange(passedOver.Take(count - chosen.Count));

        return chosen;
    }

    /// <remarks>
    /// Recorded before the response is sent, not after, so a recommendation that
    /// reached a person is never missing from the log. The cost is one insert per
    /// recommendation shown.
    /// </remarks>
    private async Task RecordAsync(
        Guid userId,
        IReadOnlyList<Recommendation> recommendations,
        CancellationToken cancellationToken)
    {
        if (recommendations.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();

        db.RecommendationEvents.AddRange(recommendations.Select(recommendation =>
            new RecommendationEvent
            {
                UserId = userId,
                MovieId = recommendation.Movie.Id,
                Rank = recommendation.Rank,
                Score = recommendation.Scored.Score,
                Confidence = recommendation.Scored.Confidence,
                Reasons = [.. recommendation.Scored.Reasons],
                ServedAt = now,
            }));

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record MovieWithGenres(Movie Movie);

    private sealed record ScoredMovie(Movie Movie, ScoredRecommendation Scored);
}

/// <summary>A film to recommend, with why and where it ranked.</summary>
internal sealed record Recommendation(Movie Movie, ScoredRecommendation Scored, int Rank);
