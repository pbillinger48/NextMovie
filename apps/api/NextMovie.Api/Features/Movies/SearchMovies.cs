using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NextMovie.Api.Domain;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Infrastructure.Tmdb;

namespace NextMovie.Api.Features.Movies;

/// <summary>
/// Searches for films by title.
/// </summary>
/// <remarks>
/// Read-through: results come from TMDb, are written into our catalogue, and are
/// returned from our own domain model. Callers therefore only ever see NextMovie
/// identifiers and NextMovie shapes, and the catalogue grows as people search.
/// </remarks>
public static class SearchMovies
{
    // TMDb refuses page numbers above 500, so rejecting them here turns a
    // confusing upstream 400 into a clear validation error.
    private const int MaxPage = 500;
    private const int MaxTitleLength = 200;

    /// <summary>Registers the movie search endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/movies/search", HandleAsync)
            .WithName(nameof(SearchMovies))
            .WithSummary("Search films by title")
            .WithDescription(
                "Searches TMDb by title, stores the results in the NextMovie catalogue, "
                + "and returns them ranked by relevance.");

        return app;
    }

    private static async Task<Results<Ok<SearchMoviesResponse>, ValidationProblem>> HandleAsync(
        [AsParameters] SearchMoviesRequest request,
        ITmdbClient tmdb,
        MovieCatalog catalog,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var page = request.Page ?? 1;

        var searchResults = await tmdb.SearchMoviesAsync(request.Title!.Trim(), page, cancellationToken);

        var mapped = new List<MappedMovie>(searchResults.Results.Count);
        foreach (var dto in searchResults.Results)
        {
            var candidate = TmdbMovieMapper.ToDomain(dto);
            if (candidate is null)
            {
                // One unusable record should cost that record, not the search.
                logger.LogWarning("Skipping unmappable TMDb result with id {TmdbId}", dto.Id);
                continue;
            }

            mapped.Add(candidate);
        }

        var stored = await catalog.UpsertAsync(mapped, cancellationToken);

        return TypedResults.Ok(new SearchMoviesResponse(
            Page: searchResults.Page,
            TotalPages: searchResults.TotalPages,
            TotalResults: searchResults.TotalResults,
            Results: [.. stored.Select(ToSummary)]));
    }

    private static Dictionary<string, string[]> Validate(SearchMoviesRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["A title is required."];
        }
        else if (request.Title.Trim().Length > MaxTitleLength)
        {
            errors[nameof(request.Title)] = [$"A title may be at most {MaxTitleLength} characters."];
        }

        if (request.Page is { } page && (page < 1 || page > MaxPage))
        {
            errors[nameof(request.Page)] = [$"Page must be between 1 and {MaxPage}."];
        }

        return errors;
    }

    private static MovieSummary ToSummary(Movie movie) => new(
        Id: movie.Id,
        TmdbId: movie.TmdbId,
        Title: movie.Title,
        Overview: movie.Overview,
        PosterPath: movie.PosterPath,
        ReleaseDate: movie.ReleaseDate,
        AverageRating: movie.AverageRating,
        Genres: [.. movie.Genres.Select(g => g.Name).Order()]);
}

/// <summary>Query parameters for a film search.</summary>
/// <param name="Title">Title to search for.</param>
/// <param name="Page">1-based page number. Defaults to 1.</param>
public sealed record SearchMoviesRequest(
    [FromQuery] string? Title,
    [FromQuery] int? Page);

/// <summary>A page of search results.</summary>
/// <param name="Page">The page returned.</param>
/// <param name="TotalPages">Total pages available for this query.</param>
/// <param name="TotalResults">Total matching films.</param>
/// <param name="Results">Films on this page, ordered by relevance.</param>
public sealed record SearchMoviesResponse(
    int Page,
    int TotalPages,
    int TotalResults,
    IReadOnlyList<MovieSummary> Results);

/// <summary>Summary view of a film.</summary>
/// <param name="Id">NextMovie identifier. Stable, and the one clients should store.</param>
/// <param name="TmdbId">TMDb identifier, exposed for attribution and deep links.</param>
/// <param name="Title">Display title.</param>
/// <param name="Overview">Synopsis, when TMDb has one.</param>
/// <param name="PosterPath">Relative TMDb poster path; combine with a TMDb image base URL to render.</param>
/// <param name="ReleaseDate">Release date, when known.</param>
/// <param name="AverageRating">TMDb community rating 0–10. Null when the film has no votes.</param>
/// <param name="Genres">Genre names, alphabetically.</param>
public sealed record MovieSummary(
    Guid Id,
    int TmdbId,
    string Title,
    string? Overview,
    string? PosterPath,
    DateOnly? ReleaseDate,
    double? AverageRating,
    IReadOnlyList<string> Genres);
