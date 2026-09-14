namespace NextMovie.Api.Domain.Import;

/// <summary>
/// One row of an export, and what became of it.
/// </summary>
/// <remarks>
/// Carries both what Letterboxd said and how we resolved it — the spike's
/// requirement that a match be auditable later rather than indistinguishable from
/// a good one. It is also what makes a job resumable: an item that is already
/// resolved is skipped rather than redone when a worker picks the job back up.
/// </remarks>
public class ImportItem
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid ImportJobId { get; init; }

    public ImportJob ImportJob { get; init; } = null!;

    /// <summary>Title exactly as the export gave it, kept for reconciliation.</summary>
    public required string Name { get; init; }

    public int? Year { get; init; }

    /// <summary>
    /// Letterboxd's URI for the film.
    /// </summary>
    /// <remarks>
    /// Stable across exports, unlike the diary export's per-viewing URI, which is
    /// what makes a re-import idempotent rather than duplicating everything.
    /// </remarks>
    public string? FilmUri { get; init; }

    /// <summary>The rating from the export, on the shared 0.5–5.0 scale.</summary>
    public decimal? Rating { get; init; }

    public DateOnly? WatchedOn { get; init; }

    public ImportItemStatus Status { get; set; } = ImportItemStatus.Pending;

    /// <summary>The film this row resolved to, once it has.</summary>
    public Guid? MatchedMovieId { get; set; }

    public Movie? MatchedMovie { get; set; }

    /// <summary>How it resolved. Recorded so a bad match can be traced.</summary>
    public MatchMethod? MatchMethod { get; set; }

    /// <summary>
    /// TMDb ids that were plausible but not decisive.
    /// </summary>
    /// <remarks>
    /// Kept so the reconciliation screen can offer the same choice the matcher
    /// declined to make, without searching TMDb again — and so a later reviewer
    /// can see what the matcher was actually looking at.
    /// </remarks>
    public int[] CandidateTmdbIds { get; set; } = [];

    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>What became of one export row.</summary>
public enum ImportItemStatus
{
    /// <summary>Not yet looked at.</summary>
    Pending = 1,

    /// <summary>Resolved to a film and applied.</summary>
    Matched = 2,

    /// <summary>Several plausible films; a person must choose.</summary>
    Ambiguous = 3,

    /// <summary>Nothing plausible was found.</summary>
    Unresolved = 4,
}
