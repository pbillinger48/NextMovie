namespace NextMovie.Api.Domain.Import;

/// <summary>
/// One Letterboxd upload, and how far through it we are.
/// </summary>
/// <remarks>
/// Both the record the status endpoint reads and the queue the worker claims from
/// (ADR-0007). It is one table because the status endpoint needed it regardless,
/// which is what made a database-backed queue nearly free.
/// </remarks>
public class ImportJob
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public ImportJobStatus Status { get; set; } = ImportJobStatus.Pending;

    /// <summary>Rows parsed out of the export.</summary>
    public required int TotalItems { get; init; }

    /// <summary>
    /// Rows the export contained but that carried no title, so could not be
    /// matched.
    /// </summary>
    /// <remarks>
    /// Reported rather than quietly dropped: a user who exported 800 films and
    /// imported 790 should be told which number is which.
    /// </remarks>
    public required int SkippedRows { get; init; }

    public int MatchedItems { get; set; }

    /// <summary>Rows that need a person to choose between candidates.</summary>
    public int AmbiguousItems { get; set; }

    /// <summary>Rows nothing plausible was found for.</summary>
    public int UnresolvedItems { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When a worker last claimed this job.
    /// </summary>
    /// <remarks>
    /// The basis for reclaiming a job whose worker died: a claim old enough to be
    /// implausible is stale, not in progress. Without this a deploy mid-import
    /// would strand the job on <see cref="ImportJobStatus.Running"/> forever.
    /// </remarks>
    public DateTimeOffset? ClaimedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Why the job failed, when it did.</summary>
    /// <remarks>
    /// Only set for systemic failures — the database, or TMDb being unreachable
    /// entirely. A single row that cannot be resolved is an item outcome, not a
    /// job failure.
    /// </remarks>
    public string? FailureReason { get; set; }

    public ICollection<ImportItem> Items { get; init; } = [];
}

/// <summary>Where an import has got to.</summary>
/// <remarks>Named to match the states <c>docs/api.md</c> has always published.</remarks>
public enum ImportJobStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
}
