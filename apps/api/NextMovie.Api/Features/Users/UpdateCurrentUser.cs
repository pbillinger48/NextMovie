using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Users;

/// <summary>
/// Updates the signed-in user's own profile.
/// </summary>
/// <remarks>
/// <c>PUT</c>, so the body is the profile's new state: a request that omits
/// <c>profileImageUrl</c> clears it. That is what the verb means and what
/// <c>docs/api.md</c> specifies, and it is the client's job to send the whole
/// profile — a partial save belongs on <c>PATCH</c>, which this deliberately is
/// not.
/// <para>
/// Only the display name and image are editable here. Email is absent because
/// changing it needs a verification round trip before the unique index moves, or
/// an account could claim an address it does not own. Password is absent because
/// changing it needs the current password and should revoke every refresh token
/// family — "change my password" is what people do when they believe they are
/// compromised, and a session that survives it defeats the point. Each is its own
/// slice, not a field on a routine profile save.
/// </para>
/// </remarks>
public static class UpdateCurrentUser
{
    private const int MaxDisplayNameLength = 100;
    private const int MaxProfileImageUrlLength = 2048;

    /// <summary>Registers the profile update endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/users/me", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(UpdateCurrentUser))
            .WithSummary("Update the signed-in user's profile")
            .WithDescription(
                "Replaces the editable parts of the profile. Fields omitted from the "
                + "request are cleared.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<UserProfileResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        UpdateCurrentUserRequest request,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return CurrentUserResults.NoLongerSignedIn();
        }

        if (Validate(request) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var user = await db.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return CurrentUserResults.NoLongerSignedIn();
        }

        user.DisplayName = request.DisplayName!.Trim();

        // Whitespace-only is treated as absent, so a cleared form field and an
        // omitted one mean the same thing rather than storing " " as a URL.
        user.ProfileImageUrl = string.IsNullOrWhiteSpace(request.ProfileImageUrl)
            ? null
            : request.ProfileImageUrl.Trim();

        // Stamped here rather than by the database: the row has changed, and a
        // profile whose UpdatedAt lies is worse than one without the column.
        user.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(UserProfileResponse.From(user));
    }

    private static Dictionary<string, string[]> Validate(UpdateCurrentUserRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors[nameof(request.DisplayName)] = ["A display name is required."];
        }
        else if (request.DisplayName.Trim().Length > MaxDisplayNameLength)
        {
            errors[nameof(request.DisplayName)] = [$"A display name may be at most {MaxDisplayNameLength} characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.ProfileImageUrl))
        {
            var url = request.ProfileImageUrl.Trim();

            if (url.Length > MaxProfileImageUrlLength)
            {
                errors[nameof(request.ProfileImageUrl)] =
                    [$"A profile image URL may be at most {MaxProfileImageUrlLength} characters."];
            }
            else if (!IsHttpUrl(url))
            {
                // Restricted to http(s) because this value is rendered as an
                // image source by every client. Accepting arbitrary schemes
                // would let a user store `javascript:` or a `data:` payload and
                // have it echoed back into other people's pages later, when
                // avatars stop being private.
                errors[nameof(request.ProfileImageUrl)] =
                    ["A profile image URL must be an absolute http or https URL."];
            }
        }

        return errors;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

/// <summary>The new state of the editable profile.</summary>
/// <remarks>
/// There is no email or password field, and adding one to this record would be a
/// security change rather than a convenience — see the remarks on
/// <see cref="UpdateCurrentUser"/>.
/// </remarks>
/// <param name="DisplayName">Name to show in the UI. Required.</param>
/// <param name="ProfileImageUrl">Absolute http or https avatar URL. Omit or send null to clear it.</param>
public sealed record UpdateCurrentUserRequest(string? DisplayName, string? ProfileImageUrl);
