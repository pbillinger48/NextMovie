namespace NextMovie.Api.Domain.Recommendations;

/// <summary>
/// Turns viewing and rating history into a description of what someone wants to watch.
/// </summary>
/// <remarks>
/// Pure, and takes the history rather than fetching it, so it can be run against a
/// real library and the result read by a person — which is the only test of
/// whether a taste model says anything true. That test is how the first version of
/// this was found to be wrong.
/// </remarks>
public static class TasteProfileBuilder
{
    /// <summary>
    /// How much of affinity comes from what someone watches rather than how they
    /// rate it.
    /// </summary>
    /// <remarks>
    /// Appetite leads because it is the less corruptible signal. Ratings measure
    /// critical judgement, which is not the same as appetite: people reserve five
    /// stars for films they admire and hand out threes to films they happily
    /// watch ten of. Choosing to watch ninety-seven animated films is a clearer
    /// statement of interest than the average score those films received.
    /// <para>
    /// Upside still matters, which is why it is not zero: within the genres
    /// somebody watches, how often the good ones land should decide how readily
    /// we recommend them.
    /// </para>
    /// </remarks>
    private const double AppetiteShare = 0.6;

    /// <summary>
    /// Notional ratings added to each genre at the user's own rate, so a genre
    /// needs weight of evidence before its upside moves.
    /// </summary>
    /// <remarks>
    /// One five-star horror film is not a passion for horror. Without this, the
    /// genres somebody has barely seen dominate — which is precisely backwards.
    /// </remarks>
    private const double UpsideShrinkage = 8.0;

    /// <summary>The rating at which a film counts as loved.</summary>
    private const decimal LovedThreshold = 4.5m;

    /// <summary>The rating at which a film counts as liked, for runtime and era preferences.</summary>
    private const decimal LikedThreshold = 4.0m;

    /// <summary>Builds a profile from everything known about a person's viewing.</summary>
    /// <param name="ratings">Films they have rated.</param>
    /// <param name="watched">
    /// Films they have watched, rated or not. Usually a superset of the ratings,
    /// and the source of appetite.
    /// </param>
    public static TasteProfile Build(
        IReadOnlyList<RatedFilm> ratings,
        IReadOnlyList<WatchedFilm> watched)
    {
        if (ratings.Count == 0 && watched.Count == 0)
        {
            return TasteProfile.Empty;
        }

        var appetite = Appetite(watched);
        var upside = Upside(ratings);

        var genres = appetite.Keys.Union(upside.Keys).ToList();

        var affinity = genres.ToDictionary(
            genreId => genreId,
            genreId => (AppetiteShare * appetite.GetValueOrDefault(genreId, 0))
                + ((1 - AppetiteShare) * upside.GetValueOrDefault(genreId, TasteProfile.NoOpinion)));

        var liked = ratings.Where(film => film.Rating >= LikedThreshold).ToList();

        return new TasteProfile(
            GenreAffinity: affinity,
            GenreAppetite: appetite,
            GenreUpside: upside,
            AverageRating: ratings.Count == 0 ? 0 : (double)ratings.Average(film => film.Rating),
            RatedFilms: ratings.Count,
            PreferredRuntimes: MiddleRange(liked.Select(film => film.Runtime)),
            PreferredEra: MiddleRange(liked.Select(film => film.ReleaseYear)));
    }

    /// <summary>
    /// How much of someone's viewing each genre accounts for, relative to the
    /// genre they watch most.
    /// </summary>
    /// <remarks>
    /// Log-scaled, because the gap between eleven films and ninety-seven matters
    /// far more than the gap between two hundred and three hundred: the first is
    /// the difference between dabbling and pursuing, the second is just volume.
    /// </remarks>
    private static Dictionary<int, double> Appetite(IReadOnlyList<WatchedFilm> watched)
    {
        var counts = new Dictionary<int, int>();

        foreach (var genreId in watched.SelectMany(film => film.GenreIds.Distinct()))
        {
            counts[genreId] = counts.GetValueOrDefault(genreId) + 1;
        }

        if (counts.Count == 0)
        {
            return counts.ToDictionary(entry => entry.Key, _ => TasteProfile.NoOpinion);
        }

        var ceiling = Math.Log(counts.Values.Max() + 1);

        return counts.ToDictionary(
            entry => entry.Key,
            entry => ceiling <= 0 ? TasteProfile.NoOpinion : Math.Log(entry.Value + 1) / ceiling);
    }

    /// <summary>
    /// How reliably each genre produces a film this person loves, relative to how
    /// often they love anything.
    /// </summary>
    /// <remarks>
    /// Measured against their own rate rather than an absolute one, because
    /// people differ in how freely they award top marks. A genre at exactly their
    /// usual rate scores <see cref="TasteProfile.NoOpinion"/>; twice their usual
    /// rate reaches the top of the scale.
    /// </remarks>
    private static Dictionary<int, double> Upside(IReadOnlyList<RatedFilm> ratings)
    {
        if (ratings.Count == 0)
        {
            return [];
        }

        var overall = (double)ratings.Count(film => film.Rating >= LovedThreshold) / ratings.Count;

        if (overall <= 0)
        {
            // Nothing has ever been loved, so nothing distinguishes the genres.
            return ratings
                .SelectMany(film => film.GenreIds)
                .Distinct()
                .ToDictionary(genreId => genreId, _ => TasteProfile.NoOpinion);
        }

        var totals = new Dictionary<int, (int Loved, int Rated)>();

        foreach (var film in ratings)
        {
            foreach (var genreId in film.GenreIds.Distinct())
            {
                var current = totals.GetValueOrDefault(genreId);

                totals[genreId] = (
                    current.Loved + (film.Rating >= LovedThreshold ? 1 : 0),
                    current.Rated + 1);
            }
        }

        return totals.ToDictionary(
            entry => entry.Key,
            entry =>
            {
                var (loved, rated) = entry.Value;
                var shrunk = (loved + (overall * UpsideShrinkage)) / (rated + UpsideShrinkage);

                // Their own rate sits at the middle of the scale; twice it, at the top.
                return Math.Clamp(shrunk / (2 * overall), 0, 1);
            });
    }

    /// <summary>
    /// The middle half of a set of values, as a range.
    /// </summary>
    /// <remarks>
    /// The interquartile range rather than a mean and standard deviation: runtimes
    /// and release years are not normally distributed, and one silent film or one
    /// four-hour epic would drag a mean somewhere unrepresentative.
    /// </remarks>
    private static Range<int>? MiddleRange(IEnumerable<int?> values)
    {
        var known = values.OfType<int>().Order().ToList();

        // Four is the fewest values from which quartiles say anything at all.
        if (known.Count < 4)
        {
            return null;
        }

        return new Range<int>(known[known.Count / 4], known[known.Count * 3 / 4]);
    }
}
