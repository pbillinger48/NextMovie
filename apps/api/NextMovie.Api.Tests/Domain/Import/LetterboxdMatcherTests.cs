using NextMovie.Api.Domain.Import;

namespace NextMovie.Api.Tests.Domain.Import;

/// <summary>
/// Tests the rules that decide which film a Letterboxd row means.
/// </summary>
/// <remarks>
/// Driven by the real examples in <c>docs/spikes/letterboxd-tmdb-matching.md</c>,
/// including the margins the spike found genuinely too close to call. The
/// asymmetry these rules are built around is worth restating: a wrong match is
/// invisible, permanent, and poisons the recommendation engine, while an
/// unresolved row is one line on a reconciliation screen. Every test below is
/// really asking "would this guess?"
/// </remarks>
public sealed class LetterboxdMatcherTests
{
    private static LetterboxdEntry Entry(string name, int? year = 2010) =>
        new(name, year, "https://boxd.it/abc", null, null);

    private static MatchCandidate Candidate(
        int tmdbId,
        string title,
        int? year = 2010,
        int votes = 1000) => new(tmdbId, title, year, votes);

    [Fact]
    public void Matches_an_exact_title_and_year()
    {
        var outcome = LetterboxdMatcher.Match(Entry("Inception"), [Candidate(27205, "Inception")]);

        var matched = Assert.IsType<MatchOutcome.Matched>(outcome);
        Assert.Equal(27205, matched.TmdbId);
        Assert.Equal(MatchMethod.Exact, matched.Method);
    }

    [Fact]
    public void Reports_nothing_when_there_are_no_candidates()
    {
        Assert.IsType<MatchOutcome.Unresolved>(LetterboxdMatcher.Match(Entry("Inception"), []));
    }

    [Fact]
    public void Does_not_claim_an_unmatched_row_is_television()
    {
        // The spike's own "is it TV then?" fallback ran only after a film match
        // failed, and mislabelled Hamilton — a real film — as television.
        // Deciding something is not a film needs positive evidence from a
        // separate lookup, so the only thing reported here is "unresolved".
        var outcome = LetterboxdMatcher.Match(Entry("Chernobyl"), []);

        Assert.IsType<MatchOutcome.Unresolved>(outcome);
    }

    // --- year tolerance ---

    [Theory]
    [InlineData(2014)]
    [InlineData(2015)]
    [InlineData(2016)]
    public void Tolerates_a_year_off_by_one(int tmdbYear)
    {
        // Letterboxd records the festival premiere, TMDb the wide release —
        // Kingsman is 2014 in one and 2015 in the other. 16 films in 796 were off
        // by exactly one, so this tolerance is required rather than generous.
        var outcome = LetterboxdMatcher.Match(
            Entry("Kingsman", year: 2015),
            [Candidate(207703, "Kingsman", year: tmdbYear)]);

        Assert.IsType<MatchOutcome.Matched>(outcome);
    }

    [Fact]
    public void Does_not_reach_past_the_tolerance()
    {
        // TMDb release dates move: Hamilton is logged as 2020 and TMDb now says
        // 2025 after a re-release. A wide year window would silently mis-key
        // films, so a distant year goes to a person instead of being guessed.
        var outcome = LetterboxdMatcher.Match(
            Entry("Hamilton", year: 2020),
            [Candidate(556574, "Hamilton", year: 2025)]);

        var ambiguous = Assert.IsType<MatchOutcome.Ambiguous>(outcome);
        Assert.Single(ambiguous.Candidates);
    }

    [Fact]
    public void Matches_on_title_alone_when_the_export_has_no_year()
    {
        var outcome = LetterboxdMatcher.Match(
            Entry("Inception", year: null),
            [Candidate(27205, "Inception", year: 2010)]);

        Assert.IsType<MatchOutcome.Matched>(outcome);
    }

    // --- the vote count tiebreak ---

    [Fact]
    public void Separates_a_real_film_from_its_obscure_namesake()
    {
        // Justice League 2017: 13,905 votes against 1,161. TMDb genuinely carries
        // shorts and student films sharing a title and year.
        var outcome = LetterboxdMatcher.Match(
            Entry("Justice League", year: 2017),
            [
                Candidate(1, "Justice League", year: 2017, votes: 1_161),
                Candidate(141052, "Justice League", year: 2017, votes: 13_905),
            ]);

        var matched = Assert.IsType<MatchOutcome.Matched>(outcome);
        Assert.Equal(141052, matched.TmdbId);
        Assert.Equal(MatchMethod.VoteCount, matched.Method);
    }

    [Fact]
    public void Refuses_to_guess_when_the_margin_is_narrow()
    {
        // "How to Make a Killing" at 720 against 399 — under 2:1. The spike
        // inspected these by hand and concluded they are genuinely too close, so
        // they go to reconciliation rather than to a coin flip.
        var outcome = LetterboxdMatcher.Match(
            Entry("How to Make a Killing"),
            [
                Candidate(1, "How to Make a Killing", votes: 720),
                Candidate(2, "How to Make a Killing", votes: 399),
            ]);

        var ambiguous = Assert.IsType<MatchOutcome.Ambiguous>(outcome);

        // Ordered by votes, so a person sees the likeliest first.
        Assert.Equal(2, ambiguous.Candidates.Count);
        Assert.Equal(720, ambiguous.Candidates[0].VoteCount);
    }

    [Fact]
    public void Refuses_to_guess_when_both_films_are_obscure()
    {
        // 40 votes against 1 is a 40:1 ratio, but 40 votes is not evidence of
        // anything. Both thresholds have to be met.
        var outcome = LetterboxdMatcher.Match(
            Entry("Student Film"),
            [
                Candidate(1, "Student Film", votes: 40),
                Candidate(2, "Student Film", votes: 1),
            ]);

        Assert.IsType<MatchOutcome.Ambiguous>(outcome);
    }

    [Fact]
    public void Takes_the_leader_at_exactly_the_threshold()
    {
        // Boundary: 500 votes is 10x 50, and 500 clears the minimum.
        var outcome = LetterboxdMatcher.Match(
            Entry("Borderline"),
            [
                Candidate(1, "Borderline", votes: 500),
                Candidate(2, "Borderline", votes: 50),
            ]);

        Assert.IsType<MatchOutcome.Matched>(outcome);
    }

    [Fact]
    public void Refuses_one_vote_short_of_the_threshold()
    {
        var outcome = LetterboxdMatcher.Match(
            Entry("Borderline"),
            [
                Candidate(1, "Borderline", votes: 499),
                Candidate(2, "Borderline", votes: 50),
            ]);

        Assert.IsType<MatchOutcome.Ambiguous>(outcome);
    }

    [Fact]
    public void Years_narrow_the_field_before_votes_decide()
    {
        // The popular film is the wrong year; the right year has one candidate.
        // Year is the stronger signal and must be applied first.
        var outcome = LetterboxdMatcher.Match(
            Entry("Hercules", year: 1997),
            [
                Candidate(1, "Hercules", year: 2014, votes: 50_000),
                Candidate(11970, "Hercules", year: 1997, votes: 3_000),
            ]);

        var matched = Assert.IsType<MatchOutcome.Matched>(outcome);
        Assert.Equal(11970, matched.TmdbId);
        Assert.Equal(MatchMethod.Exact, matched.Method);
    }

    // --- title normalisation ---

    [Theory]
    [InlineData("inception")]
    [InlineData("INCEPTION")]
    [InlineData("  Inception  ")]
    public void Ignores_case_and_surrounding_space(string exported)
    {
        Assert.IsType<MatchOutcome.Matched>(
            LetterboxdMatcher.Match(Entry(exported), [Candidate(27205, "Inception")]));
    }

    [Fact]
    public void Folds_a_trailing_article()
    {
        // Catalogues that sort by title store "Matrix, The". Folding recovers a
        // handful of films per library.
        Assert.IsType<MatchOutcome.Matched>(
            LetterboxdMatcher.Match(Entry("Matrix, The", year: 1999), [Candidate(603, "The Matrix", year: 1999)]));
    }

    [Fact]
    public void Ignores_accents()
    {
        Assert.IsType<MatchOutcome.Matched>(
            LetterboxdMatcher.Match(Entry("Amelie", year: 2001), [Candidate(194, "Amélie", year: 2001)]));
    }

    [Fact]
    public void Matches_a_title_that_tmdb_carries_with_a_subtitle()
    {
        var outcome = LetterboxdMatcher.Match(
            Entry("Mad Max", year: 2015),
            [Candidate(76341, "Mad Max: Fury Road", year: 2015)]);

        var matched = Assert.IsType<MatchOutcome.Matched>(outcome);
        Assert.Equal(MatchMethod.Subtitle, matched.Method);
    }

    [Fact]
    public void Prefers_the_exactly_titled_film_over_a_subtitled_one()
    {
        // "Alien" must not become "Alien: Resurrection" while a film actually
        // called Alien is sitting in the same list, however many votes the other
        // one has.
        var outcome = LetterboxdMatcher.Match(
            Entry("Alien", year: 1979),
            [
                Candidate(8078, "Alien: Resurrection", year: 1979, votes: 90_000),
                Candidate(348, "Alien", year: 1979, votes: 12_000),
            ]);

        var matched = Assert.IsType<MatchOutcome.Matched>(outcome);
        Assert.Equal(348, matched.TmdbId);
        Assert.Equal(MatchMethod.Exact, matched.Method);
    }

    [Fact]
    public void Does_not_match_an_unrelated_film_that_merely_starts_the_same()
    {
        // "Up" must not match "Up in the Air": the subtitle rule requires a
        // colon, not any longer title beginning with the same word.
        var outcome = LetterboxdMatcher.Match(
            Entry("Up", year: 2009),
            [Candidate(25195, "Up in the Air", year: 2009)]);

        Assert.IsType<MatchOutcome.Unresolved>(outcome);
    }
}
