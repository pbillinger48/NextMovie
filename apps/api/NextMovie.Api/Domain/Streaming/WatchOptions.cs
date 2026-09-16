namespace NextMovie.Api.Domain.Streaming;

/// <summary>
/// What one person can actually do with one film tonight.
/// </summary>
/// <remarks>
/// The join between where a film is available and what somebody pays for. Pure,
/// so the rule that decides how much availability matters to a ranking is one
/// testable function rather than something spread through a query.
/// </remarks>
/// <param name="StreamingOn">
/// Services they subscribe to that carry it. Non-empty means they can press play.
/// </param>
/// <param name="StreamingElsewhere">Services that carry it but which they do not pay for.</param>
/// <param name="RentOrBuy">Services offering it for money on top.</param>
/// <param name="Known">
/// Whether availability has been looked up at all. False means we have not asked
/// yet, which must not be presented as "not available".
/// </param>
/// <param name="Link">Where to send the viewer, when the source suggests somewhere.</param>
public sealed record WatchOptions(
    IReadOnlyList<string> StreamingOn,
    IReadOnlyList<string> StreamingElsewhere,
    IReadOnlyList<string> RentOrBuy,
    bool Known,
    string? Link)
{
    /// <summary>What we know about a film nobody has looked up yet.</summary>
    public static readonly WatchOptions Unknown = new([], [], [], Known: false, Link: null);

    /// <summary>
    /// How much being watchable moves a recommendation.
    /// </summary>
    /// <remarks>
    /// ADR-0010 ranks on availability rather than filtering by it. Filtering is
    /// the literal reading of "a film you can stream right now", and it fails
    /// exactly when it matters: somebody with one subscription gets an empty
    /// page, and an outstanding film is hidden because it costs a few pounds.
    /// <para>
    /// These numbers are meant to reorder good films, not to rescue bad ones. The
    /// largest is smaller than the gap between a 7.0 and an 8.5, so a mediocre
    /// film on Netflix still loses to a great one you would have to rent — which
    /// is the right answer to "what should I watch", even when it is not the
    /// convenient one.
    /// </para>
    /// </remarks>
    public double RankingAdjustment => this switch
    {
        // On something they already pay for: the case the product exists for.
        { StreamingOn.Count: > 0 } => 0.10,

        // Streaming somewhere, on a service they have not ticked. Worth surfacing
        // — people do sign up, and free or ad-supported services need no
        // subscription at all.
        { StreamingElsewhere.Count: > 0 } => 0.04,

        // Available, for money. No help, no penalty.
        { RentOrBuy.Count: > 0 } => 0,

        // Not available where they are. Nudged down, not hidden: a great film
        // they cannot stream is still worth knowing exists.
        { Known: true } => -0.04,

        // Never looked up. Nothing is known, so nothing is claimed.
        _ => 0,
    };

    /// <summary>Whether they can start watching without paying again.</summary>
    public bool CanStreamNow => StreamingOn.Count > 0 || StreamingElsewhere.Count > 0;

    /// <summary>
    /// Works out what somebody can do with a film, given what they subscribe to.
    /// </summary>
    /// <param name="offers">Every way the film is offered in their region.</param>
    /// <param name="subscribedProviderIds">The services they pay for.</param>
    /// <param name="link">Where the source suggests sending them.</param>
    public static WatchOptions From(
        IReadOnlyList<AvailabilityOffer> offers,
        IReadOnlySet<int> subscribedProviderIds,
        string? link)
    {
        var streamingOn = new List<string>();
        var streamingElsewhere = new List<string>();
        var rentOrBuy = new List<string>();

        foreach (var offer in offers)
        {
            var name = offer.StreamingProvider?.Name ?? string.Empty;

            if (name.Length == 0)
            {
                continue;
            }

            var destination = OfferTypes.StreamsNow(offer.Type)
                ? subscribedProviderIds.Contains(offer.StreamingProviderId)
                    ? streamingOn
                    : streamingElsewhere
                : rentOrBuy;

            // A service can appear twice — rentable and buyable from one shop —
            // and nobody wants to read its name twice.
            if (!destination.Contains(name))
            {
                destination.Add(name);
            }
        }

        return new WatchOptions(
            [.. streamingOn.Order()],
            [.. streamingElsewhere.Order()],
            [.. rentOrBuy.Order()],
            Known: true,
            link);
    }
}
