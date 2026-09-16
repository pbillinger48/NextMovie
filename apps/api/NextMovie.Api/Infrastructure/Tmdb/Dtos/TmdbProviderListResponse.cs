namespace NextMovie.Api.Infrastructure.Tmdb.Dtos;

/// <summary>
/// TMDb's <c>/watch/providers/movie</c> response, in TMDb's own shape.
/// </summary>
/// <remarks>
/// Every service TMDb knows of in one country. Unlike the per-film endpoint this
/// is a catalogue rather than an answer about a film, and it changes on the order
/// of months.
/// </remarks>
internal sealed record TmdbProviderListResponse
{
    public IReadOnlyList<TmdbProviderListItem>? Results { get; init; }
}

/// <summary>One service in a region's catalogue.</summary>
internal sealed record TmdbProviderListItem
{
    public int ProviderId { get; init; }

    public string? ProviderName { get; init; }

    public string? LogoPath { get; init; }

    /// <summary>
    /// TMDb's ordering hint for the requested region — lower is more prominent.
    /// </summary>
    /// <remarks>
    /// Region-specific despite the flat name: TMDb fills this in from the
    /// <c>watch_region</c> query parameter. It is the only signal in the payload
    /// about which services matter where, and without it the list arrives in an
    /// order that puts obscure regional services above Netflix.
    /// </remarks>
    public int DisplayPriority { get; init; }
}
