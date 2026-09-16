using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Streaming;

namespace NextMovie.Api.Tests.Domain.Streaming;

/// <summary>
/// Tests what a person can do with a film, and how much it should matter.
/// </summary>
/// <remarks>
/// ADR-0010 ranks on availability rather than filtering by it, and the size of
/// the nudge is the whole decision. Too large and a mediocre film on Netflix beats
/// a great one you would have to rent; too small and the product does not keep the
/// promise in its own sentence.
/// </remarks>
public sealed class WatchOptionsTests
{
    private const int Netflix = 8;
    private const int Max = 1899;
    private const int AppleTv = 2;

    private static AvailabilityOffer Offer(int providerId, string name, OfferType type) => new()
    {
        MovieAvailabilityId = Guid.CreateVersion7(),
        StreamingProviderId = providerId,
        Type = type,
        StreamingProvider = new StreamingProvider { Id = providerId, Name = name },
    };

    private static WatchOptions For(IReadOnlyList<AvailabilityOffer> offers, params int[] subscriptions) =>
        WatchOptions.From(offers, subscriptions.ToHashSet(), link: null);

    [Fact]
    public void A_service_you_pay_for_means_you_can_press_play()
    {
        var options = For([Offer(Netflix, "Netflix", OfferType.Subscription)], Netflix);

        Assert.Equal(["Netflix"], options.StreamingOn);
        Assert.True(options.CanStreamNow);
    }

    [Fact]
    public void A_service_you_do_not_pay_for_is_listed_separately()
    {
        var options = For([Offer(Max, "Max", OfferType.Subscription)], Netflix);

        Assert.Empty(options.StreamingOn);
        Assert.Equal(["Max"], options.StreamingElsewhere);

        // Still streamable — people do sign up, and free services need nothing.
        Assert.True(options.CanStreamNow);
    }

    [Fact]
    public void Renting_is_not_streaming()
    {
        var options = For([Offer(AppleTv, "Apple TV", OfferType.Rent)], AppleTv);

        // Describing a payment as "watch it tonight" would be a lie the product's
        // own sentence does not support.
        Assert.False(options.CanStreamNow);
        Assert.Equal(["Apple TV"], options.RentOrBuy);
    }

    [Fact]
    public void A_shop_offering_both_rent_and_buy_is_named_once()
    {
        var options = For(
        [
            Offer(AppleTv, "Apple TV", OfferType.Rent),
            Offer(AppleTv, "Apple TV", OfferType.Buy),
        ]);

        Assert.Equal(["Apple TV"], options.RentOrBuy);
    }

    [Fact]
    public void Free_and_ad_supported_services_count_as_streaming()
    {
        var options = For(
        [
            Offer(300, "Tubi", OfferType.Ads),
            Offer(301, "Kanopy", OfferType.Free),
        ]);

        Assert.True(options.CanStreamNow);
        Assert.Equal(2, options.StreamingElsewhere.Count);
    }

    // --- how much it moves the ranking ---

    [Fact]
    public void Being_on_something_you_pay_for_helps_most()
    {
        var subscribed = For([Offer(Netflix, "Netflix", OfferType.Subscription)], Netflix);
        var elsewhere = For([Offer(Max, "Max", OfferType.Subscription)], Netflix);

        Assert.True(subscribed.RankingAdjustment > elsewhere.RankingAdjustment);
    }

    [Fact]
    public void Rent_only_neither_helps_nor_hurts()
    {
        Assert.Equal(0, For([Offer(AppleTv, "Apple TV", OfferType.Rent)]).RankingAdjustment);
    }

    [Fact]
    public void Not_available_here_is_nudged_down_not_hidden()
    {
        var nowhere = WatchOptions.From([], new HashSet<int>(), link: null);

        // A great film somebody cannot stream is still worth knowing exists,
        // which is why this is a nudge and not a filter.
        Assert.True(nowhere.RankingAdjustment < 0);
        Assert.True(nowhere.Known);
    }

    [Fact]
    public void Never_having_looked_claims_nothing()
    {
        // Unknown is not unavailable. TMDb's provider data is not exhaustive, and
        // a film nobody has asked about must not be penalised as if it were.
        Assert.Equal(0, WatchOptions.Unknown.RankingAdjustment);
        Assert.False(WatchOptions.Unknown.Known);
    }

    [Fact]
    public void Availability_cannot_rescue_a_worse_film()
    {
        // The judgement behind the numbers. A 7.0 on Netflix should still lose to
        // an 8.5 you would have to rent, because the question is what to watch,
        // not what is convenient. ADR-0009 puts roughly 0.30 of score between
        // those two films; the largest availability nudge is a third of that.
        var onNetflix = For([Offer(Netflix, "Netflix", OfferType.Subscription)], Netflix);
        var rentOnly = For([Offer(AppleTv, "Apple TV", OfferType.Rent)]);

        var gap = onNetflix.RankingAdjustment - rentOnly.RankingAdjustment;

        Assert.True(gap < 0.15, $"availability moved the ranking by {gap:0.00}, enough to outrank quality");
    }

    [Fact]
    public void Services_are_listed_in_a_stable_order()
    {
        var options = For(
        [
            Offer(Max, "Max", OfferType.Subscription),
            Offer(Netflix, "Netflix", OfferType.Subscription),
        ]);

        // Alphabetical rather than however the source happened to order them, so
        // the same film does not appear to change between page loads.
        Assert.Equal(["Max", "Netflix"], options.StreamingElsewhere);
    }
}
