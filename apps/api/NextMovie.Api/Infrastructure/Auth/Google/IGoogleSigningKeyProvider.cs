using Microsoft.IdentityModel.Tokens;

namespace NextMovie.Api.Infrastructure.Auth.Google;

/// <summary>Supplies the keys Google is currently signing ID tokens with.</summary>
/// <remarks>
/// An interface so the verifier can be tested against a locally generated key.
/// The alternative — reaching over the network to Google inside a test — would
/// make the suite depend on the internet and on Google's current key rotation,
/// which is exactly the sort of test that fails for reasons unrelated to the code.
/// </remarks>
internal interface IGoogleSigningKeyProvider
{
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken);
}
