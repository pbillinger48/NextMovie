using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Infrastructure.Tmdb;

/// <summary>
/// Answers "where can I watch this?" using TMDb's provider data.
/// </summary>
/// <remarks>
/// The anti-corruption boundary for availability. TMDb's shape — lists named
/// <c>flatrate</c>, <c>ads</c> and so on, absent rather than empty when there is
/// nothing — is translated here and never reaches the domain.
/// <para>
/// TMDb's data is JustWatch's, synced periodically. It is free and good enough to
/// say whether a film is on a service. It cannot say <em>when a film leaves</em>
/// one, and its link goes to a JustWatch page rather than to the film on the
/// service. Those are the two reasons to expect this class to be replaced one
/// day, and the reason it sits behind
/// <see cref="IAvailabilityProvider"/>.
/// </para>
/// </remarks>
internal sealed class TmdbAvailabilityProvider(
    ITmdbClient tmdb,
    ILogger<TmdbAvailabilityProvider> logger) : IAvailabilityProvider
{
    public async Task<FilmAvailability?> GetAvailabilityAsync(
        int tmdbId,
        string region,
        CancellationToken cancellationToken)
    {
        TmdbWatchProvidersResponse response;

        try
        {
            response = await tmdb.GetWatchProvidersAsync(tmdbId, cancellationToken);
        }
        catch (TmdbException exception)
        {
            // Null, not empty. "We could not ask" must never be cached as "it is
            // not available", or an outage would quietly tell people for a day
            // that nothing is streaming anywhere.
            logger.LogWarning(exception, "Could not fetch availability for film {TmdbId}", tmdbId);

            return null;
        }

        if (response.Results is null || !response.Results.TryGetValue(region, out var offers))
        {
            // TMDb omits countries entirely when a film is not offered there, so
            // an absent region is a real answer: nothing is available.
            return new FilmAvailability([], Link: null);
        }

        var collected = new List<FilmOffer>();

        Collect(collected, offers.Flatrate, OfferType.Subscription);
        Collect(collected, offers.Free, OfferType.Free);
        Collect(collected, offers.Ads, OfferType.Ads);
        Collect(collected, offers.Rent, OfferType.Rent);
        Collect(collected, offers.Buy, OfferType.Buy);

        return new FilmAvailability(collected, offers.Link);
    }

    /// <remarks>
    /// A service can appear under more than one heading — rentable and buyable
    /// from the same shop — and each is a distinct way to watch, so both are
    /// kept. What is dropped is a provider with no usable name, which cannot be
    /// shown to anyone.
    /// </remarks>
    private static void Collect(
        List<FilmOffer> collected,
        IReadOnlyList<TmdbProviderDto>? providers,
        OfferType type)
    {
        foreach (var provider in providers ?? [])
        {
            if (provider.ProviderId > 0 && !string.IsNullOrWhiteSpace(provider.ProviderName))
            {
                collected.Add(new FilmOffer(
                    provider.ProviderId,
                    provider.ProviderName.Trim(),
                    string.IsNullOrWhiteSpace(provider.LogoPath) ? null : provider.LogoPath,
                    type));
            }
        }
    }
}
