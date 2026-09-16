namespace NextMovie.Api.Infrastructure.Tmdb.Dtos;

/// <summary>
/// TMDb's <c>/movie/{id}/watch/providers</c> response, in TMDb's own shape.
/// </summary>
/// <remarks>
/// Internal and confined to <c>Infrastructure.Tmdb</c>, like every other DTO
/// here. This one especially: the data underneath is JustWatch's, reshaped by
/// TMDb, and a paid source would present the same facts completely differently.
/// </remarks>
internal sealed record TmdbWatchProvidersResponse
{
    public int Id { get; init; }

    /// <summary>Keyed by ISO 3166-1 alpha-2 country code.</summary>
    public IReadOnlyDictionary<string, TmdbRegionProviders>? Results { get; init; }
}

/// <summary>What is on offer in one country.</summary>
/// <remarks>
/// Each list is absent rather than empty when a film is not offered that way, so
/// every one is nullable.
/// </remarks>
internal sealed record TmdbRegionProviders
{
    /// <summary>Where TMDb suggests sending the viewer. A JustWatch page, not the service itself.</summary>
    public string? Link { get; init; }

    /// <summary>Included with a subscription. TMDb's name for it, kept here and translated at the boundary.</summary>
    public IReadOnlyList<TmdbProviderDto>? Flatrate { get; init; }

    public IReadOnlyList<TmdbProviderDto>? Free { get; init; }

    public IReadOnlyList<TmdbProviderDto>? Ads { get; init; }

    public IReadOnlyList<TmdbProviderDto>? Rent { get; init; }

    public IReadOnlyList<TmdbProviderDto>? Buy { get; init; }
}

/// <summary>One service, as TMDb describes it.</summary>
internal sealed record TmdbProviderDto
{
    public int ProviderId { get; init; }

    public string? ProviderName { get; init; }

    public string? LogoPath { get; init; }
}
