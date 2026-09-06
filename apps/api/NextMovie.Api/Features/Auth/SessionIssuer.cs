using NextMovie.Api.Domain;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Auth;

/// <summary>
/// Assembles the token pair that every successful authentication returns.
/// </summary>
/// <remarks>
/// Shared by register and login, and by the refresh slice when it lands. It is
/// here rather than duplicated per slice because the sequence — mint an access
/// token, create a refresh token, persist it — is a protocol requirement, not a
/// per-endpoint detail. Three copies of it would be three places to forget the
/// refresh token.
/// <para>
/// Deliberately does not save. The caller owns the transaction, so registering a
/// user and issuing their first session are one <c>SaveChangesAsync</c> and
/// cannot half-succeed.
/// </para>
/// </remarks>
internal sealed class SessionIssuer(
    NextMovieDbContext db,
    IAccessTokenIssuer accessTokens,
    RefreshTokenFactory refreshTokens)
{
    /// <summary>Issues a session and stages its refresh token for insertion.</summary>
    /// <param name="user">The authenticated account.</param>
    /// <param name="familyId">
    /// Rotation chain to continue, or null to start a new one. A fresh sign-in
    /// starts a family; a refresh continues the presented token's.
    /// </param>
    public IssuedSession Issue(User user, Guid? familyId = null)
    {
        var accessToken = accessTokens.Issue(user);
        var refreshToken = refreshTokens.Create(user.Id, familyId);

        // Added to the set explicitly rather than by appending to
        // user.RefreshTokens. Refresh token ids are generated in application
        // code, not by the database, so a new entity reached only through a
        // tracked user's navigation looks to EF exactly like an existing row —
        // it has a key — and change tracking marks it Modified. That produced
        // an UPDATE against a row that was never inserted, which failed as a
        // concurrency violation on sign-in while registration (where the whole
        // graph is new) worked fine.
        db.RefreshTokens.Add(refreshToken.Entity);

        return new IssuedSession(
            new AuthenticationResponse(
                AccessToken: accessToken.Value,
                AccessTokenExpiresAt: accessToken.ExpiresAt,
                RefreshToken: refreshToken.Value,
                RefreshTokenExpiresAt: refreshToken.Entity.ExpiresAt,
                User: AuthenticatedUser.From(user)),
            refreshToken.Entity);
    }
}

/// <summary>A session, and the refresh token row backing it.</summary>
/// <remarks>
/// Register and login only need <paramref name="Response"/>. Rotation also needs
/// the entity, so it can record on the outgoing token which token superseded it —
/// without that link a revoked token is just revoked, and the chain that would
/// show how a stolen token was used is lost.
/// </remarks>
/// <param name="Response">What the client receives.</param>
/// <param name="RefreshToken">The row staged for insertion.</param>
internal sealed record IssuedSession(AuthenticationResponse Response, RefreshToken RefreshToken);
