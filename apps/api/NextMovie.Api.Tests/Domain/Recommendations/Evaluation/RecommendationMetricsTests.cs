using NextMovie.Api.Domain.Recommendations.Evaluation;

namespace NextMovie.Api.Tests.Domain.Recommendations.Evaluation;

/// <summary>
/// Tests the arithmetic that will decide whether future scoring changes are
/// improvements.
/// </summary>
/// <remarks>
/// If these are wrong, every judgement built on them is wrong and looks
/// confident while being so — which is the precise failure ADR-0012 exists to
/// stop, repeated one level up.
/// </remarks>
public sealed class RecommendationMetricsTests
{
    private static readonly Guid Heat = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Arrival = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Ran = Guid.Parse("00000000-0000-0000-0000-0000000000a3");

    // --- finding the hidden films ---

    [Fact]
    public void Records_where_each_hidden_film_landed()
    {
        var trial = RecommendationMetrics.Score(
            [Film(Ran, rank: 1), Film(Heat, rank: 2), Film(Arrival, rank: 3)],
            new HashSet<Guid> { Heat, Arrival });

        Assert.Equal([2, 3], trial.HitRanks);
        Assert.Equal(2, trial.HeldOut);
        Assert.Equal(3, trial.Returned);
    }

    [Fact]
    public void Ranks_come_back_in_order()
    {
        // ReciprocalRank reads the first element as the best rank, so an unsorted
        // list would quietly report the wrong one whenever the recommender
        // happened to return the hidden films out of order.
        var trial = RecommendationMetrics.Score(
            [Film(Heat, rank: 9), Film(Arrival, rank: 2)],
            new HashSet<Guid> { Heat, Arrival });

        Assert.Equal([2, 9], trial.HitRanks);
    }

    [Fact]
    public void A_run_that_found_nothing_is_recorded_as_such()
    {
        var trial = RecommendationMetrics.Score([Film(Ran, rank: 1)], new HashSet<Guid> { Heat });

        Assert.Empty(trial.HitRanks);
        Assert.Equal(0, RecommendationMetrics.Recall(trial));
        Assert.Equal(0, RecommendationMetrics.ReciprocalRank(trial));
    }

    // --- recall ---

    [Theory]
    [InlineData(4, 1, 0.25)]
    [InlineData(4, 2, 0.5)]
    [InlineData(3, 3, 1.0)]
    public void Recall_is_the_share_of_hidden_films_recovered(int heldOut, int found, double expected)
    {
        var trial = new Trial(HeldOut: heldOut, Returned: 12, HitRanks: [.. Enumerable.Range(1, found)]);

        Assert.Equal(expected, RecommendationMetrics.Recall(trial), precision: 6);
    }

    [Fact]
    public void Hiding_nothing_scores_zero_rather_than_perfect()
    {
        // Zero, not one. A run that hid nothing found nothing, and calling that
        // perfect recall would flatter every evaluation of a user with no history.
        Assert.Equal(0, RecommendationMetrics.Recall(new Trial(HeldOut: 0, Returned: 12, HitRanks: [])));
    }

    // --- reciprocal rank ---

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 0.5)]
    [InlineData(10, 0.1)]
    public void Reciprocal_rank_rewards_finding_it_early(int rank, double expected)
    {
        var trial = new Trial(HeldOut: 1, Returned: 12, HitRanks: [rank]);

        Assert.Equal(expected, RecommendationMetrics.ReciprocalRank(trial), precision: 6);
    }

    [Fact]
    public void Reciprocal_rank_separates_lists_recall_cannot()
    {
        var early = new Trial(HeldOut: 4, Returned: 12, HitRanks: [1]);
        var late = new Trial(HeldOut: 4, Returned: 12, HitRanks: [12]);

        // Identical recall. The difference between these two lists is the whole
        // product, which is why recall alone is not enough to steer by.
        Assert.Equal(RecommendationMetrics.Recall(early), RecommendationMetrics.Recall(late));
        Assert.True(RecommendationMetrics.ReciprocalRank(early) > RecommendationMetrics.ReciprocalRank(late));
    }

    // --- aggregating ---

    [Fact]
    public void Summarising_averages_across_runs()
    {
        var summary = RecommendationMetrics.Summarise(
        [
            new Trial(HeldOut: 2, Returned: 12, HitRanks: [1, 4]),
            new Trial(HeldOut: 2, Returned: 12, HitRanks: []),
        ]);

        Assert.Equal(2, summary.Trials);
        Assert.Equal(0.5, summary.Recall, precision: 6);
        Assert.Equal(0.5, summary.MeanReciprocalRank, precision: 6);
        Assert.Equal(0.5, summary.HitRate, precision: 6);
    }

    [Fact]
    public void Summarising_no_runs_is_zero_rather_than_a_crash()
    {
        var summary = RecommendationMetrics.Summarise([]);

        Assert.Equal(0, summary.Trials);
        Assert.Equal(0, summary.Recall);
    }

    // --- quality ---

    [Fact]
    public void Median_of_an_even_list_is_the_mean_of_the_middle_two()
    {
        var profile = RecommendationMetrics.Profile(
            [Film(Heat, 1, rating: 8.0), Film(Arrival, 2, rating: 9.0)],
            floor: 6.5);

        // 8.5, not 8.0. Twelve is the default list length, so most runs are even
        // and taking the lower would understate nearly every one of them.
        Assert.Equal(8.5, profile.MedianRating);
    }

    [Fact]
    public void Median_of_an_odd_list_is_the_middle_value()
    {
        var profile = RecommendationMetrics.Profile(
            [Film(Heat, 1, rating: 9.0), Film(Arrival, 2, rating: 7.0), Film(Ran, 3, rating: 8.0)],
            floor: 6.5);

        // Sorted first — the list arrives in rank order, not rating order.
        Assert.Equal(8.0, profile.MedianRating);
    }

    [Fact]
    public void Films_below_the_floor_are_counted()
    {
        var profile = RecommendationMetrics.Profile(
            [Film(Heat, 1, rating: 6.0), Film(Arrival, 2, rating: 8.4)],
            floor: 6.5);

        // The standing alarm on the failure that actually happened: a list full of
        // 6.0s while the catalogue held a hundred films rated 8.0 and above.
        Assert.Equal(1, profile.BelowFloor);
        Assert.Equal(6.0, profile.LowestRating);
    }

    [Fact]
    public void An_unrated_film_is_not_a_floor_violation()
    {
        var profile = RecommendationMetrics.Profile(
            [Film(Heat, 1, rating: null), Film(Arrival, 2, rating: 8.4)],
            floor: 6.5);

        // "We do not know" is not "it is bad".
        Assert.Equal(0, profile.BelowFloor);
        Assert.Equal(8.4, profile.MedianRating);
    }

    [Fact]
    public void Quality_of_an_empty_list_reports_nothing_rather_than_zero()
    {
        var profile = RecommendationMetrics.Profile([], floor: 6.5);

        // Null, not 0.0 — which would read as "we recommended terrible films"
        // rather than "we recommended none".
        Assert.Null(profile.MedianRating);
        Assert.Null(profile.LowestRating);
        Assert.Equal(0, profile.Count);
    }

    [Fact]
    public void Variety_and_streamability_are_counted()
    {
        var profile = RecommendationMetrics.Profile(
        [
            new EvaluatedFilm(Heat, 1, 8.2, [28, 80], CanStreamNow: true),
            new EvaluatedFilm(Arrival, 2, 7.9, [878, 18], CanStreamNow: false),
            new EvaluatedFilm(Ran, 3, 8.2, [28, 18], CanStreamNow: true),
        ],
            floor: 6.5);

        Assert.Equal(4, profile.DistinctGenres);
        Assert.Equal(2, profile.Streamable);
    }

    private static EvaluatedFilm Film(Guid id, int rank, double? rating = 8.0) =>
        new(id, rank, rating, [878], CanStreamNow: false);
}
