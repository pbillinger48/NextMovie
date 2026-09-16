using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Streaming;

/// <summary>Where a person watches, and what they pay for.</summary>
/// <param name="Region">The country their availability is answered for, as ISO 3166-1 alpha-2.</param>
/// <param name="Countries">Countries they may choose between, ordered by name.</param>
/// <param name="Services">Services they may tick, most prominent in their country first.</param>
public sealed record StreamingSettingsResponse(
    string Region,
    IReadOnlyList<CountryOption> Countries,
    IReadOnlyList<StreamingServiceOption> Services);

/// <summary>One country on offer.</summary>
/// <param name="Code">ISO 3166-1 alpha-2 code.</param>
/// <param name="Name">English name, for display.</param>
public sealed record CountryOption(string Code, string Name);

/// <summary>One service, and whether this person says they have it.</summary>
/// <param name="Id">Provider identifier, the same one offers on films carry.</param>
/// <param name="Name">Name to show.</param>
/// <param name="LogoPath">Relative logo path, when the source has one.</param>
/// <param name="Subscribed">Whether this person has said they subscribe.</param>
/// <param name="OfferedHere">
/// Whether the service operates in their country. False only for something they
/// ticked before moving — see the remarks on <see cref="StreamingSettings"/>.
/// </param>
public sealed record StreamingServiceOption(
    int Id,
    string Name,
    string? LogoPath,
    bool Subscribed,
    bool OfferedHere);

/// <summary>Builds the settings view shared by reading and writing it.</summary>
/// <remarks>
/// Both slices return the same resource, and a <c>PUT</c> that answered in a
/// different shape from the <c>GET</c> before it would force every client to
/// re-read after every save.
/// </remarks>
internal static class StreamingSettings
{
    /// <summary>
    /// Assembles the current settings for one user.
    /// </summary>
    /// <remarks>
    /// The list is the country's catalogue plus anything this person subscribes
    /// to that is not in it, flagged rather than dropped. Someone who ticks a
    /// service and then changes country would otherwise watch their choice
    /// disappear with no explanation and no way to untick it — the row would still
    /// be in the database, still shaping recommendations, and invisible.
    /// </remarks>
    public static async Task<StreamingSettingsResponse> ForAsync(
        User user,
        ServiceCatalog catalog,
        NextMovieDbContext db,
        CancellationToken cancellationToken)
    {
        var offered = await catalog.ForRegionAsync(user.Region, cancellationToken);

        var subscribedIds = await db.UserStreamingProviders
            .AsNoTracking()
            .Where(subscription => subscription.UserId == user.Id)
            .Select(subscription => subscription.StreamingProviderId)
            .ToListAsync(cancellationToken);

        var subscribed = subscribedIds.ToHashSet();
        var offeredIds = offered.Select(provider => provider.Id).ToHashSet();

        var elsewhere = await db.StreamingProviders
            .AsNoTracking()
            .Where(provider => subscribed.Contains(provider.Id) && !offeredIds.Contains(provider.Id))
            .OrderBy(provider => provider.Name)
            .ToListAsync(cancellationToken);

        var services = offered
            .Select(provider => new StreamingServiceOption(
                provider.Id,
                provider.Name,
                provider.LogoPath,
                Subscribed: subscribed.Contains(provider.Id),
                OfferedHere: true))
            .Concat(elsewhere.Select(provider => new StreamingServiceOption(
                provider.Id,
                provider.Name,
                provider.LogoPath,
                Subscribed: true,
                OfferedHere: false)))
            .ToList();

        return new StreamingSettingsResponse(
            user.Region,
            Domain.Streaming.Countries.All
                .Select(country => new CountryOption(country.Code, country.Name))
                .ToList(),
            services);
    }
}
