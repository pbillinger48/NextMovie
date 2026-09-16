using NextMovie.Api.Domain.Recommendations;

namespace NextMovie.Api.Tests.Domain.Recommendations;

/// <summary>
/// Tests the ranking, and the honesty of its explanations.
/// </summary>
/// <remarks>
/// Two properties matter more than any individual number. Films the person is
/// likely to enjoy must outrank films they are not — which is what a
/// recommendation <em>is</em>. And every stated reason must correspond to
/// something that actually moved the score, because an explanation that does not
/// is a lie users eventually catch.
/// </remarks>
public sealed class RecommendationScorerTests
{
    private const int SciFi = 878;
    private const int Horror = 27;
    private const int Documentary = 99;
    private const int Animation = 16;

    private static readonly Dictionary<int, string> GenreNames = new()
    {
        [SciFi] = "Science Fiction",
        [Horror] = "Horror",
        [Documentary] = "Documentary",
        [Animation] = "Animation",
    };

    /// <summary>Somebody who seeks out science fiction, avoids horror, watches 2-hour modern films.</summary>
    private static TasteProfile SciFiFan(int ratedFilms = 200)
    {
        var ratings = new List<RatedFilm>();
        ratings.AddRange(Enumerable.Repeat(new RatedFilm(4.8m, [SciFi], 120, 2016), ratedFilms * 3 / 4));
        ratings.AddRange(Enumerable.Repeat(new RatedFilm(1.5m, [Horror], 95, 2016), ratedFilms / 4));

        // Watched in the same proportion they were rated: this person chooses
        // science fiction and rarely reaches for horror.
        var watched = new List<WatchedFilm>();
        watched.AddRange(Enumerable.Repeat(new WatchedFilm([SciFi]), ratedFilms * 3 / 4));
        watched.AddRange(Enumerable.Repeat(new WatchedFilm([Horror]), ratedFilms / 4));

        return TasteProfileBuilder.Build(ratings, watched);
    }

    private static readonly DateOnly Today = new(2026, 9, 16);

    private static RecommendationCandidate Candidate(
        int genre = SciFi,
        int? runtime = 120,
        int? year = 2016,
        double? community = 7.0,
        int? votes = 20_000) =>
        new(Guid.CreateVersion7(), [genre], runtime, new DateOnly(year ?? 2016, 1, 1), community, votes);

    [Fact]
    public void A_film_in_a_loved_genre_outranks_one_in_a_disliked_genre()
    {
        var profile = SciFiFan();

        var loved = RecommendationScorer.Score(Candidate(SciFi), profile, GenreNames);
        var disliked = RecommendationScorer.Score(Candidate(Horror), profile, GenreNames);

        // The whole point of the exercise.
        Assert.True(
            loved.Score > disliked.Score,
            $"sci-fi scored {loved.Score:0.000}, horror {disliked.Score:0.000}");
    }

    [Fact]
    public void A_well_reviewed_film_outranks_a_poorly_reviewed_one_in_the_same_genre()
    {
        var profile = SciFiFan();

        var good = RecommendationScorer.Score(Candidate(community: 8.5), profile, GenreNames);
        var bad = RecommendationScorer.Score(Candidate(community: 3.0), profile, GenreNames);

        // Community rating is the quality prior: liking the genre is not enough
        // to justify recommending something badly made.
        Assert.True(good.Score > bad.Score);
    }

    [Fact]
    public void How_many_people_saw_it_does_not_decide()
    {
        var profile = SciFiFan();

        var widelySeenWrongGenre = RecommendationScorer.Score(
            Candidate(Horror, community: 7.0, votes: 50_000),
            profile,
            GenreNames);

        var lessSeenRightGenre = RecommendationScorer.Score(
            Candidate(SciFi, community: 7.0, votes: 400),
            profile,
            GenreNames);

        // Vote count decides whether a rating is believed, never how good the
        // film is. Otherwise everybody gets recommended the same blockbusters.
        Assert.True(lessSeenRightGenre.Score > widelySeenWrongGenre.Score);
    }

    // --- the quality floor ---

    [Theory]
    [InlineData(6.4)]
    [InlineData(5.0)]
    [InlineData(2.0)]
    public void A_poorly_rated_film_is_not_worth_recommending(double rating)
    {
        Assert.False(RecommendationScorer.IsWorthRecommending(Candidate(community: rating), Today));
    }

    [Fact]
    public void A_well_rated_film_is_worth_recommending()
    {
        Assert.True(RecommendationScorer.IsWorthRecommending(Candidate(community: 7.6), Today));
    }

    [Fact]
    public void A_high_rating_from_a_handful_of_people_is_not_believed()
    {
        // Nine out of ten from twelve people is not evidence about a film.
        Assert.False(RecommendationScorer.IsWorthRecommending(Candidate(community: 9.0, votes: 12), Today));
    }

    [Fact]
    public void An_unknown_vote_count_is_judged_on_rating_alone()
    {
        // Most of the catalogue predates the vote count being stored; excluding
        // all of it would be worse than trusting the rating.
        Assert.True(RecommendationScorer.IsWorthRecommending(Candidate(community: 7.8, votes: null), Today));
    }

    [Fact]
    public void An_unrated_film_is_not_recommended()
    {
        Assert.False(RecommendationScorer.IsWorthRecommending(Candidate(community: null), Today));

    }

    [Fact]
    public void A_film_that_is_not_out_yet_is_never_recommended()
    {
        // The most flattering ratings on TMDb belong to films nobody has been
        // disappointed by yet — and you cannot watch them tonight regardless.
        var unreleased = new RecommendationCandidate(
            Guid.CreateVersion7(), [SciFi], 120, Today.AddMonths(3), 9.2, 20_000);

        Assert.False(RecommendationScorer.IsWorthRecommending(unreleased, Today));
    }

    [Fact]
    public void Quality_beats_taste_when_they_disagree()
    {
        var profile = SciFiFan();

        var excellentButWrongGenre = RecommendationScorer.Score(
            Candidate(Horror, community: 8.6), profile, GenreNames);

        var mediocreInTheRightGenre = RecommendationScorer.Score(
            Candidate(SciFi, community: 6.6), profile, GenreNames);

        // The correction this weighting exists for: an earlier version returned
        // films rated 6.0 from a catalogue full of films rated above 8, because
        // taste could outvote quality.
        Assert.True(
            excellentButWrongGenre.Score > mediocreInTheRightGenre.Score,
            $"8.6 in the wrong genre {excellentButWrongGenre.Score:0.000} should beat "
            + $"6.6 in the right one {mediocreInTheRightGenre.Score:0.000}");
    }

    [Fact]
    public void A_great_film_leads_with_why_it_is_great()
    {
        var recommendation = RecommendationScorer.Score(
            Candidate(SciFi, community: 8.6), SciFiFan(), GenreNames);

        // Quality mostly decides the ranking now, so an explanation opening with
        // a genre would be explaining the wrong thing.
        Assert.StartsWith("Widely considered excellent", recommendation.Reasons[0]);
    }

    [Fact]
    public void A_film_of_the_usual_length_outranks_an_outlier()
    {
        var profile = SciFiFan();

        var usual = RecommendationScorer.Score(Candidate(runtime: 120), profile, GenreNames);
        var marathon = RecommendationScorer.Score(Candidate(runtime: 240), profile, GenreNames);

        Assert.True(usual.Score > marathon.Score);
    }

    [Fact]
    public void A_film_slightly_outside_the_usual_length_is_not_disqualified()
    {
        var profile = SciFiFan();

        var slightlyLong = RecommendationScorer.Score(Candidate(runtime: 135), profile, GenreNames);
        var absurdlyLong = RecommendationScorer.Score(Candidate(runtime: 300), profile, GenreNames);

        // The preference fades rather than falling off a cliff at the edge.
        Assert.True(slightlyLong.Score > absurdlyLong.Score);
    }

    [Fact]
    public void An_unknown_genre_is_treated_as_no_opinion_rather_than_dislike()
    {
        var profile = SciFiFan();

        var unknown = RecommendationScorer.Score(Candidate(Documentary), profile, GenreNames);
        var disliked = RecommendationScorer.Score(Candidate(Horror), profile, GenreNames);

        // Never having watched documentaries is not evidence against them.
        Assert.True(unknown.Score > disliked.Score);
    }

    // --- explanations ---

    [Fact]
    public void A_recommendation_explains_the_genre_that_earned_it()
    {
        var recommendation = RecommendationScorer.Score(Candidate(SciFi), SciFiFan(), GenreNames);

        Assert.Contains(recommendation.Reasons, reason => reason.Contains("Science Fiction"));
    }

    [Fact]
    public void A_disliked_genre_is_never_offered_as_a_reason()
    {
        var recommendation = RecommendationScorer.Score(Candidate(Horror), SciFiFan(), GenreNames);

        // The reason list is built from components that were favourable. Listing
        // a genre the person dislikes would be an explanation contradicting the
        // ranking it came from.
        Assert.DoesNotContain(recommendation.Reasons, reason => reason.Contains("Horror"));
    }

    [Fact]
    public void A_film_with_nothing_to_recommend_it_claims_nothing()
    {
        var recommendation = RecommendationScorer.Score(
            Candidate(Documentary, runtime: 300, year: 1950, community: 6.6),
            SciFiFan(),
            GenreNames);

        // Better to say nothing than to pad the list until every recommendation
        // looks the same.
        Assert.Empty(recommendation.Reasons);
    }

    [Fact]
    public void Community_approval_is_only_claimed_when_it_is_real()
    {
        var wellReviewed = RecommendationScorer.Score(Candidate(community: 8.2), SciFiFan(), GenreNames);
        var mediocre = RecommendationScorer.Score(Candidate(community: 5.5), SciFiFan(), GenreNames);

        Assert.Contains(wellReviewed.Reasons, reason => reason.Contains("8.2"));
        Assert.DoesNotContain(mediocre.Reasons, reason => reason.Contains("TMDb"));
    }

    // --- confidence ---

    [Theory]
    [InlineData(400, RecommendationConfidence.High)]
    [InlineData(60, RecommendationConfidence.Medium)]
    [InlineData(10, RecommendationConfidence.Low)]
    public void Confidence_follows_how_much_history_there_is(int rated, RecommendationConfidence expected)
    {
        var recommendation = RecommendationScorer.Score(Candidate(SciFi), SciFiFan(rated), GenreNames);

        Assert.Equal(expected, recommendation.Confidence);
    }

    [Fact]
    public void Confidence_drops_for_a_genre_the_person_has_never_watched()
    {
        // Four hundred ratings say nothing useful about a documentary if none of
        // them are documentaries.
        var recommendation = RecommendationScorer.Score(Candidate(Documentary), SciFiFan(400), GenreNames);

        Assert.Equal(RecommendationConfidence.Low, recommendation.Confidence);
    }

    [Fact]
    public void A_good_film_in_a_genre_you_watch_widely_ranks_above_a_mediocre_one()
    {
        // The correction this model exists for. Somebody with a hundred animated
        // films behind them should be recommended the good ones, not told they
        // dislike animation.
        var breadthWatcher = TasteProfileBuilder.Build(
            [
                .. Enumerable.Repeat(new RatedFilm(3.2m, [Animation], 100, 2016), 90),
                .. Enumerable.Repeat(new RatedFilm(5.0m, [Animation], 100, 2016), 10),
            ],
            [.. Enumerable.Repeat(new WatchedFilm([Animation]), 100)]);

        var acclaimed = RecommendationScorer.Score(
            new RecommendationCandidate(Guid.CreateVersion7(), [Animation], 100, new DateOnly(2016, 1, 1), 8.6, 20_000),
            breadthWatcher,
            GenreNames);

        var forgettable = RecommendationScorer.Score(
            new RecommendationCandidate(Guid.CreateVersion7(), [Animation], 100, new DateOnly(2016, 1, 1), 5.2, 20_000),
            breadthWatcher,
            GenreNames);

        Assert.True(
            acclaimed.Score > forgettable.Score,
            $"acclaimed {acclaimed.Score:0.000} vs forgettable {forgettable.Score:0.000}");

        // And it is actually recommendable, not merely better than the bad one.
        Assert.True(acclaimed.Score > 0.6, $"acclaimed scored only {acclaimed.Score:0.000}");
    }

    [Fact]
    public void Quality_matters_more_inside_a_genre_you_watch_than_outside_it()
    {
        var profile = SciFiFan();

        var gainInLovedGenre =
            RecommendationScorer.Score(Candidate(SciFi, community: 8.5), profile, GenreNames).Score
            - RecommendationScorer.Score(Candidate(SciFi, community: 5.0), profile, GenreNames).Score;

        var gainInAvoidedGenre =
            RecommendationScorer.Score(Candidate(Horror, community: 8.5), profile, GenreNames).Score
            - RecommendationScorer.Score(Candidate(Horror, community: 5.0), profile, GenreNames).Score;

        // The interaction term: a great film is worth more in a genre you watch.
        Assert.True(gainInLovedGenre > gainInAvoidedGenre);
    }

    [Fact]
    public void Someone_with_no_history_gets_no_confident_recommendations()
    {
        var recommendation = RecommendationScorer.Score(
            Candidate(SciFi),
            TasteProfile.Empty,
            GenreNames);

        Assert.Equal(RecommendationConfidence.Low, recommendation.Confidence);
    }
}
