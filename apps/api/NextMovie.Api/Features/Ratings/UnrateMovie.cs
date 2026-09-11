using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Ratings;

/// <summary>
/// Removes the signed-in user's rating of a film.
/// </summary>
/// <remarks>
/// Deleting the row rather than storing a zero: an absent opinion and a bad
/// opinion are different statements, and a zero would tell the recommendation
/// engine the second when the user meant the first (ADR-0006).
/// <para>
/// The viewing is left in place. Changing your mind about a rating is not a claim
/// that you never saw the film.
/// </para>
/// </remarks>
public static class UnrateMovie
{
    /// <summary>Registers the unrate endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/v1/movies/{id:guid}/rating", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(UnrateMovie))
            .WithSummary("Remove a rating")
            .WithDescription(
                "Removes the signed-in user's rating of a film. The film stays in their "
                + "watch history.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id,
        ClaimsPrincipal caller,
        UserLibrary library,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return RatingResults.NoLongerSignedIn();
        }

        await library.UnrateAsync(userId, id, cancellationToken);

        // 204 whether or not there was a rating to remove. Deleting something
        // twice is not an error, and reporting 404 for "already gone" would make
        // a retried request look like a failure.
        return TypedResults.NoContent();
    }
}
