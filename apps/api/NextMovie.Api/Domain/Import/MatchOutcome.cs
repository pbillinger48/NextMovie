namespace NextMovie.Api.Domain.Import;

/// <summary>How a Letterboxd row was resolved to a film, if it was.</summary>
/// <remarks>
/// Persisted with each imported row so a bad match is auditable later instead of
/// being indistinguishable from a good one — a requirement the spike arrived at
/// after finding that heuristics which explain away failures can hide real bugs.
/// </remarks>
public enum MatchMethod
{
    /// <summary>Title and year both matched.</summary>
    Exact = 1,

    /// <summary>The title matched as a prefix of a longer, subtitled TMDb title.</summary>
    Subtitle = 2,

    /// <summary>Several films shared the title and year; TMDb vote count separated them.</summary>
    VoteCount = 3,

    /// <summary>A person chose, during reconciliation.</summary>
    Manual = 4,
}

/// <summary>The result of matching one Letterboxd row against TMDb candidates.</summary>
public abstract record MatchOutcome
{
    private MatchOutcome()
    {
    }

    /// <summary>One film was resolved confidently.</summary>
    /// <param name="TmdbId">The film chosen.</param>
    /// <param name="Method">How it was chosen. Recorded, not discarded.</param>
    public sealed record Matched(int TmdbId, MatchMethod Method) : MatchOutcome;

    /// <summary>
    /// Several plausible films, and nothing separates them well enough to guess.
    /// </summary>
    /// <remarks>
    /// This is the outcome that goes to reconciliation. A tiebreak that silently
    /// picks the wrong film is worse than a visible failure, so the bar for
    /// choosing is deliberately high and this outcome is deliberately common
    /// enough to be worth a screen.
    /// </remarks>
    /// <param name="Candidates">What was on the table, so a person can choose.</param>
    public sealed record Ambiguous(IReadOnlyList<MatchCandidate> Candidates) : MatchOutcome;

    /// <summary>
    /// Nothing plausible at all.
    /// </summary>
    /// <remarks>
    /// Note what this does <b>not</b> claim: that the row is television. The spike
    /// found that a "must be TV then" fallback running only after a film match
    /// failed mislabelled a real film. Deciding something is not a film requires
    /// positive evidence, which is a separate lookup, not the absence of a match.
    /// </remarks>
    public sealed record Unresolved : MatchOutcome;
}
