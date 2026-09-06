using System.ComponentModel.DataAnnotations;

namespace NextMovie.Api.Infrastructure.Auth;

/// <summary>Configuration for issuing and validating access tokens.</summary>
/// <remarks>
/// Holds only what legitimately varies between environments. Token lifetimes are
/// not here on purpose — they live in
/// <see cref="Domain.Authentication.AuthenticationPolicy"/>, because they are
/// properties of the protocol rather than of the deployment.
/// </remarks>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC-SHA256 requires a key of at least 256 bits. Below that,
    /// <c>SymmetricSecurityKey</c> refuses to sign — better to fail at startup
    /// with a clear message than on the first sign-in attempt.
    /// </summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>
    /// Secret used to sign and verify access tokens, read as UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// Never stored in the repository. Supplied locally via .NET user-secrets and
    /// via environment configuration elsewhere — generate one with
    /// <c>openssl rand -base64 48</c>.
    /// <para>
    /// Symmetric (HS256) rather than asymmetric (RS256) because one service both
    /// issues and verifies these tokens. Asymmetric signing pays for itself when
    /// a separate service must verify tokens without being able to mint them; we
    /// have no such service, so it would add key distribution for no benefit.
    /// Revisit if a second service ever needs to validate NextMovie tokens.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    [MinLength(MinimumSigningKeyLength)]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Value placed in, and required of, the <c>iss</c> claim.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = "nextmovie-api";

    /// <summary>Value placed in, and required of, the <c>aud</c> claim.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = "nextmovie";
}
