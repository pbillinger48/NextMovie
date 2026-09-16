using Microsoft.Extensions.Logging.Abstractions;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Tests.Infrastructure.Streaming;

/// <summary>
/// Tests the boundary between TMDb's shape and ours.
/// </summary>
/// <remarks>
/// The distinction that carries the most weight is between "we asked and it is
/// not available" and "we could not ask". Confusing them means an outage gets
/// cached as absence, and everyone is told for a day that nothing is streaming
/// anywhere.
/// </remarks>
public sealed class TmdbAvailabilityProviderTests
{
    private const int Netflix = 8;
    private const int AppleTv = 2;

    private static TmdbProviderDto Provider(int id, string name) =>
        new() { ProviderId = id, ProviderName = name, LogoPath = "/logo.jpg" };

    private static Task<FilmAvailability?> AskAsync(
        TmdbWatchProvidersResponse response,
        string region = "US")
    {
        var provider = new TmdbAvailabilityProvider(
            new StubTmdb { Response = response },
            NullLogger<TmdbAvailabilityProvider>.Instance);

        return provider.GetAvailabilityAsync(550, region, CancellationToken.None);
    }

    [Fact]
    public async Task Reads_every_way_a_film_can_be_watched()
    {
        var availability = await AskAsync(new TmdbWatchProvidersResponse
        {
            Results = new Dictionary<string, TmdbRegionProviders>
            {
                ["US"] = new()
                {
                    Link = "https://www.themoviedb.org/movie/550/watch?locale=US",
                    Flatrate = [Provider(Netflix, "Netflix")],
                    Rent = [Provider(AppleTv, "Apple TV")],
                    Buy = [Provider(AppleTv, "Apple TV")],
                },
            },
        });

        Assert.NotNull(availability);
        Assert.Equal(3, availability.Offers.Count);
        Assert.Contains(availability.Offers, offer =>
            offer.ProviderId == Netflix && offer.Type == OfferType.Subscription);

        // One shop, two ways to watch. Both are kept: renting and buying are
        // different propositions to the person deciding.
        Assert.Contains(availability.Offers, offer => offer.Type == OfferType.Rent);
        Assert.Contains(availability.Offers, offer => offer.Type == OfferType.Buy);
    }

    [Fact]
    public async Task Translates_tmdbs_names_for_things()
    {
        var availability = await AskAsync(new TmdbWatchProvidersResponse
        {
            Results = new Dictionary<string, TmdbRegionProviders>
            {
                // "flatrate" is TMDb's word, and it stops here.
                ["US"] = new() { Flatrate = [Provider(Netflix, "Netflix")], Ads = [Provider(3, "Pluto TV")] },
            },
        });

        Assert.NotNull(availability);
        Assert.Contains(availability.Offers, offer => offer.Type == OfferType.Subscription);
        Assert.Contains(availability.Offers, offer => offer.Type == OfferType.Ads);
    }

    [Fact]
    public async Task Answers_for_the_region_asked_about()
    {
        var response = new TmdbWatchProvidersResponse
        {
            Results = new Dictionary<string, TmdbRegionProviders>
            {
                ["US"] = new() { Flatrate = [Provider(Netflix, "Netflix")] },
                ["GB"] = new() { Flatrate = [Provider(39, "Now TV")] },
            },
        };

        var british = await AskAsync(response, "GB");

        // The whole reason availability is keyed on region: a film on Netflix in
        // one country is routinely not in another.
        Assert.NotNull(british);
        Assert.Equal("Now TV", Assert.Single(british.Offers).ProviderName);
    }

    [Fact]
    public async Task A_region_tmdb_does_not_list_means_not_available_there()
    {
        var availability = await AskAsync(
            new TmdbWatchProvidersResponse
            {
                Results = new Dictionary<string, TmdbRegionProviders>
                {
                    ["US"] = new() { Flatrate = [Provider(Netflix, "Netflix")] },
                },
            },
            region: "JP");

        // A real answer, not a failure: TMDb omits countries where a film is not
        // offered, and that is worth caching so we stop asking.
        Assert.NotNull(availability);
        Assert.Empty(availability.Offers);
    }

    [Fact]
    public async Task An_unreachable_source_is_not_an_answer()
    {
        var provider = new TmdbAvailabilityProvider(
            new StubTmdb { Failure = new TmdbException("TMDb could not be reached.", statusCode: null) },
            NullLogger<TmdbAvailabilityProvider>.Instance);

        // Null, never an empty list. Caching an outage as "not available
        // anywhere" would tell everyone, for a day, that nothing streams.
        Assert.Null(await provider.GetAvailabilityAsync(550, "US", CancellationToken.None));
    }

    [Fact]
    public async Task Providers_with_nothing_to_show_are_dropped()
    {
        var availability = await AskAsync(new TmdbWatchProvidersResponse
        {
            Results = new Dictionary<string, TmdbRegionProviders>
            {
                ["US"] = new()
                {
                    Flatrate =
                    [
                        Provider(Netflix, "Netflix"),
                        new TmdbProviderDto { ProviderId = 0, ProviderName = "Broken" },
                        new TmdbProviderDto { ProviderId = 99, ProviderName = "  " },
                    ],
                },
            },
        });

        Assert.NotNull(availability);
        Assert.Equal("Netflix", Assert.Single(availability.Offers).ProviderName);
    }

    [Theory]
    [InlineData(OfferType.Subscription, true)]
    [InlineData(OfferType.Free, true)]
    [InlineData(OfferType.Ads, true)]
    [InlineData(OfferType.Rent, false)]
    [InlineData(OfferType.Buy, false)]
    public void Only_some_offers_mean_you_can_press_play(OfferType type, bool streamsNow)
    {
        // Describing a purchase as streaming would be a lie the product's own
        // sentence does not support.
        Assert.Equal(streamsNow, OfferTypes.StreamsNow(type));
    }

    private sealed class StubTmdb : ITmdbClient
    {
        public TmdbWatchProvidersResponse Response { get; init; } = new();

        public TmdbException? Failure { get; init; }

        public Task<TmdbWatchProvidersResponse> GetWatchProvidersAsync(int tmdbId, CancellationToken cancellationToken) =>
            Failure is not null
                ? Task.FromException<TmdbWatchProvidersResponse>(Failure)
                : Task.FromResult(Response);

        public Task<TmdbSearchResponse> SearchMoviesAsync(string title, int page, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbSearchResponse> GetRelatedMoviesAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbSearchResponse> DiscoverBestInGenreAsync(int genreId, int minimumVotes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbProviderListResponse> GetProvidersAsync(string region, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
