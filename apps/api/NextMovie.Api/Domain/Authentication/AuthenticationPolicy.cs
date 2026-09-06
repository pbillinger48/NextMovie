namespace NextMovie.Api.Domain.Authentication;

/// <summary>
/// The numbers that define how authentication behaves.
/// </summary>
/// <remarks>
/// Deliberately constants rather than configuration. Every value here is a
/// security property of the protocol, and an environment that silently issued a
/// thirty-day access token because a variable was mistyped would be a far worse
/// failure than a redeploy to change one. Configuration holds what legitimately
/// differs per environment — issuer, audience, signing key — and nothing that
/// changes the shape of the protocol itself. See ADR-0003.
/// </remarks>
public static class AuthenticationPolicy
{
    /// <summary>
    /// How long an access token stays valid.
    /// </summary>
    /// <remarks>
    /// Short because access tokens are stateless: there is no revocation list to
    /// consult, so the window between a token being stolen and becoming useless
    /// is exactly this value. Fifteen minutes trades a refresh round trip for
    /// that window, which is the trade ADR-0003 makes.
    /// </remarks>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>How long a refresh token stays exchangeable.</summary>
    /// <remarks>
    /// Long because this is what "stay signed in" means in practice. The risk is
    /// bounded by rotation and family revocation rather than by expiry.
    /// </remarks>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>Consecutive failures before an account is locked out.</summary>
    /// <remarks>
    /// Five is high enough that a person mistyping a password will not trip it,
    /// and low enough to make online guessing pointless. It is not a defence
    /// against a spray across many accounts — that needs request-level rate
    /// limiting, which is not built yet.
    /// </remarks>
    public const int MaxFailedLoginAttempts = 5;

    /// <summary>How long a lockout lasts once triggered.</summary>
    /// <remarks>
    /// Temporary rather than permanent on purpose. A lockout an attacker can
    /// trigger at will, against an address they merely guessed, is a denial of
    /// service against the real owner — so it has to expire on its own without
    /// a support request.
    /// </remarks>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}
