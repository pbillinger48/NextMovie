using NextMovie.Api.Domain.Library;

namespace NextMovie.Api.Domain;

/// <summary>
/// What one user thinks of one film, now.
/// </summary>
/// <remarks>
/// One row per user per film, enforced by a unique index: a person holds one
/// opinion of a film at a time, and re-rating updates rather than accumulates.
/// Unrating deletes the row — an absent rating and a rating of zero are different
/// statements, and only one of them is representable (ADR-0006).
/// </remarks>
public class Rating
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required Guid MovieId { get; init; }

    public Movie Movie { get; init; } = null!;

    /// <summary>
    /// 0.5 to 5.0 in half-stars. See <see cref="RatingScale"/>.
    /// </summary>
    /// <remarks>
    /// <c>decimal</c>, mapped to <c>numeric(2,1)</c>, not a floating-point type.
    /// These values get grouped, averaged and compared by the recommendation
    /// engine, and binary floating point is where two equal ratings quietly stop
    /// being equal.
    /// </remarks>
    public required decimal Value { get; set; }

    /// <summary>Where this rating came from.</summary>
    /// <remarks>
    /// Checked before a re-import overwrites anything: a rating the user typed
    /// outranks one a CSV supplied.
    /// </remarks>
    public required LibrarySource Source { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Applies a new rating, recording when it changed.</summary>
    public void Revise(decimal value, LibrarySource source, DateTimeOffset now)
    {
        Value = value;
        Source = source;
        UpdatedAt = now;
    }
}
