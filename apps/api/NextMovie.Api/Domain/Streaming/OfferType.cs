namespace NextMovie.Api.Domain.Streaming;

/// <summary>How a film is available on a service.</summary>
/// <remarks>
/// The distinction is the product's, not a technicality. "Stream right now" means
/// a film someone can start watching on something they already pay for — see
/// <see cref="OfferTypes.StreamsNow"/>. Renting is watching after paying again,
/// and describing a purchase as streaming would be a lie the product's own
/// sentence does not support.
/// </remarks>
public enum OfferType
{
    /// <summary>Included with a subscription. TMDb calls this <c>flatrate</c>.</summary>
    Subscription = 1,

    /// <summary>Free, no subscription and no advertising.</summary>
    Free = 2,

    /// <summary>Free with advertising.</summary>
    Ads = 3,

    /// <summary>Available to rent.</summary>
    Rent = 4,

    /// <summary>Available to buy.</summary>
    Buy = 5,
}

/// <summary>Which offers count as watchable tonight.</summary>
public static class OfferTypes
{
    /// <summary>
    /// The offers that mean "you can press play", given a subscription.
    /// </summary>
    public static bool StreamsNow(OfferType type) =>
        type is OfferType.Subscription or OfferType.Free or OfferType.Ads;
}
