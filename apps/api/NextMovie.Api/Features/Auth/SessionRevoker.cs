using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Auth;

/// <summary>
/// Revokes refresh tokens by family.
/// </summary>
/// <remarks>
/// Shared by logout, which ends one session deliberately, and by refresh, which
/// ends one because a token was replayed. Both mean the same thing to the
/// database — every live token in this chain stops working — so they share an
/// implementation rather than two that could drift.
/// </remarks>
internal sealed class SessionRevoker(NextMovieDbContext db)
{
    /// <summary>Revokes every token in a family that is still live.</summary>
    /// <returns>How many tokens were revoked.</returns>
    /// <remarks>
    /// A single ranged UPDATE, supported by the index on <c>family_id</c>. It
    /// matters that this is one statement: revoking on suspicion of theft is
    /// exactly when we want to be fast and atomic, not loading a chain into
    /// memory a row at a time.
    /// <para>
    /// <c>ExecuteUpdateAsync</c> bypasses the change tracker, so any
    /// <see cref="Domain.RefreshToken"/> already loaded in this context keeps its
    /// stale <c>RevokedAt</c>. Callers must not save a tracked copy afterwards
    /// expecting it to agree with the database.
    /// </para>
    /// </remarks>
    public Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now),
                cancellationToken);
}
