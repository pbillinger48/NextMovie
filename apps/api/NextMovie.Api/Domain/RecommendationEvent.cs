using NextMovie.Api.Domain.Recommendations;

namespace NextMovie.Api.Domain;

/// <summary>
/// A record that a film was recommended to someone, and where it ranked.
/// </summary>
/// <remarks>
/// Written whenever recommendations are served. Two reasons, and the second is
/// the one that matters in a year's time.
/// <para>
/// It makes the current engine <b>measurable</b>: whether the films people
/// actually watch were the ones ranked highly is a question no amount of unit
/// testing answers.
/// </para>
/// <para>
/// And it is the only part of a future learned model that cannot be built later.
/// Cast, keywords, embeddings and a GPU can all be added whenever they are
/// wanted; what was recommended in October, and what happened next, exists only
/// if it was written down at the time. Every week without this log is training
/// data that will never exist.
/// </para>
/// </remarks>
public class RecommendationEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required Guid MovieId { get; init; }

    public Movie Movie { get; init; } = null!;

    /// <summary>Where it appeared in the list, from 1.</summary>
    public required int Rank { get; init; }

    /// <summary>The score it was ranked by, 0–1.</summary>
    public required double Score { get; init; }

    public required RecommendationConfidence Confidence { get; init; }

    /// <summary>
    /// The reasons shown to the user, exactly as shown.
    /// </summary>
    /// <remarks>
    /// Stored rather than recomputed because the model will change. Knowing what
    /// a recommendation claimed at the time is what makes an old event
    /// interpretable later.
    /// </remarks>
    public required string[] Reasons { get; init; }

    public DateTimeOffset ServedAt { get; init; } = DateTimeOffset.UtcNow;
}
