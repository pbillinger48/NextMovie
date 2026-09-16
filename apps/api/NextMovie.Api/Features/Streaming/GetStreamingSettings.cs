using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Streaming;

/// <summary>
/// Returns where the signed-in user watches and what they subscribe to.
/// </summary>
/// <remarks>
/// Its own resource rather than fields on the profile. The service list is
/// fetched from an external catalogue and runs to a hundred entries — putting it
/// on <c>GET /users/me</c> would make every profile read pay for it, including
/// the one the header does on every page.
/// </remarks>
public static class GetStreamingSettings
{
    /// <summary>Registers the streaming settings endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/me/streaming", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(GetStreamingSettings))
            .WithSummary("Get the signed-in user's streaming settings")
            .WithDescription(
                "Returns the user's region, the countries they may choose from, and the "
                + "services on offer there with their subscriptions marked.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<StreamingSettingsResponse>, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        ServiceCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return StreamingSettingsResults.NoLongerSignedIn();
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        return user is null
            ? StreamingSettingsResults.NoLongerSignedIn()
            : TypedResults.Ok(await StreamingSettings.ForAsync(user, catalog, db, cancellationToken));
    }
}

/// <summary>Responses shared by the streaming settings slices.</summary>
internal static class StreamingSettingsResults
{
    /// <inheritdoc cref="Users.CurrentUserResults.NoLongerSignedIn"/>
    public static ProblemHttpResult NoLongerSignedIn() => TypedResults.Problem(
        title: "Not signed in",
        detail: "This session is no longer valid. Sign in again.",
        statusCode: StatusCodes.Status401Unauthorized);
}
