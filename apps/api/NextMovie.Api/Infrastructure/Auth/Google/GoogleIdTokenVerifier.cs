using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NextMovie.Api.Infrastructure.Auth.Google;

/// <summary>Verifies Google ID tokens against Google's published signing keys.</summary>
/// <remarks>
/// Nothing in an ID token may be trusted before this passes. An unverified JWT is
/// just a base64 string somebody handed us — its <c>sub</c> and <c>email</c> are
/// claims an attacker can type.
/// </remarks>
internal sealed class GoogleIdTokenVerifier(
    IGoogleSigningKeyProvider signingKeys,
    IOptions<GoogleOptions> options,
    ILogger<GoogleIdTokenVerifier> logger) : IGoogleIdTokenVerifier
{
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>
    /// Both forms Google uses. The bare host appears in tokens from some clients
    /// and the scheme-qualified form in others; rejecting either would fail real
    /// sign-ins.
    /// </summary>
    private static readonly string[] ValidIssuers =
    [
        "https://accounts.google.com",
        "accounts.google.com",
    ];

    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SecurityKey> keys;

        try
        {
            keys = await signingKeys.GetSigningKeysAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Google's metadata is unreachable. Refusing is the only safe answer
            // — but it is our problem, not a bad token, so it is logged as such.
            logger.LogError(exception, "Could not fetch Google's signing keys; refusing the sign-in");

            return null;
        }

        var result = await Handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,

            // Pinned to what Google actually signs with. A validator that accepts
            // whatever the token's header asks for is how algorithm-confusion
            // attacks start — the token must not choose how it is verified.
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

            ValidateIssuer = true,
            ValidIssuers = ValidIssuers,

            // The allow-list from configuration, and the reason a token minted
            // for someone else's Google client cannot sign anybody in here.
            ValidateAudience = true,
            ValidAudiences = options.Value.ClientIds,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!result.IsValid)
        {
            logger.LogWarning(result.Exception, "Rejected a Google ID token");

            return null;
        }

        return ToIdentity(result.Claims, logger);
    }

    private static GoogleIdentity? ToIdentity(IDictionary<string, object> claims, ILogger logger)
    {
        var subject = StringClaim(claims, JwtRegisteredClaimNames.Sub);
        var email = StringClaim(claims, JwtRegisteredClaimNames.Email);

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            // Both are required to create or find an account. A validly signed
            // token without them is Google behaving unexpectedly, which is worth
            // a log rather than a silent refusal.
            logger.LogWarning("A valid Google ID token was missing its subject or email");

            return null;
        }

        return new GoogleIdentity(
            Subject: subject,
            Email: email,
            EmailVerified: BoolClaim(claims, "email_verified"),
            Name: StringClaim(claims, "name"),
            PictureUrl: StringClaim(claims, "picture"));
    }

    private static string? StringClaim(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value) ? value as string : null;

    /// <remarks>
    /// <c>email_verified</c> arrives as a JSON boolean from Google's own tokens
    /// but as the string "true" from some client libraries. Reading only one form
    /// would silently treat every verified address as unverified, which is a
    /// refusal to sign in rather than a security hole — but a baffling one.
    /// </remarks>
    private static bool BoolClaim(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value) && value switch
        {
            bool flag => flag,
            string text => bool.TryParse(text, out var parsed) && parsed,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            _ => false,
        };
}
