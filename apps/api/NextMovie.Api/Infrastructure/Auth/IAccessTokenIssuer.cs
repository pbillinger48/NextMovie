using NextMovie.Api.Domain;

namespace NextMovie.Api.Infrastructure.Auth;

/// <summary>Mints the short-lived access tokens clients send as bearer tokens.</summary>
internal interface IAccessTokenIssuer
{
    /// <summary>Issues a signed access token for <paramref name="user"/>.</summary>
    AccessToken Issue(User user);
}

/// <summary>A signed access token and the moment it stops being accepted.</summary>
/// <remarks>
/// The expiry is returned alongside the token so callers never have to decode
/// the JWT to find out when to refresh. Parsing a token to discover its own
/// lifetime is how clients end up trusting unverified claims.
/// </remarks>
/// <param name="Value">The encoded JWT.</param>
/// <param name="ExpiresAt">When the token expires.</param>
internal sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
