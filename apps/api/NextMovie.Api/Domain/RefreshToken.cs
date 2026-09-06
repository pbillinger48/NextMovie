namespace NextMovie.Api.Domain;

/// <summary>
/// A single issued refresh token.
/// </summary>
/// <remarks>
/// Refresh tokens are persisted because ADR-0003 requires rotation and family
/// revocation, and neither is possible with a purely stateless token.
/// <para>
/// Rows are never deleted on rotation. A rotated token must remain in the table
/// so that presenting it again can be recognised as replay — deleting it would
/// make a stolen token indistinguishable from one that never existed.
/// </para>
/// </remarks>
public class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public User User { get; init; } = null!;

    /// <summary>
    /// SHA-256 hash of the token value, hex encoded. The token itself is never
    /// stored.
    /// </summary>
    /// <remarks>
    /// A fast hash is correct here and is not an inconsistency with password
    /// hashing. Slow hashing exists to resist brute force against low-entropy,
    /// human-chosen secrets; a 256-bit random token has no such weakness, so
    /// PBKDF2 would add latency to every refresh and buy nothing. Hashing at all
    /// still matters: it means a leaked database dump does not hand an attacker
    /// usable tokens.
    /// </remarks>
    public required string TokenHash { get; init; }

    /// <summary>
    /// Identifies the rotation chain this token belongs to.
    /// </summary>
    /// <remarks>
    /// Every refresh issues a new token in the same family. If a token that has
    /// already been rotated is presented again, the only innocent explanations
    /// are a race or a retry — but the dangerous one is that it was stolen, and
    /// we cannot tell which holder is legitimate. Revoking the whole family logs
    /// out both, which is the correct conservative response.
    /// </remarks>
    public required Guid FamilyId { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When this token was revoked, or null while it remains valid.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The token that superseded this one, when it was rotated.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>Whether this token can still be exchanged.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
