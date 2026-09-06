using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Authentication;
using NextMovie.Api.Infrastructure.Auth;

namespace NextMovie.Api.Tests.Infrastructure.Auth;

/// <summary>
/// Checks that tokens we issue are accepted by the validation the API actually runs.
/// </summary>
/// <remarks>
/// The point is the round trip. Asserting on the claims of a freshly minted token
/// only proves the issuer agrees with itself; issuing and then validating through
/// <see cref="ConfigureJwtBearerOptions"/> — the same type <c>Program</c> registers —
/// proves the two halves have not drifted apart, which is the failure that would
/// otherwise appear as every request being 401 with nothing obviously wrong.
/// </remarks>
public sealed class AccessTokenTests
{
    private static readonly JwtOptions Jwt = new()
    {
        SigningKey = "a-test-signing-key-long-enough-for-hmac-sha256",
        Issuer = "nextmovie-api-tests",
        Audience = "nextmovie-tests",
    };

    /// <summary>
    /// The real current time, not a fixed literal.
    /// </summary>
    /// <remarks>
    /// Issuing is driven by an injected clock, but validation compares against
    /// the machine's, so a token minted at a hard-coded instant would be judged
    /// expired the moment that date passed.
    /// </remarks>
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly User Parker = new()
    {
        Id = Guid.CreateVersion7(),
        Email = "Parker@Example.com",
        DisplayName = "Parker",
    };

    [Fact]
    public async Task An_issued_token_passes_the_configured_validation()
    {
        var result = await ValidateAsync(Issue(Jwt, Now));

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public async Task The_token_identifies_the_user_by_id()
    {
        var result = await ValidateAsync(Issue(Jwt, Now));

        // `sub`, not the mapped ClaimTypes.NameIdentifier URI: MapInboundClaims
        // is off precisely so the claim survives with the name it was issued
        // under. Anything reading the caller's identity depends on this.
        Assert.Equal(Parker.Id.ToString(), result.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal(Parker.Email, result.Claims[JwtRegisteredClaimNames.Email]);
    }

    [Fact]
    public void The_token_expires_after_the_policy_lifetime()
    {
        var token = Issue(Jwt, Now);

        Assert.Equal(Now + AuthenticationPolicy.AccessTokenLifetime, token.ExpiresAt);
    }

    [Fact]
    public async Task A_token_signed_with_a_different_key_is_rejected()
    {
        var forged = Issue(
            new JwtOptions
            {
                SigningKey = "a-different-key-that-is-also-long-enough-for-hs256",
                Issuer = Jwt.Issuer,
                Audience = Jwt.Audience,
            },
            Now);

        var result = await ValidateAsync(forged);

        // Everything else about this token is correct, so if signature
        // validation were off it would sail through — which would mean anyone
        // could mint tokens for any user.
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_rejected()
    {
        var foreign = Issue(
            new JwtOptions
            {
                SigningKey = Jwt.SigningKey,
                Issuer = "some-other-service",
                Audience = Jwt.Audience,
            },
            Now);

        Assert.False((await ValidateAsync(foreign)).IsValid);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        // Issued far enough in the past that the lifetime plus the permitted
        // clock skew have both elapsed.
        var stale = Issue(Jwt, Now - AuthenticationPolicy.AccessTokenLifetime - TimeSpan.FromHours(1));

        Assert.False((await ValidateAsync(stale)).IsValid);
    }

    private static AccessToken Issue(JwtOptions options, DateTimeOffset issuedAt) =>
        new JwtAccessTokenIssuer(Options.Create(options), new FixedTimeProvider(issuedAt)).Issue(Parker);

    private static Task<TokenValidationResult> ValidateAsync(AccessToken token)
    {
        var bearer = new JwtBearerOptions();
        new ConfigureJwtBearerOptions(Options.Create(Jwt))
            .Configure(JwtBearerDefaults.AuthenticationScheme, bearer);

        return new JsonWebTokenHandler().ValidateTokenAsync(token.Value, bearer.TokenValidationParameters);
    }
}
