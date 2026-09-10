namespace NextMovie.Api.Infrastructure.Auth.Google;

/// <summary>Checks that an ID token really came from Google and is meant for us.</summary>
internal interface IGoogleIdTokenVerifier
{
    /// <summary>
    /// Verifies an ID token.
    /// </summary>
    /// <returns>
    /// The identity it asserts, or null when the token fails verification for any
    /// reason.
    /// </returns>
    /// <remarks>
    /// One null for every failure, deliberately. The caller has the same response
    /// to all of them — refuse the sign-in — and a reason code would only invite
    /// somebody to surface it, which would tell an attacker holding a token
    /// exactly which part of it to fix. The reason is logged instead.
    /// </remarks>
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken);
}

/// <summary>What a verified Google ID token tells us about a person.</summary>
/// <param name="Subject">
/// Google's stable identifier for the account — the value an external login is
/// keyed on. See ADR-0005.
/// </param>
/// <param name="Email">The address on the Google account.</param>
/// <param name="EmailVerified">Whether Google says the address is verified. Linking depends on this.</param>
/// <param name="Name">Display name, when Google supplies one.</param>
/// <param name="PictureUrl">Avatar URL, when Google supplies one.</param>
internal sealed record GoogleIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string? Name,
    string? PictureUrl);
