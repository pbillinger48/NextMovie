using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace NextMovie.Api.Infrastructure.Auth.Google;

/// <summary>Fetches Google's signing keys from its OpenID Connect metadata.</summary>
/// <remarks>
/// Delegates to <see cref="ConfigurationManager{T}"/>, which caches the document
/// and refetches it when Google rotates keys. Writing that by hand is deceptively
/// easy to get wrong: cache too long and every sign-in fails the moment a key
/// rotates, cache too little and each sign-in waits on a network round trip.
/// </remarks>
internal sealed class GoogleSigningKeyProvider : IGoogleSigningKeyProvider
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;

    public GoogleSigningKeyProvider(IOptions<GoogleOptions> options, IHttpClientFactory httpClientFactory)
    {
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            options.Value.MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(httpClientFactory.CreateClient(nameof(GoogleSigningKeyProvider))));
    }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken)
    {
        var configuration = await _configuration.GetConfigurationAsync(cancellationToken);

        return [.. configuration.SigningKeys];
    }
}
