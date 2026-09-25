using NextMovie.Api.Domain.Recommendations;

namespace NextMovie.Api.Features.Recommendations;

/// <summary>
/// What the engine did on the way to an answer.
/// </summary>
/// <remarks>
/// Filled in only when a caller asks for it, and ignored entirely otherwise —
/// the engine does not reason about it and no behaviour depends on it.
/// <para>
/// It exists because the two failures that matter most look identical from
/// outside: a film that was never fetched and a film that was fetched and
/// outranked both come back as "not recommended". The first is a sourcing ceiling
/// nothing downstream can lift; the second is a ranking decision that a weight
/// could change. Telling them apart by reading twelve titles is guesswork, and
/// guessing wrong costs a branch (ADR-0012).
/// </para>
/// </remarks>
internal sealed class RecommendationTrace
{
    /// <summary>Genres TMDb was asked for its best films in.</summary>
    public List<int> DiscoveryGenres { get; } = [];

    /// <summary>Films whose relatives were fetched.</summary>
    public List<int> SeedTmdbIds { get; } = [];

    /// <summary>Distinct candidates returned by both sources, before any filtering.</summary>
    public int Fetched { get; set; }

    /// <summary>Candidates left after removing what the user has seen or answered about.</summary>
    public int Unseen { get; set; }

    /// <summary>
    /// Every candidate that survived the quality floor, with what it scored.
    /// </summary>
    /// <remarks>
    /// The pool that actually competed. Comparing it against the final list is
    /// what separates "never sourced" from "sourced and beaten", and the score
    /// components then say <em>why</em> it was beaten — which is the difference
    /// between knowing a weight is wrong and guessing which one.
    /// </remarks>
    public List<ContendingFilm> Contending { get; } = [];
}

/// <summary>One film that competed for a slot, and how it fared.</summary>
/// <param name="MovieId">The film.</param>
/// <param name="Title">
/// Carried here rather than looked up later. A caller evaluating a hypothetical
/// reader rolls its whole run back, so by the time anything reads this the film's
/// row may no longer exist.
/// </param>
/// <param name="GenreIds">Its genres, for asking whether a taste was represented.</param>
/// <param name="Score">Its final score, before any availability adjustment.</param>
/// <param name="Breakdown">What each signal contributed, before weighting.</param>
internal sealed record ContendingFilm(
    Guid MovieId,
    string Title,
    IReadOnlyList<int> GenreIds,
    double Score,
    ScoreBreakdown? Breakdown);
