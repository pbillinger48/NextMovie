namespace NextMovie.Api.Infrastructure.Tmdb.Dtos;

/// <summary>
/// TMDb's <c>/movie/{id}</c> response, in TMDb's own shape.
/// </summary>
/// <remarks>
/// Internal and confined to <c>Infrastructure.Tmdb</c> for the same reason as
/// every other DTO here: this is a third party's wire format, and letting it
/// reach the domain would couple our contract to theirs.
/// <para>
/// Models only the fields the catalogue stores. TMDb returns a good deal more —
/// budget, revenue, production companies — and adding them here without a column
/// to put them in would be noise.
/// </para>
/// </remarks>
internal sealed record TmdbMovieDetailsResponse
{
    public int Id { get; init; }

    public string? Title { get; init; }

    public string? OriginalTitle { get; init; }

    public string? Overview { get; init; }

    public string? PosterPath { get; init; }

    public string? BackdropPath { get; init; }

    /// <summary>ISO date, or an empty string — TMDb does not use null here.</summary>
    public string? ReleaseDate { get; init; }

    /// <summary>Minutes. Null or zero for films with no known runtime.</summary>
    public int? Runtime { get; init; }

    public double? VoteAverage { get; init; }

    public int VoteCount { get; init; }

    public double? Popularity { get; init; }

    public string? OriginalLanguage { get; init; }

    /// <summary>e.g. <c>Released</c>, <c>Post Production</c>.</summary>
    public string? Status { get; init; }

    /// <summary>
    /// Full genre objects, unlike search, which returns bare ids.
    /// </summary>
    public IReadOnlyList<TmdbGenreDto>? Genres { get; init; }
}

/// <summary>A genre as TMDb's details endpoint returns it.</summary>
internal sealed record TmdbGenreDto
{
    public int Id { get; init; }

    public string? Name { get; init; }
}
