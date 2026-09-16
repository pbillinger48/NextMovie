namespace NextMovie.Api.Domain.Streaming;

/// <summary>
/// A service on offer in one country.
/// </summary>
/// <remarks>
/// Which services exist is a fact about a country, not about a service, so it
/// cannot live on <see cref="StreamingProvider"/>: Hulu is in the United States
/// and nowhere else, and Sky Go in Britain and Ireland. The same provider row is
/// referenced from as many regions as offer it.
/// </remarks>
public class RegionalProvider
{
    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public required string Region { get; init; }

    public required int StreamingProviderId { get; init; }

    public StreamingProvider StreamingProvider { get; init; } = null!;

    /// <summary>
    /// Ordering hint from the source — lower is more prominent in this country.
    /// </summary>
    /// <remarks>
    /// Kept because the alternative is alphabetical, which puts Apple TV and
    /// Amazon above Netflix and buries the services almost everyone actually has.
    /// </remarks>
    public int DisplayPriority { get; set; }
}

/// <summary>
/// When a country's service catalogue was last fetched.
/// </summary>
/// <remarks>
/// A row of its own rather than a timestamp on each <see cref="RegionalProvider"/>
/// so that "asked, and the answer was nothing" can be recorded. Taking the newest
/// child row's timestamp instead would leave an empty or unrecognised region
/// looking permanently stale, and it would be re-fetched on every page view.
/// </remarks>
public class RegionCatalog
{
    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public required string Region { get; init; }

    public required DateTimeOffset RefreshedAt { get; set; }
}
