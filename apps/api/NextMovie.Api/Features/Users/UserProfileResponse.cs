using NextMovie.Api.Domain;

namespace NextMovie.Api.Features.Users;

/// <summary>A user's own profile.</summary>
/// <remarks>
/// Identical today to <c>AuthenticatedUser</c> in the auth slices, and
/// deliberately not shared with it. That one answers "who just signed in" as part
/// of a session; this one is the profile resource. Collapsing them would mean any
/// field added to a profile later — a bio, notification settings — would silently
/// start appearing in every login response.
/// <para>
/// <c>docs/api.md</c> also promises a taste profile and streaming providers here.
/// Neither has a schema and both are deferred, so they are absent rather than
/// stubbed; the doc has been corrected to match.
/// </para>
/// </remarks>
/// <param name="Id">NextMovie user identifier.</param>
/// <param name="Email">Email address, as the user entered it.</param>
/// <param name="DisplayName">Name shown in the UI.</param>
/// <param name="ProfileImageUrl">Avatar URL, when one is set.</param>
/// <param name="CreatedAt">When the account was created.</param>
public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? ProfileImageUrl,
    DateTimeOffset CreatedAt)
{
    internal static UserProfileResponse From(User user) => new(
        Id: user.Id,
        Email: user.Email,
        DisplayName: user.DisplayName,
        ProfileImageUrl: user.ProfileImageUrl,
        CreatedAt: user.CreatedAt);
}
