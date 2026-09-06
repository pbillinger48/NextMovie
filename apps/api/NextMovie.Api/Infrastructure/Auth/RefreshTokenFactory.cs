using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Authentication;

namespace NextMovie.Api.Infrastructure.Auth;

/// <summary>Creates refresh tokens and computes their stored hashes.</summary>
internal sealed class RefreshTokenFactory(TimeProvider time)
{
    /// <summary>
    /// 256 bits of entropy, which is why the stored hash does not need to be a
    /// slow one — see <see cref="RefreshToken.TokenHash"/>.
    /// </summary>
    private const int TokenSizeInBytes = 32;

    /// <summary>
    /// Creates a new refresh token for a user.
    /// </summary>
    /// <param name="userId">Owner of the token.</param>
    /// <param name="familyId">
    /// The rotation chain to continue. Null starts a new family, which is what a
    /// fresh sign-in does; refreshing passes the existing family through so
    /// replay of any token in the chain can revoke all of it.
    /// </param>
    /// <remarks>
    /// The plaintext value is returned exactly once, here. It is never stored and
    /// cannot be recovered from the entity — if it is lost, the only remedy is
    /// issuing another.
    /// </remarks>
    public IssuedRefreshToken Create(Guid userId, Guid? familyId = null)
    {
        var now = time.GetUtcNow();

        // RandomNumberGenerator, not Random: this value is a credential, and
        // Random is a predictable pseudo-random sequence.
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSizeInBytes));

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(value),
            FamilyId = familyId ?? Guid.CreateVersion7(),
            ExpiresAt = now + AuthenticationPolicy.RefreshTokenLifetime,
            CreatedAt = now,
        };

        return new IssuedRefreshToken(entity, value);
    }

    /// <summary>
    /// Hashes a token value into the form stored in the database.
    /// </summary>
    /// <remarks>
    /// Hex rather than base64 so the column can be fixed-length
    /// <c>character(64)</c>, and lower-cased so a lookup never depends on which
    /// side produced the casing.
    /// </remarks>
    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>A newly created refresh token, in both the forms that matter.</summary>
/// <param name="Entity">The row to persist. Carries only the hash.</param>
/// <param name="Value">The plaintext token to hand the client. Never persisted.</param>
internal sealed record IssuedRefreshToken(RefreshToken Entity, string Value);
