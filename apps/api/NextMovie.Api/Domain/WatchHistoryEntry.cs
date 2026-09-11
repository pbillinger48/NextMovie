using NextMovie.Api.Domain.Library;

namespace NextMovie.Api.Domain;

/// <summary>
/// One viewing of one film by one user.
/// </summary>
/// <remarks>
/// Append-only, and deliberately not unique per user and film: a film watched
/// three times is three rows, which is what the Letterboxd diary export records
/// and what any "watched again" feature will need (ADR-0006).
/// </remarks>
public class WatchHistoryEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required Guid MovieId { get; init; }

    public Movie Movie { get; init; } = null!;

    /// <summary>
    /// When the film was watched, or null when that is not known.
    /// </summary>
    /// <remarks>
    /// Nullable because "I have seen this, I do not remember when" is a real and
    /// common state: <c>ratings.csv</c> carries no date, and neither does someone
    /// rating a film from memory. A sentinel date would be a lie that later sorts
    /// wrongly and averages wrongly.
    /// <para>
    /// A date rather than a timestamp: people remember the day they saw a film,
    /// not the minute, and storing an instant invites time-zone bugs that shift a
    /// viewing across midnight.
    /// </para>
    /// </remarks>
    public DateOnly? WatchedOn { get; set; }

    /// <summary>Where this viewing came from.</summary>
    public required LibrarySource Source { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
