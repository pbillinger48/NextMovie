namespace NextMovie.Api.Domain.Streaming;

/// <summary>
/// Somewhere to ask where a film can be watched.
/// </summary>
/// <remarks>
/// An interface rather than a call to TMDb, because this is the part of the
/// product most likely to be replaced. TMDb's provider data is free, already
/// integrated, and good enough to answer "can I stream this" — but it gives no
/// per-title deep link and no indication of when a film leaves a service, and
/// paid sources do both.
/// <para>
/// Returning our own types rather than a vendor's keeps that a swap rather than a
/// migration: a second implementation and a DI registration, with nothing in the
/// domain or the database to change. It is the same boundary ADR-0003 drew around
/// password hashing and ADR-0005 around Google.
/// </para>
/// </remarks>
internal interface IAvailabilityProvider
{
    /// <summary>
    /// Asks where a film can be watched in one region.
    /// </summary>
    /// <returns>
    /// What is on offer, which may be nothing — a film genuinely unavailable in a
    /// region is a real answer. Null means the question could not be asked, which
    /// is different and must not be cached as absence.
    /// </returns>
    Task<FilmAvailability?> GetAvailabilityAsync(
        int tmdbId,
        string region,
        CancellationToken cancellationToken);
}

/// <summary>Where a film can be watched, in one region, as some source describes it.</summary>
/// <param name="Offers">Every way to watch it. Empty means genuinely unavailable there.</param>
/// <param name="Link">Where to send the viewer, when the source suggests somewhere.</param>
internal sealed record FilmAvailability(IReadOnlyList<FilmOffer> Offers, string? Link);

/// <summary>One way to watch a film on one service.</summary>
/// <param name="ProviderId">The service's identifier, in TMDb's numbering.</param>
/// <param name="ProviderName">Its display name.</param>
/// <param name="LogoPath">Relative logo path, when the source has one.</param>
/// <param name="Type">Subscription, free, ad-supported, rent or buy.</param>
internal sealed record FilmOffer(
    int ProviderId,
    string ProviderName,
    string? LogoPath,
    OfferType Type);
