using System.ComponentModel.DataAnnotations;

namespace NextMovie.Api.Infrastructure.Auth.Google;

/// <summary>Configuration for accepting Google sign-ins.</summary>
/// <remarks>
/// There is no client secret here, and that is the point of ADR-0005: the API
/// never talks to Google's token endpoint. Each client completes the flow itself
/// and presents the resulting ID token, so all the API needs is the set of
/// audiences it will accept.
/// </remarks>
public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    /// <summary>
    /// The OAuth client IDs whose ID tokens we accept — the web client today,
    /// iOS and Android later.
    /// </summary>
    /// <remarks>
    /// This is an allow-list, and it is the security boundary: a token minted for
    /// somebody else's client must not sign anybody in here. A stale entry is a
    /// way in, so this is configuration to review rather than append to.
    /// <para>
    /// Not a secret — client IDs are public — so unlike the TMDb token and the
    /// JWT signing key this can live in ordinary configuration.
    /// </para>
    /// </remarks>
    [Required]
    [MinLength(1, ErrorMessage = "At least one Google client ID must be configured.")]
    public string[] ClientIds { get; init; } = [];

    /// <summary>Where Google publishes its OpenID Connect metadata.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MetadataAddress { get; init; } = "https://accounts.google.com/.well-known/openid-configuration";
}
