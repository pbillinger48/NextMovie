namespace NextMovie.Api.Domain.Recommendations;

/// <summary>
/// Decides which genres a person's candidate films should be fetched from.
/// </summary>
/// <remarks>
/// The most consequential decision in the engine, and the least obvious: a genre
/// no candidate is ever fetched for cannot be recommended at any score, so this
/// sets a ceiling nothing downstream can lift. Ranking can only choose among what
/// sourcing supplied.
/// <para>
/// Its own type rather than private helpers on the engine, because that ceiling
/// deserves tests of its own. Reached only through a live TMDb call and a
/// database, it was previously provable only by reading twelve film titles and
/// squinting.
/// </para>
/// </remarks>
public static class GenreSelection
{
    /// <summary>
    /// How much of someone's viewing a genre must account for before it is worth
    /// sourcing from.
    /// </summary>
    /// <remarks>
    /// Appetite is measured against the genre they watch most, so this reads "at
    /// least a tenth as much as your favourite", not "a tenth of everything".
    /// <para>
    /// Was 0.3, which turned out to be an accidental ban. On a real 786-film
    /// library it admitted eleven genres and excluded Horror (0.15), War (0.09),
    /// Music (0.08) and Western (0.04) — from a person who had said in as many
    /// words that he likes westerns and war films.
    /// </para>
    /// </remarks>
    public const double MinimumAppetite = 0.1;

    /// <summary>How many genres one request asks TMDb for its best films in.</summary>
    public const int DiscoveryGenres = 5;

    /// <summary>
    /// How many of those are kept for genres somebody likes but does not watch
    /// much of.
    /// </summary>
    /// <remarks>
    /// Without a reservation the slots spread across an affinity-ordered list,
    /// and affinity is mostly appetite — so the big genres take every slot and the
    /// pool is shaped by what somebody watches most rather than by what they
    /// enjoy most. Two of five reaches the tail without letting it run the page.
    /// </remarks>
    public const int NicheGenres = 2;

    /// <summary>The appetite above which a genre counts as one of somebody's mainstays.</summary>
    /// <remarks>
    /// Half as much viewing as their top genre. Only genres below this compete for
    /// the reserved slots, so the reservation cannot be won by the very genres it
    /// exists to make room around.
    /// </remarks>
    public const double MainstreamAppetite = 0.5;

    /// <summary>
    /// The genres somebody watches enough of to be worth sourcing from, best
    /// liked first.
    /// </summary>
    public static IReadOnlyList<int> Watched(TasteProfile profile) =>
    [
        .. profile.GenreAffinity
            .Where(entry => profile.GenreAppetite.GetValueOrDefault(entry.Key) >= MinimumAppetite)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Select(entry => entry.Key),
    ];

    /// <summary>
    /// Picks evenly spaced entries rather than the first few.
    /// </summary>
    /// <remarks>
    /// Taking from the front would ask for the best dramas five times over. Used
    /// for seed films as well as discovery, so the two cannot drift apart.
    /// </remarks>
    public static IReadOnlyList<int> Spread(IReadOnlyList<int> ordered, int slots)
    {
        if (ordered.Count == 0 || slots <= 0)
        {
            return [];
        }

        var picked = new List<int>(slots);

        for (var slot = 0; slot < slots; slot++)
        {
            var genreId = ordered[ordered.Count * slot / slots];

            // Spacing collapses onto repeats when there are fewer genres than
            // slots. A duplicate would spend a whole upstream request re-asking a
            // question already answered.
            if (!picked.Contains(genreId))
            {
                picked.Add(genreId);
            }
        }

        return picked;
    }

    /// <summary>
    /// The genres to ask TMDb for its best films in.
    /// </summary>
    /// <remarks>
    /// Most slots spread across what they watch; the last two are held for genres
    /// they like but watch little of.
    /// </remarks>
    public static IReadOnlyList<int> ForDiscovery(TasteProfile profile)
    {
        var chosen = Spread(Watched(profile), DiscoveryGenres - NicheGenres).ToList();

        // Ranked by upside, not affinity, and deliberately. Upside asks how
        // reliably a genre produces a film they love — the only signal that can
        // tell a genre somebody watches rarely and adores from one they watch
        // rarely and merely tolerate. Affinity cannot: it is mostly appetite, so
        // ranking by it would hand these slots straight back to the genres they
        // are being held away from.
        //
        // No appetite floor here, on purpose. Upside is shrunk toward the
        // person's own average by eight notional ratings, so a genre with almost
        // no history lands mid-table rather than on top — the evidence gate is
        // already inside the number.
        var niche = profile.GenreUpside
            .Where(entry => !chosen.Contains(entry.Key))
            .Where(entry => profile.GenreAppetite.GetValueOrDefault(entry.Key) < MainstreamAppetite)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Select(entry => entry.Key)
            .Take(NicheGenres);

        chosen.AddRange(niche);

        return chosen;
    }
}
