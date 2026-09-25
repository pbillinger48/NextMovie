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
    /// The genres of every candidate that survived the quality floor, one entry
    /// per film.
    /// </summary>
    /// <remarks>
    /// The pool that actually competed. Comparing it against the final list is
    /// what separates "never sourced" from "sourced and beaten".
    /// </remarks>
    public List<IReadOnlyList<int>> Contending { get; } = [];
}
