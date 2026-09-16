namespace NextMovie.Api.Domain.Streaming;

/// <summary>
/// Where one film can be watched, in one region, as of one moment.
/// </summary>
/// <remarks>
/// Keyed on film <b>and region</b> because that is the grain the data has: a film
/// on Netflix in the United States is frequently not on Netflix in the United
/// Kingdom, and a model without region would be wrong for everyone outside
/// whichever country it silently assumed.
/// <para>
/// A row with no offers is meaningful and is not the same as no row at all. The
/// first says "we asked, and it is not available here"; the second says "we have
/// never asked". Without the distinction, every unavailable film would be
/// re-fetched on every request forever.
/// </para>
/// </remarks>
public class MovieAvailability
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid MovieId { get; init; }

    public Movie Movie { get; init; } = null!;

    /// <summary>ISO 3166-1 alpha-2 country code, upper case.</summary>
    public required string Region { get; init; }

    /// <summary>
    /// When this was last fetched.
    /// </summary>
    /// <remarks>
    /// The most perishable data in the product: films leave services monthly, so
    /// this is what decides when to ask again. See ADR-0010 for why a day is the
    /// window.
    /// </remarks>
    public required DateTimeOffset RefreshedAt { get; set; }

    /// <summary>Where the provider suggests sending the viewer, when it offers one.</summary>
    public string? Link { get; set; }

    public ICollection<AvailabilityOffer> Offers { get; init; } = [];
}

/// <summary>One way to watch a film on one service.</summary>
public class AvailabilityOffer
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid MovieAvailabilityId { get; init; }

    public MovieAvailability MovieAvailability { get; init; } = null!;

    public required int StreamingProviderId { get; init; }

    public StreamingProvider StreamingProvider { get; init; } = null!;

    public required OfferType Type { get; init; }
}
