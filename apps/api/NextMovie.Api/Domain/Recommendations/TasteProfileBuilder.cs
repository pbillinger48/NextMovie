namespace NextMovie.Api.Domain.Recommendations;

/// <summary>
/// Turns a person's ratings into a description of what they like.
/// </summary>
/// <remarks>
/// Pure, and takes the ratings rather than fetching them, so it can be run
/// against a real library and the result read by a human — which is the only
/// useful test of whether a taste model says anything true.
/// </remarks>
public static class TasteProfileBuilder
{
    /// <summary>
    /// How strongly a genre is pulled back toward the user's overall average when
    /// there is little evidence for it.
    /// </summary>
    /// <remarks>
    /// One five-star horror film does not mean somebody loves horror. This is
    /// shrinkage: a genre's affinity is diluted by a notional five ratings at the
    /// user's own average, so a genre needs real weight of evidence before it
    /// moves. Without it, the genres a person has barely watched dominate the
    /// ranking — which is precisely backwards.
    /// </remarks>
    private const double GenreShrinkage = 5.0;

    /// <summary>
    /// The rating at which a film counts as "liked" for working out preferred
    /// runtimes and eras.
    /// </summary>
    /// <remarks>
    /// Preferences are taken from what someone rates well, not from everything
    /// they have seen. A person who watched a great many bad three-hour films is
    /// not thereby a fan of three-hour films.
    /// </remarks>
    private const decimal LikedThreshold = 4.0m;

    /// <summary>
    /// Builds a profile. Returns <see cref="TasteProfile.Empty"/> when there is
    /// nothing to learn from.
    /// </summary>
    public static TasteProfile Build(IReadOnlyList<RatedFilm> ratings)
    {
        if (ratings.Count == 0)
        {
            return TasteProfile.Empty;
        }

        var average = (double)ratings.Average(film => film.Rating);
        var liked = ratings.Where(film => film.Rating >= LikedThreshold).ToList();

        return new TasteProfile(
            GenreAffinity: GenreAffinity(ratings, average),
            AverageRating: average,
            RatedFilms: ratings.Count,

            // Preferences come from liked films; with too few of those there is
            // no preference to state, and inventing one would put a confident
            // number on noise.
            PreferredRuntimes: MiddleRange(liked.Select(film => film.Runtime)),
            PreferredEra: MiddleRange(liked.Select(film => film.ReleaseYear)));
    }

    /// <summary>
    /// How much better or worse than usual this person rates each genre.
    /// </summary>
    /// <remarks>
    /// Measured against their own average rather than an absolute scale, because
    /// people use the scale differently: someone whose mean is 4.2 is not
    /// enthusiastic about everything, they are generous. Affinity is the
    /// deviation from their own baseline, shrunk toward it by
    /// <see cref="GenreShrinkage"/>.
    /// </remarks>
    private static Dictionary<int, double> GenreAffinity(
        IReadOnlyList<RatedFilm> ratings,
        double average)
    {
        var totals = new Dictionary<int, (double Sum, int Count)>();

        foreach (var film in ratings)
        {
            foreach (var genreId in film.GenreIds.Distinct())
            {
                var current = totals.GetValueOrDefault(genreId);
                totals[genreId] = (current.Sum + (double)film.Rating, current.Count + 1);
            }
        }

        return totals.ToDictionary(
            entry => entry.Key,
            entry =>
            {
                var (sum, count) = entry.Value;

                // The shrunk mean: (observed total + prior) / (observed + prior
                // weight), then expressed as a deviation from the baseline.
                var shrunkMean = (sum + (average * GenreShrinkage)) / (count + GenreShrinkage);

                return shrunkMean - average;
            });
    }

    /// <summary>
    /// The middle half of a set of values, as a range.
    /// </summary>
    /// <remarks>
    /// The interquartile range rather than a mean and standard deviation: film
    /// runtimes and release years are not normally distributed — one silent film
    /// or one four-hour epic would drag a mean somewhere unrepresentative — and
    /// the middle half describes "what they usually watch" without being at the
    /// mercy of the extremes.
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
