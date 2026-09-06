using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace NextMovie.Api.Infrastructure.Auth;

/// <summary>
/// Configures bearer token validation from the same options used to issue them.
/// </summary>
/// <remarks>
/// A separate class rather than a lambda in <c>Program</c> for two reasons. It
/// keeps issuing and validating provably in agreement — a test can construct this
/// and check that a token from <see cref="JwtAccessTokenIssuer"/> passes the real
/// validation parameters, which a lambda buried in startup could not. And it
/// defers reading <see cref="JwtOptions"/> until the options are actually
/// resolved, so the build-time OpenAPI generator (which has no signing key and
/// never serves a request) can still start the host.
/// </remarks>
internal sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> options)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions bearer)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        Configure(bearer);
    }

    public void Configure(JwtBearerOptions bearer)
    {
        var jwt = options.Value;

        // Without this the handler rewrites `sub` to the long ClaimTypes URI for
        // backwards compatibility, so code reading `sub` silently finds nothing.
        bearer.MapInboundClaims = false;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            // Pinned to the algorithm we issue with. A validator that accepts
            // whatever the token's own header asks for is the root of the
            // classic JWT algorithm-confusion attacks — the token must not get
            // to choose how it is verified.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,

            // Default is five minutes, which would extend every 15-minute token
            // by a third. Thirty seconds absorbs ordinary clock drift without
            // meaningfully widening the window a stolen token stays usable.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }
}
