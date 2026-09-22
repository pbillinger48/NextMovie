using NextMovie.Api.Domain.Recommendations.Evaluation;

namespace NextMovie.Api.Tests.Domain.Recommendations.Evaluation;

/// <summary>
/// Tests the measure that tells personalisation from popularity.
/// </summary>
/// <remarks>
/// A high score here is bad — it means everybody gets the same films. The
/// inversion is easy to get backwards in code and in reading, which is most of
/// why these exist.
/// </remarks>
public sealed class OverlapMetricsTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    // --- pairwise similarity ---

    [Fact]
    public void Identical_lists_score_one()
    {
        Assert.Equal(1.0, OverlapMetrics.Jaccard(Set(A, B), Set(A, B)));
    }

    [Fact]
    public void Disjoint_lists_score_zero()
    {
        Assert.Equal(0.0, OverlapMetrics.Jaccard(Set(A, B), Set(C, D)));
    }

    [Fact]
    public void Half_shared_is_the_share_of_the_union()
    {
        // Three films between them, one shared: 1/3, not 1/2. Jaccard divides by
        // the union, which is what stops a long list and a short one looking
        // similar just because the short one is contained in it.
        Assert.Equal(1.0 / 3, OverlapMetrics.Jaccard(Set(A, B), Set(B, C)), precision: 6);
    }

    [Fact]
    public void A_short_list_inside_a_long_one_is_not_a_match()
    {
        // Containment is not similarity. A recommender that gave one person two
        // films and another twelve, all of which include those two, has not given
        // them the same answer.
        Assert.Equal(2.0 / 4, OverlapMetrics.Jaccard(Set(A, B), Set(A, B, C, D)), precision: 6);
    }

    [Fact]
    public void Two_empty_lists_are_not_identical()
    {
        // Zero, not one. A recommender that returned nothing to anybody would
        // otherwise be reported as perfectly overlapping — and read as the worst
        // possible personalisation score for the wrong reason.
        Assert.Equal(0.0, OverlapMetrics.Jaccard(Set(), Set()));
    }

    [Fact]
    public void An_empty_list_shares_nothing()
    {
        Assert.Equal(0.0, OverlapMetrics.Jaccard(Set(), Set(A, B)));
    }

    // --- across cohorts ---

    [Fact]
    public void Every_pair_is_compared_once()
    {
        // Three cohorts: A|B, B|C, A|C — pairs (1/3, 1/3, 1/3). Counting a pair
        // twice, or comparing a list with itself, would both skew this.
        var overlap = OverlapMetrics.Compare([Set(A, B), Set(B, C), Set(A, C)]);

        Assert.Equal(3, overlap.Cohorts);
        Assert.Equal(1.0 / 3, overlap.MeanPairwise, precision: 6);
    }

    [Fact]
    public void A_recommender_that_ignores_taste_scores_one()
    {
        var canon = Set(A, B, C);

        var overlap = OverlapMetrics.Compare([canon, Set(A, B, C), Set(A, B, C)]);

        // The finding this tool exists to make legible: everybody got the same
        // films.
        Assert.Equal(1.0, overlap.MeanPairwise);
        Assert.Equal(3, overlap.Universal.Count);
        Assert.Equal(3, canon.Count);
    }

    [Fact]
    public void A_single_cohort_is_not_evidence()
    {
        // Zero would read as "perfectly personalised" from one list, which proves
        // nothing at all.
        var overlap = OverlapMetrics.Compare([Set(A, B)]);

        Assert.Equal(1, overlap.Cohorts);
        Assert.Empty(overlap.Universal);
    }

    [Fact]
    public void Comparing_nothing_does_not_crash()
    {
        var overlap = OverlapMetrics.Compare([]);

        Assert.Equal(0, overlap.Cohorts);
        Assert.Equal(0, overlap.MeanPairwise);
    }

    // --- universal films ---

    [Fact]
    public void Universal_films_appear_in_every_list()
    {
        var universal = OverlapMetrics.Universal([Set(A, B, C), Set(A, C, D), Set(A, C)]);

        Assert.Equal([A, C], universal.Order().ToList());
    }

    [Fact]
    public void A_film_missing_from_one_list_is_not_universal()
    {
        Assert.Empty(OverlapMetrics.Universal([Set(A, B), Set(A, C), Set(B, C)]));
    }

    [Fact]
    public void List_size_is_reported_only_when_the_lists_agree()
    {
        Assert.Equal(2, OverlapMetrics.Compare([Set(A, B), Set(C, D)]).ListSize);

        // Zero rather than a misleading one: "12 films per list" would be a lie
        // about a run where one cohort returned three.
        Assert.Equal(0, OverlapMetrics.Compare([Set(A, B), Set(C)]).ListSize);
    }

    [Fact]
    public void Shared_with_any_counts_films_someone_else_also_got()
    {
        var mine = Set(A, B, C);

        Assert.Equal(2, OverlapMetrics.SharedWithAny(mine, [mine, Set(A, D), Set(C, D)]));
    }

    private static HashSet<Guid> Set(params Guid[] films) => [.. films];
}
