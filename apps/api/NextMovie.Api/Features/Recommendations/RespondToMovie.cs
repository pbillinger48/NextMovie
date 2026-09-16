using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Recommendations;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Recommendations;

/// <summary>
/// Records what the signed-in user decided to do about a film.
/// </summary>
/// <remarks>
/// <c>PUT</c> rather than <c>POST</c>: a person has one current answer about a
/// film, so saving something twice must leave it saved once. Saving a film they
/// had dismissed replaces the dismissal rather than contradicting it (ADR-0011).
/// <para>
/// Three answers arrive here and only two are stored as responses. <c>Seen</c> is
/// a fact about viewing, so it is written to watch history instead — one source of
/// truth for "watched", and the film stays in the taste profile built from it. The
/// client is not asked to know that; it is one action from the user's point of
/// view, and the storage split is ours to keep.
/// </para>
/// </remarks>
public static class RespondToMovie
{
    /// <summary>Registers the response endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/movies/{id:guid}/response", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(RespondToMovie))
            .WithSummary("Say what you want to do about a film")
            .WithDescription(
                "Records Saved, NotInterested or Seen. Saved films form the watchlist; "
                + "all three stop the film being recommended again.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<MovieResponseState>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        RespondToMovieRequest request,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        UserLibrary library,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return MovieResponseResults.NoLongerSignedIn();
        }

        if (!MovieResponseChoice.TryParse(request.Response, out var choice))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Response)] =
                    [$"A response must be one of: {string.Join(", ", MovieResponseChoice.All)}."],
            });
        }

        // Checked before writing because the foreign key would otherwise fail as
        // a 500. A film nobody has searched for is genuinely not in the
        // catalogue, and that is a 404 rather than a server fault.
        if (!await db.Movies.AnyAsync(movie => movie.Id == id, cancellationToken))
        {
            return MovieResponseResults.FilmNotFound();
        }

        var now = time.GetUtcNow();

        if (choice == MovieResponseChoice.Seen)
        {
            await library.MarkWatchedAsync(userId, id, now, cancellationToken);

            // Any earlier Saved or NotInterested is withdrawn. Both were
            // statements about a film they had not seen, and neither survives
            // finding out that they had.
            await WithdrawAsync(db, userId, id, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return TypedResults.Ok(await StateAsync(db, userId, id, cancellationToken));
        }

        var kind = choice == MovieResponseChoice.Saved ? ResponseKind.Saved : ResponseKind.NotInterested;

        var response = await db.RecommendationResponses
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.MovieId == id,
                cancellationToken);

        if (response is null)
        {
            response = new RecommendationResponse
            {
                UserId = userId,
                MovieId = id,
                Kind = kind,
                RespondedAt = now,
            };

            db.RecommendationResponses.Add(response);
        }
        else
        {
            response.Kind = kind;
            response.RespondedAt = now;
        }

        // Resolved here rather than taken from the request. A client-supplied
        // event id could attribute a response to somebody else's impression,
        // which would corrupt the very data this exists to collect.
        response.RecommendationEventId = await db.RecommendationEvents
            .Where(shown => shown.UserId == userId && shown.MovieId == id)
            .OrderByDescending(shown => shown.ServedAt)
            .Select(shown => (Guid?)shown.Id)
            .FirstOrDefaultAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await StateAsync(db, userId, id, cancellationToken));
    }

    /// <summary>Removes a stored response, if there is one.</summary>
    internal static async Task<bool> WithdrawAsync(
        NextMovieDbContext db,
        Guid userId,
        Guid movieId,
        CancellationToken cancellationToken)
    {
        var response = await db.RecommendationResponses
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.MovieId == movieId,
                cancellationToken);

        if (response is null)
        {
            return false;
        }

        db.RecommendationResponses.Remove(response);

        return true;
    }

    /// <summary>Reads back where a film now stands for one person.</summary>
    internal static async Task<MovieResponseState> StateAsync(
        NextMovieDbContext db,
        Guid userId,
        Guid movieId,
        CancellationToken cancellationToken)
    {
        var response = await db.RecommendationResponses
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.MovieId == movieId,
                cancellationToken);

        var watched = await db.WatchHistory
            .AsNoTracking()
            .AnyAsync(entry => entry.UserId == userId && entry.MovieId == movieId, cancellationToken);

        return new MovieResponseState(
            MovieId: movieId,
            Response: response?.Kind.ToString(),
            Watched: watched,
            RespondedAt: response?.RespondedAt);
    }
}

/// <summary>The answers a client may send.</summary>
/// <remarks>
/// Parsed explicitly rather than bound as an enum, matching how
/// <c>RecommendationConfidence</c> already crosses this boundary. It also lets a
/// wrong value come back as a 400 naming what was allowed, instead of the
/// serializer's own message.
/// </remarks>
internal static class MovieResponseChoice
{
    public const string Saved = "Saved";
    public const string NotInterested = "NotInterested";
    public const string Seen = "Seen";

    public static readonly string[] All = [Saved, NotInterested, Seen];

    public static bool TryParse(string? value, out string choice)
    {
        choice = All.FirstOrDefault(
            allowed => string.Equals(allowed, value?.Trim(), StringComparison.OrdinalIgnoreCase),
            string.Empty);

        return choice.Length > 0;
    }
}

/// <summary>What the user wants to do about a film.</summary>
/// <param name="Response">One of <c>Saved</c>, <c>NotInterested</c> or <c>Seen</c>.</param>
public sealed record RespondToMovieRequest(string? Response);

/// <summary>Where a film stands for the signed-in user.</summary>
/// <param name="MovieId">The film.</param>
/// <param name="Response">
/// <c>Saved</c>, <c>NotInterested</c>, or null when there is no standing response —
/// including after <c>Seen</c>, which is recorded as a viewing rather than a
/// response.
/// </param>
/// <param name="Watched">Whether the user has any viewing of this film.</param>
/// <param name="RespondedAt">When the response was given, when there is one.</param>
public sealed record MovieResponseState(
    Guid MovieId,
    string? Response,
    bool Watched,
    DateTimeOffset? RespondedAt);

/// <summary>Responses shared by the film-response slices.</summary>
internal static class MovieResponseResults
{
    public static ProblemHttpResult NoLongerSignedIn() => TypedResults.Problem(
        title: "Not signed in",
        detail: "This session is no longer valid. Sign in again.",
        statusCode: StatusCodes.Status401Unauthorized);

    public static ProblemHttpResult FilmNotFound() => TypedResults.Problem(
        title: "Film not found",
        detail: "No film with that identifier is in the catalogue.",
        statusCode: StatusCodes.Status404NotFound);
}
