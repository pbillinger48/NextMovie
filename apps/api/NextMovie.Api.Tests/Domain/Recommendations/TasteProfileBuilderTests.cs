using NextMovie.Api.Domain.Recommendations;

namespace NextMovie.Api.Tests.Domain.Recommendations;

/// <summary>
/// Tests what a viewing history is taken to mean.
/// </summary>
/// <remarks>
/// The failures worth guarding against are not crashes but plausible-looking
/// nonsense. The first version of this model concluded, from a real library, that
/// somebody who had watched 97 animated films and 11 westerns disliked animation —
/// it was measuring how they scored films rather than what they wanted to watch.
/// Several tests below exist specifically to keep that from coming back.
/// </remarks>
public sealed class TasteProfileBuilderTests
{
    private const int SciFi = 878;
    private const int Horror = 27;
    private const int Animation = 16;
    private const int Western = 37;

    private static RatedFilm Rated(
        decimal rating,
        int genre = SciFi,
        int? runtime = 120,
        int? year = 2015) => new(rating, [genre], runtime, year);

    private static IEnumerable<WatchedFilm> Watched(int genre, int count) =>
        Enumerable.Repeat(new WatchedFilm([genre]), count);

    [Fact]
    public void No_history_produces_an_empty_profile()
    {
        var profile = TasteProfileBuilder.Build([], []);

        Assert.Equal(0, profile.RatedFilms);
        Assert.Empty(profile.GenreAffinity);
        Assert.Null(profile.PreferredRuntimes);
    }

    [Fact]
    public void A_genre_watched_far_more_outranks_one_watched_rarely()
    {
        // The case that broke the first model. Ninety-seven animated films
        // against eleven westerns, with animation scoring lower on average —
        // watching it nine times as often is the stronger statement.
        var ratings = new List<RatedFilm>();
        ratings.AddRange(Enumerable.Repeat(Rated(3.3m, Animation), 97));
        ratings.AddRange(Enumerable.Repeat(Rated(4.2m, Western), 11));

        var profile = TasteProfileBuilder.Build(
            ratings,
            [.. Watched(Animation, 97), .. Watched(Western, 11)]);

        Assert.True(
            profile.GenreAffinity[Animation] > TasteProfile.NoOpinion,
            $"animation affinity was {profile.GenreAffinity[Animation]:0.00}");
    }

    [Fact]
    public void Watching_a_genre_widely_is_not_held_against_it()
    {
        // One person, both genres — appetite is relative to their own viewing, so
        // comparing across two people would mean nothing.
        //
        // They have explored animation broadly and accumulated mediocre entries,
        // and seen only the canon of westerns. The first model read that as
        // "dislikes animation, loves westerns"; the truth is they watch a great
        // deal of animation and dip into westerns.
        var profile = TasteProfileBuilder.Build(
            [
                .. Enumerable.Repeat(Rated(3.2m, Animation), 90),
                .. Enumerable.Repeat(Rated(5.0m, Animation), 10),
                .. Enumerable.Repeat(Rated(4.5m, Western), 6),
            ],
            [.. Watched(Animation, 100), .. Watched(Western, 6)]);

        Assert.True(
            profile.GenreAffinity[Animation] > profile.GenreAffinity[Western],
            $"animation {profile.GenreAffinity[Animation]:0.00} should outrank "
            + $"western {profile.GenreAffinity[Western]:0.00}");

        // Both are still live: dipping into westerns is not evidence against them.
        Assert.True(profile.GenreAffinity[Western] > TasteProfile.NoOpinion);
    }

    [Fact]
    public void Ratings_still_separate_two_genres_watched_equally()
    {
        // Appetite leads, but it does not decide alone: given the same amount of
        // viewing, the genre whose films land should win.
        var profile = TasteProfileBuilder.Build(
        [
            .. Enumerable.Repeat(Rated(5.0m, SciFi), 30),
            .. Enumerable.Repeat(Rated(2.0m, Horror), 30),
        ],
            [.. Watched(SciFi, 30), .. Watched(Horror, 30)]);

        Assert.True(profile.GenreAffinity[SciFi] > profile.GenreAffinity[Horror]);
    }

    [Fact]
    public void Upside_is_measured_against_how_freely_this_person_loves_anything()
    {
        // Someone who loves half of everything is not enthusiastic about a genre
        // just because they loved half of it.
        var generous = TasteProfileBuilder.Build(
        [
            .. Enumerable.Repeat(Rated(5.0m, SciFi), 20),
            .. Enumerable.Repeat(Rated(5.0m, Horror), 20),
        ],
            [.. Watched(SciFi, 20), .. Watched(Horror, 20)]);

        Assert.Equal(generous.GenreUpside[SciFi], generous.GenreUpside[Horror], 3);
    }

    [Fact]
    public void One_loved_film_does_not_make_a_favourite_genre()
    {
        var profile = TasteProfileBuilder.Build(
            [.. Enumerable.Repeat(Rated(3.0m, SciFi), 40), Rated(5.0m, Horror)],
            [.. Watched(SciFi, 40), .. Watched(Horror, 1)]);

        Assert.True(
            profile.GenreAffinity[Horror] < profile.GenreAffinity[SciFi],
            "a single five-star film should not outrank forty films of sustained viewing");
    }

    [Fact]
    public void An_unwatched_genre_is_no_opinion_rather_than_dislike()
    {
        var profile = TasteProfileBuilder.Build(
            [.. Enumerable.Repeat(Rated(4.0m, SciFi), 20)],
            [.. Watched(SciFi, 20)]);

        Assert.Equal(TasteProfile.NoOpinion, profile.AffinityFor(Western));
    }

    [Fact]
    public void Viewing_counts_even_when_nothing_was_rated()
    {
        // Most of a real library is unrated. A model that ignores it ignores most
        // of what it was told.
        var profile = TasteProfileBuilder.Build([], [.. Watched(Animation, 50), .. Watched(Horror, 2)]);

        Assert.True(profile.GenreAffinity[Animation] > profile.GenreAffinity[Horror]);
    }

    [Fact]
    public void Preferences_come_from_films_they_liked_not_films_they_endured()
    {
        var profile = TasteProfileBuilder.Build(
        [
            .. Enumerable.Repeat(Rated(1.0m, runtime: 200), 10),
            .. Enumerable.Repeat(Rated(5.0m, runtime: 95), 10),
        ],
            [.. Watched(SciFi, 20)]);

        Assert.NotNull(profile.PreferredRuntimes);
        Assert.True(profile.PreferredRuntimes.Contains(95));
        Assert.False(profile.PreferredRuntimes.Contains(200));
    }

    [Fact]
    public void No_preference_is_stated_from_too_little_evidence()
    {
        var profile = TasteProfileBuilder.Build(
            [.. Enumerable.Repeat(Rated(5.0m), 3)],
            [.. Watched(SciFi, 3)]);

        Assert.Null(profile.PreferredRuntimes);
        Assert.Null(profile.PreferredEra);
    }

    [Fact]
    public void Outliers_do_not_drag_the_preferred_range()
    {
        var profile = TasteProfileBuilder.Build(
        [
            Rated(5.0m, runtime: 40, year: 1922),
            .. Enumerable.Repeat(Rated(5.0m, runtime: 110, year: 2015), 20),
            Rated(5.0m, runtime: 240, year: 2024),
        ],
            [.. Watched(SciFi, 22)]);

        Assert.NotNull(profile.PreferredRuntimes);
        Assert.True(profile.PreferredRuntimes.Contains(110));
        Assert.False(profile.PreferredRuntimes.Contains(240));
    }
}
