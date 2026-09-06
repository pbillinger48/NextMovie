using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace NextMovie.Api.Infrastructure.Auth;

/// <summary>Reads NextMovie's claims off an authenticated caller.</summary>
internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The id of the signed-in user, or null when the token carries no usable
    /// <c>sub</c>.
    /// </summary>
    /// <remarks>
    /// Every authenticated slice needs this, so it lives here rather than in the
    /// first slice that happened to want it.
    /// <para>
    /// Reads <c>sub</c> directly, which works because <c>MapInboundClaims</c> is
    /// off in <see cref="ConfigureJwtBearerOptions"/>. With the default mapping
    /// the claim would arrive under a long ClaimTypes URI and this would quietly
    /// find nothing — so the two settings have to stay in agreement.
    /// </para>
    /// <para>
    /// Returns null rather than throwing on an unparseable subject. The token's
    /// signature has already been verified by this point, so a malformed
    /// <c>sub</c> means we issued something wrong, not that a caller is
    /// attacking — but it is still not an identity, and the caller must be
    /// treated as unauthenticated either way.
    /// </para>
    /// </remarks>
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
            ? userId
            : null;
}
