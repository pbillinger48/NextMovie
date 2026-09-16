using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Streaming;

namespace NextMovie.Api.Infrastructure.Persistence;

/// <summary>
/// Which services a person in a given country can be asked about.
/// </summary>
/// <remarks>
/// Read-through with a long cache, the same shape as <see cref="AvailabilityCatalog"/>
/// but with the opposite lifetime. Which films are on Netflix changes weekly;
/// whether Netflix exists in Britain changes approximately never, so this is
/// fetched once a month rather than once a day.
/// </remarks>
internal sealed class ServiceCatalog(
    NextMovieDbContext db,
    IServiceDirectory directory,
    TimeProvider time,
    ILogger<ServiceCatalog> logger)
{
    /// <summary>
    /// How long a country's catalogue is trusted.
    /// </summary>
    /// <remarks>
    /// Services launch and close a few times a year. A month of staleness costs
    /// somebody the ability to tick a service that launched last week — mildly
    /// annoying and self-correcting — while a short window would spend an upstream
    /// call every time anyone opened their settings.
    /// </remarks>
    private static readonly TimeSpan StaysFresh = TimeSpan.FromDays(30);

    /// <summary>
    /// The services on offer in one country, most prominent first.
    /// </summary>
    /// <remarks>
    /// Returns whatever is stored when the source cannot be reached, including an
    /// empty list on the very first attempt. A settings page that lists nothing is
    /// a poor experience; one that fails to load is a worse one, and the ticks
    /// somebody already saved stay saved either way.
    /// </remarks>
    public async Task<IReadOnlyList<StreamingProvider>> ForRegionAsync(
        string region,
        CancellationToken cancellationToken)
    {
        var catalog = await db.RegionCatalogs
            .FirstOrDefaultAsync(entry => entry.Region == region, cancellationToken);

        var now = time.GetUtcNow();

        if (catalog is null || now - catalog.RefreshedAt > StaysFresh)
        {
            await RefreshAsync(region, catalog, now, cancellationToken);
        }

        return await db.RegionalProviders
            .AsNoTracking()
            .Where(offered => offered.Region == region)
            .OrderBy(offered => offered.DisplayPriority)
            .ThenBy(offered => offered.StreamingProvider.Name)
            .Select(offered => offered.StreamingProvider)
            .ToListAsync(cancellationToken);
    }

    private async Task RefreshAsync(
        string region,
        RegionCatalog? catalog,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fetched = await directory.GetServicesAsync(region, cancellationToken);

        if (fetched is null)
        {
            // Nothing is stamped, so the next request tries again rather than
            // treating an outage as a fresh answer for the next month.
            logger.LogDebug("Service catalogue unavailable for {Region}; keeping what we have", region);

            return;
        }

        var providerIds = fetched.Select(service => service.ProviderId).ToArray();

        var known = await db.StreamingProviders
            .Where(provider => providerIds.Contains(provider.Id))
            .ToDictionaryAsync(provider => provider.Id, cancellationToken);

        foreach (var service in fetched)
        {
            if (known.TryGetValue(service.ProviderId, out var provider))
            {
                provider.Name = service.Name;
                provider.LogoPath = service.LogoPath;

                continue;
            }

            db.StreamingProviders.Add(new StreamingProvider
            {
                Id = service.ProviderId,
                Name = service.Name,
                LogoPath = service.LogoPath,
            });
        }

        // Providers must exist before anything references them, and the region
        // rows below are exactly such a reference.
        await db.SaveChangesAsync(cancellationToken);

        var offered = await db.RegionalProviders
            .Where(entry => entry.Region == region)
            .ToDictionaryAsync(entry => entry.StreamingProviderId, cancellationToken);

        foreach (var service in fetched)
        {
            if (offered.Remove(service.ProviderId, out var existing))
            {
                existing.DisplayPriority = service.DisplayPriority;

                continue;
            }

            db.RegionalProviders.Add(new RegionalProvider
            {
                Region = region,
                StreamingProviderId = service.ProviderId,
                DisplayPriority = service.DisplayPriority,
            });
        }

        // Whatever the source no longer lists has left the country. Removing it
        // is the point of refreshing at all — a service somebody can still tick
        // after it has closed makes every recommendation built on it wrong.
        db.RegionalProviders.RemoveRange(offered.Values);

        if (catalog is null)
        {
            db.RegionCatalogs.Add(new RegionCatalog { Region = region, RefreshedAt = now });
        }
        else
        {
            catalog.RefreshedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
