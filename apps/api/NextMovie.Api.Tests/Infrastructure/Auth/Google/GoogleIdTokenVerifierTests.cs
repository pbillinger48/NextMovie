using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NextMovie.Api.Infrastructure.Auth.Google;

namespace NextMovie.Api.Tests.Infrastructure.Auth.Google;

/// <summary>
/// Tests what the API is willing to accept as proof of a Google identity.
/// </summary>
/// <remarks>
/// This is the security boundary of ADR-0005: everything downstream trusts the
/// <c>sub</c> and <c>email</c> that come out of here, and an unverified JWT is
/// just a string somebody typed. Driven with a locally generated RSA key rather
/// than Google's real one, so the suite neither needs the internet nor breaks
/// when Google rotates keys.
/// </remarks>
public sealed class GoogleIdTokenVerifierTests
{
    private const string ClientId = "nextmovie-test.apps.googleusercontent.com";
    private const string GoogleIssuer = "https://accounts.google.com";
    private const string Subject = "112233445566778899000";
    private const string Email = "parker@example.com";

    private static readonly RSA GoogleKey = RSA.Create(2048);
    private static readonly RsaSecurityKey SigningKey = new(GoogleKey) { KeyId = "test-key" };

    [Fact]
    public async Task Accepts_a_well_formed_google_token()
    {
        var identity = await VerifyAsync(TokenWith());

        Assert.NotNull(identity);
        Assert.Equal(Subject, identity.Subject);
        Assert.Equal(Email, identity.Email);
        Assert.True(identity.EmailVerified);
        Assert.Equal("Parker", identity.Name);
    }

    [Fact]
    public async Task Accepts_the_bare_issuer_form()
    {
        // Some Google clients issue "accounts.google.com" without the scheme.
        // Rejecting it would fail real sign-ins from those clients.
        Assert.NotNull(await VerifyAsync(TokenWith(issuer: "accounts.google.com")));
    }

    [Fact]
    public async Task Rejects_a_token_for_another_client()
    {
        // The audience allow-list is what stops a token minted for somebody
        // else's Google client from signing their user in here. Everything else
        // about this token is valid.
        Assert.Null(await VerifyAsync(TokenWith(audience: "someone-elses-app.apps.googleusercontent.com")));
    }

    [Fact]
    public async Task Rejects_a_token_from_another_issuer()
    {
        Assert.Null(await VerifyAsync(TokenWith(issuer: "https://evil.example.com")));
    }

    [Fact]
    public async Task Rejects_an_expired_token()
    {
        Assert.Null(await VerifyAsync(TokenWith(expires: DateTime.UtcNow.AddHours(-2))));
    }

    [Fact]
    public async Task Rejects_a_token_signed_with_an_unknown_key()
    {
        // Correct issuer, correct audience, correct claims — but not signed by
        // Google. If signature checking were off, anyone could mint identities.
        var attacker = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "attacker" };

        Assert.Null(await VerifyAsync(TokenWith(signingKey: attacker)));
    }

    [Fact]
    public async Task Rejects_a_token_missing_an_email()
    {
        // An account cannot be created or found without one, and a validly signed
        // token that lacks it means Google behaved unexpectedly.
        Assert.Null(await VerifyAsync(TokenWith(email: null)));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Reads_a_boolean_verified_flag(bool claim, bool expected)
    {
        var identity = await VerifyAsync(TokenWith(emailVerified: claim));

        Assert.NotNull(identity);
        Assert.Equal(expected, identity.EmailVerified);
    }

    [Fact]
    public async Task Reads_a_string_verified_flag()
    {
        // Some client libraries render the claim as the string "true". Reading
        // only the boolean form would treat every verified address as unverified
        // and refuse sign-ins for no visible reason.
        var identity = await VerifyAsync(TokenWith(emailVerified: "true"));

        Assert.NotNull(identity);
        Assert.True(identity.EmailVerified);
    }

    [Fact]
    public async Task Refuses_when_googles_keys_cannot_be_fetched()
    {
        var verifier = new GoogleIdTokenVerifier(
            new UnreachableKeys(),
            OptionsFor(),
            NullLogger<GoogleIdTokenVerifier>.Instance);

        // Failing closed: without the keys there is no way to tell a real token
        // from a forged one, so nobody signs in.
        Assert.Null(await verifier.VerifyAsync(TokenWith(), CancellationToken.None));
    }

    private static async Task<GoogleIdentity?> VerifyAsync(string idToken)
    {
        var verifier = new GoogleIdTokenVerifier(
            new TestKeys(SigningKey),
            OptionsFor(),
            NullLogger<GoogleIdTokenVerifier>.Instance);

        return await verifier.VerifyAsync(idToken, CancellationToken.None);
    }

    private static IOptions<GoogleOptions> OptionsFor() =>
        Options.Create(new GoogleOptions { ClientIds = [ClientId] });

    private static string TokenWith(
        string issuer = GoogleIssuer,
        string audience = ClientId,
        string? email = Email,
        object? emailVerified = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = Subject,
            ["email_verified"] = emailVerified ?? true,
            ["name"] = "Parker",
            ["picture"] = "https://lh3.googleusercontent.com/a/parker",
        };

        if (email is not null)
        {
            claims[JwtRegisteredClaimNames.Email] = email;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                signingKey ?? SigningKey,
                SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class TestKeys(SecurityKey key) : IGoogleSigningKeyProvider
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SecurityKey>>([key]);
    }

    private sealed class UnreachableKeys : IGoogleSigningKeyProvider
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken) =>
            throw new HttpRequestException("Google is unreachable");
    }
}
