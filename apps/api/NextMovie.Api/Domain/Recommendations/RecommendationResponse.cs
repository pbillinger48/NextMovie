namespace NextMovie.Api.Domain.Recommendations;

/// <summary>What somebody decided to do about a film they were shown.</summary>
/// <remarks>
/// Persisted by name rather than by number, like <c>LibrarySource</c>: the column
/// stays readable in psql, and reordering this enum cannot silently relabel every
/// existing row.
/// <para>
/// There is deliberately no <c>Seen</c> member. "I have already watched this" is a
/// fact about viewing, and ADR-0006 says viewings live in <c>watch_history</c>.
/// Recording it here as well would give "watched" two sources of truth and leave
/// the film out of the user's own history and taste profile.
/// </para>
/// </remarks>
public enum ResponseKind
{
    /// <summary>Wants to watch it. This, collected, is the watchlist.</summary>
    Saved = 1,

    /// <summary>Does not want to be shown it again.</summary>
    NotInterested = 2,
}

/// <summary>
/// One person's current answer about one film.
/// </summary>
/// <remarks>
/// Unique on the pair, because a person's answer about a film is one thing:
/// saving something they had dismissed replaces the dismissal rather than
/// contradicting it (ADR-0011).
/// <para>
/// The watchlist is a query over this — <see cref="ResponseKind.Saved"/>, newest
/// first — rather than a table of its own. Two tables would let "saved" and "on
/// the watchlist" disagree, and a watchlist row needs nothing this does not
/// already carry.
/// </para>
/// </remarks>
public class RecommendationResponse
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required Guid MovieId { get; init; }

    public Movie Movie { get; init; } = null!;

    public required ResponseKind Kind { get; set; }

    /// <summary>
    /// The impression that prompted this, when there was one.
    /// </summary>
    /// <remarks>
    /// The other half of a training row: the event holds the features exactly as
    /// they were shown, this holds the outcome. Resolved on the server as the most
    /// recent event for this user and film — a client-supplied id could attribute a
    /// response to somebody else's impression, which would corrupt the very data
    /// this exists to collect.
    /// <para>
    /// Nullable because responses also arrive from the film page, where there was
    /// no impression at all. The absence is real rather than convenient.
    /// </para>
    /// </remarks>
    public Guid? RecommendationEventId { get; set; }

    public RecommendationEvent? RecommendationEvent { get; init; }

    public required DateTimeOffset RespondedAt { get; set; }
}
