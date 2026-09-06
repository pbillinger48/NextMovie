using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Users;

/// <summary>
/// Returns the signed-in user's own profile.
/// </summary>
/// <remarks>
/// The first endpoint to require authentication, and therefore the first to
/// exercise the bearer middleware wired up alongside login. The caller is
/// identified from the token's <c>sub</c> claim and never from anything in the
/// request — a user id in the route or query string would let any signed-in
/// caller read any other account.
/// </remarks>
public static class GetCurrentUser
{
    /// <summary>Registers the current-user endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/me", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(GetCurrentUser))
            .WithSummary("Get the signed-in user's profile")
            .WithDescription("Returns the profile of the account the access token belongs to.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<UserProfileResponse>, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return CurrentUserResults.NoLongerSignedIn();
        }

        // AsNoTracking: this is a read, and change tracking would only add
        // bookkeeping for entities nothing intends to modify.
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        return user is null
            ? CurrentUserResults.NoLongerSignedIn()
            : TypedResults.Ok(UserProfileResponse.From(user));
    }
}

/// <summary>Responses shared by the current-user slices.</summary>
internal static class CurrentUserResults
{
    /// <summary>
    /// The token is valid but names a user who no longer exists.
    /// </summary>
    /// <remarks>
    /// 401 rather than 404: the token authenticates a subject that is gone, so
    /// there is no caller to authorise, and the client's correct response is to
    /// discard the session and sign in again. A 404 would read as "try again
    /// later" and leave the client holding a token that can never work.
    /// <para>
    /// Unreachable today — nothing deletes accounts. It exists because the
    /// alternative is a <c>NullReferenceException</c> the first time something
    /// does.
    /// </para>
    /// </remarks>
    public static ProblemHttpResult NoLongerSignedIn() => TypedResults.Problem(
        title: "Not signed in",
        detail: "This session is no longer valid. Sign in again.",
        statusCode: StatusCodes.Status401Unauthorized);
}
