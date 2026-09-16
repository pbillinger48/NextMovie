using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using NextMovie.Api.Domain.Recommendations;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Features.Streaming;
using NextMovie.Api.Infrastructure.Auth;

namespace NextMovie.Api.Features.Recommendations;

/// <summary>
/// Recommends films the signed-in user has not seen.
/// </summary>
/// <remarks>
/// Candidates come from TMDb's relatedness, seeded from the films this person
/// rated most highly; the ranking, filtering and explanation are ours (ADR-0008).
/// <para>
/// Note what this deliberately does <b>not</b> answer: where to watch anything.
/// Streaming availability is out of the first version, and the response says so
/// rather than leaving clients to imply otherwise.
/// </para>
/// </remarks>
public static class GetRecommendations
{
    private const int DefaultCount = 10;

    private const int MaxCount = 30;

    /// <summary>Registers the recommendations endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/recommendations", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(GetRecommendations))
            .WithSummary("Recommend films")
            .WithDescription(
                "Returns films the signed-in user has not seen, ranked against their "
                + "viewing and rating history, each with the reasons behind its ranking.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<RecommendationsResponse>, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal caller,
        RecommendationEngine engine,
        int? count,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return TypedResults.Problem(
                title: "Not signed in",
                detail: "This session is no longer valid. Sign in again.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var recommendations = await engine.RecommendAsync(
            userId,
            Math.Clamp(count ?? DefaultCount, 1, MaxCount),
            cancellationToken);

        return TypedResults.Ok(new RecommendationsResponse(
            [.. recommendations.Select(ToResponse)]));
    }

    private static RecommendedFilm ToResponse(Recommendation recommendation) => new(
        MovieId: recommendation.Movie.Id,
        Title: recommendation.Movie.Title,
        PosterPath: recommendation.Movie.PosterPath,
        ReleaseDate: recommendation.Movie.ReleaseDate,
        Runtime: recommendation.Movie.Runtime,
        AverageRating: recommendation.Movie.AverageRating,
        Genres: [.. recommendation.Movie.Genres.Select(genre => genre.Name).Order()],

        // Rank, not a percentage. The scores of a returned set sit within a few
        // points of each other, and a 70% beside a 66% implies a precision this
        // model does not have — it invites a comparison it cannot support. The
        // order and the reasons are the parts that mean something.
        Rank: recommendation.Rank,
        Confidence: recommendation.Scored.Confidence.ToString(),
        Reasons: recommendation.Scored.Reasons,
        Watch: new WatchingOptions(
            StreamingOn: recommendation.Watch.StreamingOn,
            StreamingElsewhere: recommendation.Watch.StreamingElsewhere,
            RentOrBuy: recommendation.Watch.RentOrBuy,
            CanStreamNow: recommendation.Watch.CanStreamNow,
            Known: recommendation.Watch.Known,
            Link: recommendation.Watch.Link));
}

/// <summary>Films worth watching next.</summary>
/// <param name="Recommendations">
/// Ranked, best first. Empty when there is not enough history to reason from —
/// which is an honest answer rather than a fallback to whatever is popular.
/// </param>
public sealed record RecommendationsResponse(IReadOnlyList<RecommendedFilm> Recommendations);

/// <summary>One recommended film, and why.</summary>
/// <param name="MovieId">NextMovie identifier.</param>
/// <param name="Title">Display title.</param>
/// <param name="PosterPath">Relative TMDb poster path.</param>
/// <param name="ReleaseDate">Release date, when known.</param>
/// <param name="Runtime">Runtime in minutes, when known.</param>
/// <param name="AverageRating">TMDb community rating 0–10.</param>
/// <param name="Genres">Genre names, alphabetically.</param>
/// <param name="Rank">
/// Position in this response, from 1. Deliberately not a match percentage: the
/// scores behind a list sit within a few points of each other, and publishing
/// them would suggest a precision the model does not have.
/// </param>
/// <param name="Confidence">
/// <c>High</c>, <c>Medium</c> or <c>Low</c> — how much history stands behind the
/// judgement, including whether this person has watched anything in the film's
/// genres.
/// </param>
/// <param name="Reasons">
/// Why it ranked where it did, drawn from the factors that actually moved it.
/// Possibly empty; never invented.
/// </param>
/// <param name="Watch">Where the viewer can watch it, in their region.</param>
public sealed record RecommendedFilm(
    Guid MovieId,
    string Title,
    string? PosterPath,
    DateOnly? ReleaseDate,
    int? Runtime,
    double? AverageRating,
    IReadOnlyList<string> Genres,
    int Rank,
    string Confidence,
    IReadOnlyList<string> Reasons,
    WatchingOptions Watch);

