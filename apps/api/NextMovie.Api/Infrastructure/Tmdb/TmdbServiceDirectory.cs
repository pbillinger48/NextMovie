using NextMovie.Api.Domain.Streaming;

namespace NextMovie.Api.Infrastructure.Tmdb;

/// <summary>Answers <see cref="IServiceDirectory"/> from TMDb.</summary>
/// <remarks>
/// The anti-corruption boundary for the provider catalogue: TMDb's DTOs stop
/// here and <see cref="RegionalService"/> continues.
/// </remarks>
internal sealed class TmdbServiceDirectory(ITmdbClient tmdb, ILogger<TmdbServiceDirectory> logger)
    : IServiceDirectory
{
    public async Task<IReadOnlyList<RegionalService>?> GetServicesAsync(
        string region,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await tmdb.GetProvidersAsync(region, cancellationToken);

            return response.Results is null
                ? []
                : response.Results
                    // A service with no name cannot be offered as a checkbox, and
                    // there is nothing sensible to label it with.
                    .Where(item => !string.IsNullOrWhiteSpace(item.ProviderName))
                    .Select(item => new RegionalService(
                        item.ProviderId,
                        item.ProviderName!,
                        item.LogoPath,
                        item.DisplayPriority))
                    .DistinctBy(service => service.ProviderId)
                    .ToList();
        }
        catch (TmdbException ex)
        {
            // Null, not empty. An outage must not read as "this country has no
            // streaming services", which would wipe the settings page.
            logger.LogWarning(ex, "Could not fetch the service catalogue for {Region}", region);

            return null;
        }
    }
}
