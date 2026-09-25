using NextMovie.Api.Domain.Recommendations;

namespace NextMovie.Api.Tests.Domain.Recommendations;

/// <summary>
/// Tests the ceiling on what can ever be recommended.
/// </summary>
/// <remarks>
/// A genre no candidate is fetched from cannot be recommended at any score, so a
/// mistake here is invisible downstream: the ranking looks reasonable, the tests
/// pass, and a whole kind of film silently does not exist. That is precisely what
/// happened — a real library's owner said he liked westerns and war films while
/// an appetite threshold of 0.3 made both unreachable.
/// </remarks>
public sealed class GenreSelectionTests
{
    private const int Drama = 18;
    private const int Comedy = 35;
    private const int Animation = 16;
    private const int Horror = 27;
    private const int War = 10752;
    private const int Western = 37;

    // Stand-ins, so the crowded profile below reads as a shape rather than as a
    // claim about any particular genre.
    private const int Mainstays = 9;

    /// <summary>The mainstay with the best upside in the whole profile.</summary>
    private const int BestLikedMainstay = 902;

    /// <summary>Watched most of the three niche genres, and liked least of them.</summary>
    private const int Tolerated = 911;

    private const int Adored = 912;
    private const int AlsoAdored = 913;

    // --- who is eligible at all ---

    [Fact]
    public void A_genre_watched_a_tenth_as_much_as_the_favourite_is_eligible()
    {
        // The real numbers that motivated the change: Western sat at 0.04 of
        // Drama's viewing and was excluded outright.
        var profile = Profile(
            appetite: new() { [Drama] = 1.0, [War] = 0.10, [Western] = 0.04 },
            upside: new() { [Drama] = 0.5, [War] = 0.5, [Western] = 0.5 });

        var watched = GenreSelection.Watched(profile);

        Assert.Contains(War, watched);
        Assert.DoesNotContain(Western, watched);
    }

    [Fact]
    public void Eligible_genres_come_back_best_liked_first()
    {
        var profile = Profile(
            appetite: new() { [Drama] = 1.0, [Comedy] = 0.8, [Animation] = 0.5 },
            upside: new() { [Drama] = 0.2, [Comedy] = 0.9, [Animation] = 0.5 },
            affinity: new() { [Drama] = 0.3, [Comedy] = 0.9, [Animation] = 0.6 });

        Assert.Equal([Comedy, Animation, Drama], GenreSelection.Watched(profile));
    }

    // --- spreading ---

    [Fact]
    public void Spreading_takes_from_across_the_list_not_the_front()
    {
        // Taking the front would ask for the best dramas five times over.
        Assert.Equal([1, 4, 7], GenreSelection.Spread([1, 2, 3, 4, 5, 6, 7, 8, 9], 3));
    }

    [Fact]
    public void Spreading_never_asks_the_same_question_twice()
    {
        // With fewer genres than slots the spacing collapses onto repeats, and a
        // duplicate would spend a whole upstream request re-asking something
        // already answered.
        var picked = GenreSelection.Spread([Drama, Comedy], 5);

        Assert.Equal(picked.Count, picked.Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Spreading_into_no_slots_picks_nothing(int slots)
    {
        Assert.Empty(GenreSelection.Spread([Drama, Comedy], slots));
    }

    [Fact]
    public void Spreading_an_empty_list_picks_nothing()
    {
        Assert.Empty(GenreSelection.Spread([], 5));
    }

    // --- the reserved slots ---

    /// <summary>
    /// Nine mainstays and three genres barely watched.
    /// </summary>
    /// <remarks>
    /// Deliberately larger than it looks like it needs to be, for two reasons
    /// found by breaking the code and watching these tests pass anyway.
    /// <para>
    /// <b>Nine mainstays</b>, so the evenly spaced mainstream slots land on
    /// indices 0, 4 and 8 and take only mainstays. With a shorter list they
    /// consume the niche genres first, and the reservation is never exercised at
    /// all.
    /// </para>
    /// <para>
    /// <b>Three niche genres</b>, ordered one way by appetite and the other by
    /// upside, so that picking two of them distinguishes the rules. With only two
    /// candidates for two slots both are chosen however they are ranked, and a
    /// reservation ranked on entirely the wrong signal still passes.
    /// </para>
    /// </remarks>
    private static TasteProfile CrowdedProfile()
    {
        var appetite = new Dictionary<int, double>();
        var upside = new Dictionary<int, double>();

        for (var index = 0; index < Mainstays; index++)
        {
            var genreId = 901 + index;

            // 1.00 down to 0.60 — all comfortably above the mainstream line.
            appetite[genreId] = 1.0 - (index * 0.05);
            upside[genreId] = genreId == BestLikedMainstay ? 0.95 : 0.30;
        }

        // Watched most, liked least. Affinity ranks it above the other two;
        // upside ranks it below both.
        appetite[Tolerated] = 0.30;
        upside[Tolerated] = 0.40;

        appetite[Adored] = 0.20;
        upside[Adored] = 0.90;

        appetite[AlsoAdored] = 0.15;
        upside[AlsoAdored] = 0.85;

        return Profile(appetite, upside, affinity: appetite);
    }

    private static IReadOnlyList<int> Reserved(TasteProfile profile)
    {
        var chosen = GenreSelection.ForDiscovery(profile);

        return [.. chosen.Skip(Math.Min(chosen.Count, GenreSelection.DiscoveryGenres - GenreSelection.NicheGenres))];
    }

    [Fact]
    public void Genres_they_love_but_rarely_watch_reach_the_pool()
    {
        // Both reserved slots go to the barely-watched genres, which affinity
        // would have buried under nine mainstays.
        Assert.Equal([Adored, AlsoAdored], Reserved(CrowdedProfile()));
    }

    [Fact]
    public void A_genre_watched_rarely_and_merely_tolerated_does_not_win_a_slot()
    {
        // The distinction the reservation exists to draw, and the one only upside
        // can see: this genre is watched more than either of the others and liked
        // less than both. Ranking on affinity would pick it first.
        Assert.DoesNotContain(Tolerated, Reserved(CrowdedProfile()));
    }

    [Fact]
    public void A_mainstay_cannot_win_a_reserved_slot()
    {
        // Mainstay2 has the best upside in the whole profile and is not among the
        // genres the mainstream slots happened to take. It must still lose: the
        // reservation exists to make room around the mainstays, so a mainstay
        // winning one defeats the entire mechanism.
        Assert.DoesNotContain(BestLikedMainstay, Reserved(CrowdedProfile()));
    }

    [Fact]
    public void The_reserved_slots_rank_on_upside_not_appetite()
    {
        var profile = CrowdedProfile();
        var chosen = GenreSelection.ForDiscovery(profile);

        // Every remaining mainstay outranks both niche genres on appetite and on
        // affinity. If the reservation ranked on either, none of these would be
        // here at all.
        Assert.All(
            Reserved(profile),
            genreId => Assert.True(profile.GenreAppetite[genreId] < GenreSelection.MainstreamAppetite));

        Assert.Equal(GenreSelection.DiscoveryGenres, chosen.Count);
    }

    [Fact]
    public void Discovery_asks_about_at_most_five_genres()
    {
        var appetite = Enumerable.Range(1, 30).ToDictionary(id => id, id => 1.0 - (id * 0.03));
        var upside = Enumerable.Range(1, 30).ToDictionary(id => id, id => id * 0.01);

        var chosen = GenreSelection.ForDiscovery(Profile(appetite, upside, appetite));

        // Each genre is an upstream call on the recommendation path.
        Assert.Equal(GenreSelection.DiscoveryGenres, chosen.Count);
        Assert.Equal(chosen.Count, chosen.Distinct().Count());
    }

    [Fact]
    public void A_genre_already_chosen_is_not_chosen_again_for_a_reserved_slot()
    {
        var profile = Profile(
            appetite: new() { [Drama] = 1.0, [Animation] = 0.3 },
            upside: new() { [Drama] = 0.3, [Animation] = 0.9 },
            affinity: new() { [Drama] = 0.8, [Animation] = 0.4 });

        var chosen = GenreSelection.ForDiscovery(profile);

        Assert.Equal(chosen.Count, chosen.Distinct().Count());
    }

    [Fact]
    public void Somebody_with_no_history_is_asked_about_nothing()
    {
        Assert.Empty(GenreSelection.ForDiscovery(Profile(new(), new())));
    }

    private static TasteProfile Profile(
        Dictionary<int, double> appetite,
        Dictionary<int, double> upside,
        Dictionary<int, double>? affinity = null) => new(
        GenreAffinity: affinity ?? appetite,
        GenreAppetite: appetite,
        GenreUpside: upside,
        AverageRating: 3.5,
        RatedFilms: 100,
        PreferredRuntimes: null,
        PreferredEra: null);
}
