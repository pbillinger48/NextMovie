using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Recommendations;

/// <summary>
/// Withdraws the signed-in user's response to a film.
/// </summary>
/// <remarks>
/// How a film leaves the watchlist, and how a dismissal is undone — the same
/// operation, because they are the same row (ADR-0011).
/// <para>
/// It does not un-watch anything. "Seen it" is recorded as a viewing, and
/// deleting viewings is a different capability that ADR-0006 does not cover;
/// silently removing one here would make this endpoint destroy history it was
/// never asked about.
/// </para>
/// </remarks>
public static class WithdrawMovieResponse
{
    /// <summary>Registers the response withdrawal endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/v1/movies/{id:guid}/response", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(WithdrawMovieResponse))
            .WithSummary("Withdraw your response to a film")
            .WithDescription(
                "Removes a Saved or NotInterested response, taking the film off the "
                + "watchlist or un-hiding it. Does not remove a recorded viewing.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<MovieResponseState>, ProblemHttpResult>> HandleAsync(
        Guid id,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return MovieResponseResults.NoLongerSignedIn();
        }

        // Idempotent, and no 404 for a film with no response: the caller asked for
        // there to be no response, and there is none. Clients undo from a list
        // that may already have moved on, and failing them for succeeding would be
        // a worse contract.
        if (await RespondToMovie.WithdrawAsync(db, userId, id, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // The resulting state rather than 204, because withdrawing a response does
        // not always leave a film untouched — one marked seen is still watched, and
        // a client that assumed otherwise would render the wrong buttons.
        return TypedResults.Ok(await RespondToMovie.StateAsync(db, userId, id, cancellationToken));
    }
}
