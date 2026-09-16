using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Streaming;

namespace NextMovie.Api.Infrastructure.Persistence;

/// <summary>
/// Keeps a usable answer to "where can I watch this?" without asking every time.
/// </summary>
/// <remarks>
/// Read-through with a day's cache, the same pattern <c>GetMovieDetails</c> uses
/// for film details (ADR-0010). Availability is the most perishable data in the
/// product — films leave services monthly — so this is the shortest-lived cache we
/// keep, and the one most worth revisiting if it proves wrong too often.
/// </remarks>
internal sealed class AvailabilityCatalog(
    NextMovieDbContext db,
    IAvailabilityProvider provider,
    TimeProvider time,
    ILogger<AvailabilityCatalog> logger)
{
    /// <summary>
    /// How long an answer is trusted.
    /// </summary>
    /// <remarks>
    /// Catalogues change at roughly this granularity, and a day's staleness is
    /// the cost of not spending a network call per film per page view. Shortening
    /// it is the fix if people start being sent to services a film has left.
    /// </remarks>
    private static readonly TimeSpan StaysFresh = TimeSpan.FromHours(24);

    /// <summary>
    /// Availability for a set of films, refreshing whatever has gone stale.
    /// </summary>
    /// <remarks>
    /// Batched because the caller is usually a list: a page of twelve
    /// recommendations should cost one query and at most twelve upstream calls,
    /// not twelve queries.
    /// </remarks>
    public async Task<Dictionary<Guid, MovieAvailability>> ForFilmsAsync(
        IReadOnlyList<Movie> films,
        string region,
        CancellationToken cancellationToken)
    {
        if (films.Count == 0)
        {
            return [];
        }

        var filmIds = films.Select(film => film.Id).ToArray();

        var known = await db.MovieAvailability
            .Include(availability => availability.Offers)
            .ThenInclude(offer => offer.StreamingProvider)
            .Where(availability => filmIds.Contains(availability.MovieId) && availability.Region == region)
            .ToDictionaryAsync(availability => availability.MovieId, cancellationToken);

        var now = time.GetUtcNow();
        var stale = films
            .Where(film => !known.TryGetValue(film.Id, out var availability)
                || now - availability.RefreshedAt > StaysFresh)
            .ToList();

        foreach (var film in stale)
        {
            var refreshed = await RefreshAsync(film, region, known.GetValueOrDefault(film.Id), now, cancellationToken);

            if (refreshed is not null)
            {
                known[film.Id] = refreshed;
            }
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return known;
    }

    /// <summary>
    /// Fetches one film's availability and stores it.
    /// </summary>
    /// <returns>
    /// The stored answer, or the previous one when the source could not be
    /// reached — stale availability is more useful than none, and far more useful
    /// than telling somebody nothing is streaming because of an outage.
    /// </returns>
    private async Task<MovieAvailability?> RefreshAsync(
        Movie film,
        string region,
        MovieAvailability? existing,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fetched = await provider.GetAvailabilityAsync(film.TmdbId, region, cancellationToken);

        if (fetched is null)
        {
            logger.LogDebug("Availability unavailable for {TmdbId} in {Region}; keeping what we have", film.TmdbId, region);

            return existing;
        }

        await EnsureProvidersAsync(fetched.Offers, cancellationToken);

        var availability = existing;

        if (availability is null)
        {
            availability = new MovieAvailability
            {
                MovieId = film.Id,
                Region = region,
                RefreshedAt = now,
                Link = fetched.Link,
            };

            db.MovieAvailability.Add(availability);
        }
        else
        {
            availability.RefreshedAt = now;
            availability.Link = fetched.Link;

            // Replaced wholesale rather than merged: an offer that has gone is
            // the single most important change this data carries, and merging
            // would keep a film on a service it left.
            db.RemoveRange(availability.Offers);
            availability.Offers.Clear();
        }

        foreach (var offer in fetched.Offers)
        {
            var row = new AvailabilityOffer
            {
                MovieAvailabilityId = availability.Id,
                StreamingProviderId = offer.ProviderId,
                Type = offer.Type,
            };

            availability.Offers.Add(row);

            // Tracked explicitly as well as attached to the navigation. Offer ids
            // are generated in application code, so a new row reached only
            // through a tracked parent looks to EF like an existing one — it has
            // a key — and change tracking marks it Modified, producing an UPDATE
            // against a row that was never inserted.
            db.Add(row);
        }

        return availability;
    }

    /// <remarks>
    /// Services arrive with the availability that mentions them rather than from
    /// a seeded list: which services exist differs by region and changes, so a
    /// seed would be wrong somewhere from the day it was written.
    /// </remarks>
    private async Task EnsureProvidersAsync(
        IReadOnlyList<FilmOffer> offers,
        CancellationToken cancellationToken)
    {
        var ids = offers.Select(offer => offer.ProviderId).Distinct().ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        var known = await db.StreamingProviders
            .Where(provider => ids.Contains(provider.Id))
            .ToDictionaryAsync(provider => provider.Id, cancellationToken);

        foreach (var offer in offers.DistinctBy(offer => offer.ProviderId))
        {
            if (known.TryGetValue(offer.ProviderId, out var existing))
            {
                // Names and logos change; keeping them current costs nothing.
                existing.Name = offer.ProviderName;
                existing.LogoPath = offer.LogoPath;

                continue;
            }

            db.StreamingProviders.Add(new StreamingProvider
            {
                Id = offer.ProviderId,
                Name = offer.ProviderName,
                LogoPath = offer.LogoPath,
            });

            known[offer.ProviderId] = null!;
        }

        // Saved before the offers that reference them, since the foreign key
        // demands the provider exists first.
        await db.SaveChangesAsync(cancellationToken);
    }
}
