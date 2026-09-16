namespace NextMovie.Api.Domain.Streaming;

/// <summary>
/// A service this person pays for.
/// </summary>
/// <remarks>
/// Stated by the user rather than inferred from their viewing (ADR-0010).
/// Inference would reason from where films are available <em>now</em> about what
/// somebody subscribed to years ago, and — worse — a wrong inference is invisible:
/// the person never sees the assumption, so they cannot correct it.
/// <para>
/// This will go stale when somebody cancels a service and does not say so, and
/// the product will confidently tell them a film is on it. That is an accepted
/// cost of the explicit approach, and a visible one: the label names the service,
/// so the mistake is obvious rather than silent.
/// </para>
/// </remarks>
public class UserStreamingProvider
{
    public required Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required int StreamingProviderId { get; init; }

    public StreamingProvider StreamingProvider { get; init; } = null!;

    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}
