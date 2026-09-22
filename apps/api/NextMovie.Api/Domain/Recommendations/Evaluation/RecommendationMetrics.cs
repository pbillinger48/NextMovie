namespace NextMovie.Api.Domain.Recommendations.Evaluation;

/// <summary>One recommended film, reduced to what evaluation cares about.</summary>
/// <param name="MovieId">Identifies it, and is how a held-out film is recognised.</param>
/// <param name="Rank">Position in the list, from 1.</param>
/// <param name="Rating">TMDb community rating, when known.</param>
/// <param name="GenreIds">Genres, for measuring how varied a list is.</param>
/// <param name="CanStreamNow">Whether the user could press play on it.</param>
internal sealed record EvaluatedFilm(
    Guid MovieId,
    int Rank,
    double? Rating,
    IReadOnlyList<int> GenreIds,
    bool CanStreamNow);

/// <summary>What one hold-out run found.</summary>
/// <param name="HeldOut">How many films were hidden.</param>
/// <param name="Returned">How many recommendations came back.</param>
/// <param name="HitRanks">Where the hidden films turned up, from 1. Empty when none did.</param>
internal sealed record Trial(int HeldOut, int Returned, IReadOnlyList<int> HitRanks);

/// <summary>Aggregate performance across several trials.</summary>
/// <param name="Trials">How many runs this summarises.</param>
/// <param name="Recall">
/// Mean share of hidden films that came back. <b>A lower bound on quality, not
/// accuracy</b> — most good recommendations are films the user has never seen and
/// are invisible to this number (ADR-0012).
/// </param>
/// <param name="MeanReciprocalRank">
/// Mean of 1/rank of the first hidden film found, 0 for a run that found none.
/// Rewards ranking a hidden film first rather than twelfth, which recall cannot
/// see.
/// </param>
/// <param name="HitRate">Share of runs that found at least one hidden film.</param>
internal sealed record Aggregate(int Trials, double Recall, double MeanReciprocalRank, double HitRate);

/// <summary>How good the returned list is, irrespective of what it found.</summary>
/// <param name="Count">Films returned.</param>
/// <param name="MedianRating">Median TMDb rating of those with one.</param>
/// <param name="LowestRating">Worst rating in the list.</param>
/// <param name="BelowFloor">How many fall under the quality floor. Should always be zero.</param>
/// <param name="DistinctGenres">Genres represented, as a blunt measure of variety.</param>
/// <param name="Streamable">How many the user could start watching now.</param>
internal sealed record QualityProfile(
    int Count,
    double? MedianRating,
    double? LowestRating,
    int BelowFloor,
    int DistinctGenres,
    int Streamable);

/// <summary>
/// The arithmetic behind "did that change help".
/// </summary>
/// <remarks>
/// Pure, and separated from the runner that gathers the rows, because this is
/// exactly the kind of code that is quietly wrong — an off-by-one in a rank, a
/// median that mishandles even counts — and it must not be the one part of the
/// evaluation with nothing checking it (ADR-0012).
/// </remarks>
internal static class RecommendationMetrics
{
    /// <summary>Finds where the hidden films landed in a returned list.</summary>
    public static Trial Score(IReadOnlyList<EvaluatedFilm> returned, IReadOnlySet<Guid> heldOut) => new(
        HeldOut: heldOut.Count,
        Returned: returned.Count,
        HitRanks: [.. returned.Where(film => heldOut.Contains(film.MovieId)).Select(film => film.Rank).Order()]);

    /// <summary>
    /// Share of the hidden films this run recovered.
    /// </summary>
    /// <remarks>
    /// Zero when nothing was hidden, rather than one. A run that hid nothing found
    /// nothing, and calling that perfect recall would flatter every evaluation of
    /// a user with no history.
    /// </remarks>
    public static double Recall(Trial trial) =>
        trial.HeldOut == 0 ? 0 : (double)trial.HitRanks.Count / trial.HeldOut;

    /// <summary>
    /// One over the rank of the first hidden film, or zero when none was found.
    /// </summary>
    /// <remarks>
    /// The measure recall is blind to. Two runs that each recover one film in
    /// twelve score identically on recall whether it came first or last, and the
    /// difference between those two lists is the whole product.
    /// </remarks>
    public static double ReciprocalRank(Trial trial) =>
        trial.HitRanks.Count == 0 ? 0 : 1.0 / trial.HitRanks[0];

    /// <summary>Averages a set of trials.</summary>
    public static Aggregate Summarise(IReadOnlyList<Trial> trials) =>
        trials.Count == 0
            ? new Aggregate(0, 0, 0, 0)
            : new Aggregate(
                Trials: trials.Count,
                Recall: trials.Average(Recall),
                MeanReciprocalRank: trials.Average(ReciprocalRank),
                HitRate: trials.Count(trial => trial.HitRanks.Count > 0) / (double)trials.Count);

    /// <summary>
    /// Describes the quality of a returned list.
    /// </summary>
    /// <remarks>
    /// Reported alongside recall because the failure that actually happened was a
    /// quality failure, not a recall failure: a recommender that recovers hidden
    /// films <em>and</em> returns dross scores well above and is still bad.
    /// </remarks>
    public static QualityProfile Profile(IReadOnlyList<EvaluatedFilm> films, double floor)
    {
        var ratings = films
            .Select(film => film.Rating)
            .OfType<double>()
            .ToList();

        return new QualityProfile(
            Count: films.Count,
            MedianRating: Median(ratings),
            LowestRating: ratings.Count == 0 ? null : ratings.Min(),

            // Films with no rating are not counted as violations. "We do not know"
            // is not "it is bad", and the floor is applied on known ratings.
            BelowFloor: ratings.Count(rating => rating < floor),
            DistinctGenres: films.SelectMany(film => film.GenreIds).Distinct().Count(),
            Streamable: films.Count(film => film.CanStreamNow));
    }

    /// <remarks>
    /// The mean of the middle two for an even count, not the lower of them. A
    /// list of 8.0 and 9.0 has a median of 8.5, and reporting 8.0 would
    /// understate every even-length run — which is most of them, since twelve is
    /// the default.
    /// </remarks>
    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();

        var middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }
}
