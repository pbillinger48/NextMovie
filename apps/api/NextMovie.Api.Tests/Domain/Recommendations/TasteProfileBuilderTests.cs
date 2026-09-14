using NextMovie.Api.Domain.Recommendations;

namespace NextMovie.Api.Tests.Domain.Recommendations;

/// <summary>
/// Tests what a set of ratings is taken to mean.
/// </summary>
/// <remarks>
/// The failures worth guarding against here are not crashes but plausible-looking
/// nonsense: a genre someone watched once dominating the model, or a preference
/// invented from three data points. Both produce recommendations that are wrong
/// in a way nobody can see.
/// </remarks>
public sealed class TasteProfileBuilderTests
{
    private const int SciFi = 878;
    private const int Horror = 27;
    private const int Drama = 18;

    private static RatedFilm Film(
        decimal rating,
        int genre = SciFi,
        int? runtime = 120,
        int? year = 2015) => new(rating, [genre], runtime, year);

    [Fact]
    public void An_empty_history_produces_an_empty_profile()
    {
        var profile = TasteProfileBuilder.Build([]);

        Assert.Equal(0, profile.RatedFilms);
        Assert.Empty(profile.GenreAffinity);
        Assert.Null(profile.PreferredRuntimes);
    }

    [Fact]
    public void A_genre_rated_above_the_users_own_average_has_positive_affinity()
    {
        var profile = TasteProfileBuilder.Build(
        [
            .. Enumerable.Repeat(Film(5.0m, SciFi), 20),
            .. Enumerable.Repeat(Film(2.0m, Horror), 20),
        ]);

        Assert.True(profile.GenreAffinity[SciFi] > 0);
        Assert.True(profile.GenreAffinity[Horror] < 0);
    }

    [Fact]
    public void Affinity_is_measured_against_the_persons_own_scale()
    {
        // A generous rater whose mean is 4.4 is not enthusiastic about
        // everything — they are generous. A 4.0 from them is below par.
        var profile = TasteProfileBuilder.Build(
        [
            .. Enumerable.Repeat(Film(5.0m, SciFi), 20),
            .. Enumerable.Repeat(Film(4.0m, Drama), 20),
        ]);

        Assert.True(profile.AverageRating > 4.0);
        Assert.True(profile.GenreAffinity[Drama] < 0);
    }

    [Fact]
    public void One_great_film_does_not_make_a_favourite_genre()
    {
        // A single five-star horror film against twenty middling sci-fi ones.
        // Without shrinkage, horror would come out as this person's passion.
        var profile = TasteProfileBuilder.Build(
        [
            .. Enumerable.Repeat(Film(3.0m, SciFi), 20),
            Film(5.0m, Horror),
        ]);

        Assert.True(
            profile.GenreAffinity[Horror] < 0.5,
            $"one film moved horror affinity to {profile.GenreAffinity[Horror]:0.00}");
    }

    [Fact]
    public void Weight_of_evidence_moves_a_genre_further_than_a_single_rating()
    {
        var once = TasteProfileBuilder.Build(
            [.. Enumerable.Repeat(Film(3.0m, SciFi), 20), Film(5.0m, Horror)]);

        var often = TasteProfileBuilder.Build(
            [.. Enumerable.Repeat(Film(3.0m, SciFi), 20), .. Enumerable.Repeat(Film(5.0m, Horror), 15)]);

        Assert.True(often.GenreAffinity[Horror] > once.GenreAffinity[Horror]);
    }

    [Fact]
    public void Preferences_come_from_films_they_liked_not_films_they_endured()
    {
        // Long films they disliked, short films they loved. The preference is for
        // the short ones.
        var profile = TasteProfileBuilder.Build(
        [
            .. Enumerable.Repeat(Film(1.0m, runtime: 200), 10),
            .. Enumerable.Repeat(Film(5.0m, runtime: 95), 10),
        ]);

        Assert.NotNull(profile.PreferredRuntimes);
        Assert.True(profile.PreferredRuntimes.Contains(95));
        Assert.False(profile.PreferredRuntimes.Contains(200));
    }

    [Fact]
    public void No_preference_is_stated_from_too_little_evidence()
    {
        // Three liked films is not a runtime preference. Inventing one puts a
        // confident number on noise.
        var profile = TasteProfileBuilder.Build([.. Enumerable.Repeat(Film(5.0m), 3)]);

        Assert.Null(profile.PreferredRuntimes);
        Assert.Null(profile.PreferredEra);
    }

    [Fact]
    public void Outliers_do_not_drag_the_preferred_range()
    {
        // One silent film and one four-hour epic among ordinary modern films.
        // A mean would be dragged; the middle half is not.
        var profile = TasteProfileBuilder.Build(
        [
            Film(5.0m, runtime: 40, year: 1922),
            .. Enumerable.Repeat(Film(5.0m, runtime: 110, year: 2015), 20),
            Film(5.0m, runtime: 240, year: 2024),
        ]);

        Assert.NotNull(profile.PreferredRuntimes);
        Assert.True(profile.PreferredRuntimes.Contains(110));
        Assert.False(profile.PreferredRuntimes.Contains(240));
    }

    [Fact]
    public void A_film_in_several_genres_counts_toward_each()
    {
        var profile = TasteProfileBuilder.Build(
        [
            .. Enumerable.Repeat(new RatedFilm(5.0m, [SciFi, Drama], 120, 2015), 20),
            .. Enumerable.Repeat(Film(1.0m, Horror), 20),
        ]);

        Assert.True(profile.GenreAffinity[SciFi] > 0);
        Assert.True(profile.GenreAffinity[Drama] > 0);
    }
}
