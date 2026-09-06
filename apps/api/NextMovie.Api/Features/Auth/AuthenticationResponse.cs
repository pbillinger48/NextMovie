using NextMovie.Api.Domain;

namespace NextMovie.Api.Features.Auth;

/// <summary>A newly established session.</summary>
/// <remarks>
/// Register and login return the same shape deliberately: from the client's
/// point of view both end in "you are signed in", and giving them different
/// response types would push that distinction into every caller for no reason.
/// </remarks>
/// <param name="AccessToken">Bearer token for API calls. Send as <c>Authorization: Bearer {token}</c>.</param>
/// <param name="AccessTokenExpiresAt">When the access token stops being accepted.</param>
/// <param name="RefreshToken">Opaque token used to obtain a new access token. Store it as securely as a password.</param>
/// <param name="RefreshTokenExpiresAt">When the refresh token stops being exchangeable.</param>
/// <param name="User">The signed-in account.</param>
public sealed record AuthenticationResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthenticatedUser User);

/// <summary>The account a session belongs to.</summary>
/// <param name="Id">NextMovie user identifier.</param>
/// <param name="Email">Email address, as the user entered it.</param>
/// <param name="DisplayName">Name to show in the UI.</param>
/// <param name="ProfileImageUrl">Avatar URL, when one is set.</param>
public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    string DisplayName,
    string? ProfileImageUrl)
{
    internal static AuthenticatedUser From(User user) => new(
        Id: user.Id,
        Email: user.Email,
        DisplayName: user.DisplayName,
        ProfileImageUrl: user.ProfileImageUrl);
}
