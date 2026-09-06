using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Authentication;

namespace NextMovie.Api.Infrastructure.Auth;

/// <summary>Issues HS256 JWT access tokens.</summary>
/// <remarks>
/// Uses <see cref="JsonWebTokenHandler"/> rather than the older
/// <c>JwtSecurityTokenHandler</c>: the newer handler is the maintained one, is
/// faster, and does not silently rename claims the way its predecessor's inbound
/// claim mapping does.
/// </remarks>
internal sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions _options;
    private readonly TimeProvider _time;
    private readonly SigningCredentials _credentials;

    public JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;

        // Built once. Deriving the key per token would hash the secret on every
        // sign-in for no benefit — the key is immutable for the process lifetime.
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Issue(User user)
    {
        var now = _time.GetUtcNow();
        var expiresAt = now + AuthenticationPolicy.AccessTokenLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                // The user id, and the only claim any authorisation decision
                // should be made from. Everything else in here is a convenience.
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),

                // Carried so the API can identify the caller in logs without a
                // database round trip. Deliberately nothing else: a JWT is
                // readable by anyone holding it, so display names, roles and
                // profile data go in the token only when something needs them.
                [JwtRegisteredClaimNames.Email] = user.Email,

                // A unique token id. Not used yet — it exists so that if
                // access-token revocation is ever needed, there is something to
                // revoke. Adding it later would leave every already-issued token
                // unidentifiable.
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
            },
        };

        return new AccessToken(Handler.CreateToken(descriptor), expiresAt);
    }
}
