namespace NextMovie.Api.Infrastructure.Tmdb.Dtos;

/// <summary>
/// TMDb's <c>/search/movie</c> response, in TMDb's own shape.
/// </summary>
/// <remarks>
/// These types are deliberately <c>internal</c> and confined to
/// <c>Infrastructure.Tmdb</c>. They model a third party's wire format, which can
/// change without notice; letting them escape into the domain or the API surface
/// would couple our contract to theirs. <see cref="TmdbMovieMapper"/> is the only
/// thing that reads them.
/// </remarks>
internal sealed record TmdbSearchResponse
{
    public int Page { get; init; }

    public IReadOnlyList<TmdbMovieDto> Results { get; init; } = [];

    public int TotalPages { get; init; }

    public int TotalResults { get; init; }
}

/// <summary>A single film as returned by TMDb search.</summary>
internal sealed record TmdbMovieDto
{
    public int Id { get; init; }

    public string? Title { get; init; }

    public string? OriginalTitle { get; init; }

    public string? Overview { get; init; }

    /// <summary>Relative path such as <c>/abc123.jpg</c>, or null. Not a full URL.</summary>
    public string? PosterPath { get; init; }

    /// <summary>Relative path such as <c>/xyz789.jpg</c>, or null. Not a full URL.</summary>
    public string? BackdropPath { get; init; }

    /// <summary>
    /// ISO date, or an <em>empty string</em> for films with no announced date.
    /// TMDb does not use null here, which is the single most common cause of
    /// parse failures against this API.
    /// </summary>
    public string? ReleaseDate { get; init; }

    /// <summary>Community rating 0–10. Reported as <c>0</c> when nobody has voted.</summary>
    public double? VoteAverage { get; init; }

    /// <summary>Number of votes behind <see cref="VoteAverage"/>. Zero means the rating is meaningless.</summary>
    public int VoteCount { get; init; }

    public double? Popularity { get; init; }

    /// <summary>ISO 639-1 code, e.g. <c>en</c>.</summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>TMDb genre identifiers. Search returns ids only, never full genre objects.</summary>
    public IReadOnlyList<int>? GenreIds { get; init; }

    public bool Adult { get; init; }
}
