using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Infrastructure.Streaming;

/// <summary>
/// Tests the cache around the most perishable data in the product.
/// </summary>
/// <remarks>
/// Two failures matter more than the rest: telling somebody a film is on a
/// service it has left, and treating an outage as proof that nothing is
/// available. Both are silent, and both send people to the wrong place.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class AvailabilityCatalogTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Netflix = 8;
    private const int Max = 1899;

    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

    private async Task ClearAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "delete from availability_offers; delete from movie_availability; "
            + "delete from user_streaming_providers; delete from streaming_providers; "
            + "delete from movie_genre; delete from movies;",
            Ct);
    }

    [Fact]
    public async Task Fetches_and_stores_what_it_learns()
    {
        var film = await SeedFilmAsync();
        var source = new StubProvider(Offer(Netflix, "Netflix", OfferType.Subscription));

        var availability = await AskAsync(source, film);

        var stored = Assert.Single(availability).Value;
        Assert.Equal("US", stored.Region);
        Assert.Equal(Now, stored.RefreshedAt);

        var offer = Assert.Single(stored.Offers);
        Assert.Equal(Netflix, offer.StreamingProviderId);
        Assert.Equal(OfferType.Subscription, offer.Type);

        await using var db = postgres.CreateContext();

        // Services arrive with the availability that mentions them; there is no
        // seeded list to go stale.
        Assert.Equal("Netflix", (await db.StreamingProviders.SingleAsync(Ct)).Name);
    }

    [Fact]
    public async Task Does_not_ask_again_within_the_day()
    {
        var film = await SeedFilmAsync();
        var source = new StubProvider(Offer(Netflix, "Netflix", OfferType.Subscription));

        await AskAsync(source, film);
        await AskAsync(source, film, at: Now.AddHours(6));

        // A page of twelve films should not cost twelve upstream calls every
        // time somebody reloads it.
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Asks_again_once_the_answer_is_a_day_old()
    {
        var film = await SeedFilmAsync();
        var source = new StubProvider(Offer(Netflix, "Netflix", OfferType.Subscription));

        await AskAsync(source, film);
        await AskAsync(source, film, at: Now.AddHours(25));

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task A_film_that_has_left_a_service_stops_being_listed_on_it()
    {
        var film = await SeedFilmAsync();

        var source = new StubProvider(Offer(Netflix, "Netflix", OfferType.Subscription));
        await AskAsync(source, film);

        source.Offers = [Offer(Max, "Max", OfferType.Subscription)];
        var refreshed = await AskAsync(source, film, at: Now.AddHours(25));

        // The single most important change this data carries. Merging instead of
        // replacing would keep sending people to a service the film has left.
        var offer = Assert.Single(refreshed[film.Id].Offers);
        Assert.Equal(Max, offer.StreamingProviderId);
    }

    [Fact]
    public async Task Not_available_here_is_remembered_rather_than_re_asked()
    {
        var film = await SeedFilmAsync();
        var source = new StubProvider();

        await AskAsync(source, film);
        await AskAsync(source, film, at: Now.AddHours(6));

        // A film unavailable in a region is a real answer. Without storing it,
        // every unavailable film would be looked up again on every request
        // forever.
        Assert.Equal(1, source.Calls);

        await using var db = postgres.CreateContext();
        var stored = await db.MovieAvailability.Include(a => a.Offers).SingleAsync(Ct);
        Assert.Empty(stored.Offers);
    }

    [Fact]
    public async Task An_outage_keeps_the_previous_answer_rather_than_erasing_it()
    {
        var film = await SeedFilmAsync();
        var source = new StubProvider(Offer(Netflix, "Netflix", OfferType.Subscription));
        await AskAsync(source, film);

        source.Unreachable = true;
        var afterOutage = await AskAsync(source, film, at: Now.AddHours(25));

        // Stale availability is more useful than none, and far more useful than
        // telling somebody nothing streams because TMDb was down.
        Assert.Single(afterOutage[film.Id].Offers);
    }

    [Fact]
    public async Task Regions_are_answered_separately()
    {
        var film = await SeedFilmAsync();

        var american = new StubProvider(Offer(Netflix, "Netflix", OfferType.Subscription));
        await AskAsync(american, film);

        var british = new StubProvider(Offer(Max, "Max", OfferType.Subscription));
        var abroad = await AskAsync(british, film, region: "GB");

        Assert.Equal(Max, Assert.Single(abroad[film.Id].Offers).StreamingProviderId);

        await using var db = postgres.CreateContext();

        // Two answers about one film, which is the grain the data actually has.
        Assert.Equal(2, await db.MovieAvailability.CountAsync(Ct));
    }

    [Fact]
    public async Task Asking_about_nothing_costs_nothing()
    {
        var source = new StubProvider();

        Assert.Empty(await Catalog(source, Now).ForFilmsAsync([], "US", Ct));
        Assert.Equal(0, source.Calls);
    }

    // --- helpers ---

    private static FilmOffer Offer(int id, string name, OfferType type) =>
        new(id, name, "/logo.jpg", type);

    private async Task<Movie> SeedFilmAsync()
    {
        await using var db = postgres.CreateContext();

        var film = new Movie { TmdbId = 550, Title = "Fight Club" };
        db.Movies.Add(film);
        await db.SaveChangesAsync(Ct);

        return film;
    }

    private async Task<Dictionary<Guid, MovieAvailability>> AskAsync(
        StubProvider source,
        Movie film,
        DateTimeOffset? at = null,
        string region = "US")
    {
        await using var db = postgres.CreateContext();

        var catalog = new AvailabilityCatalog(
            db,
            source,
            new FixedTimeProvider(at ?? Now),
            NullLogger<AvailabilityCatalog>.Instance);

        return await catalog.ForFilmsAsync([film], region, Ct);
    }

    private AvailabilityCatalog Catalog(StubProvider source, DateTimeOffset at) =>
        new(postgres.CreateContext(), source, new FixedTimeProvider(at), NullLogger<AvailabilityCatalog>.Instance);

    private sealed class StubProvider(params FilmOffer[] offers) : IAvailabilityProvider
    {
        public IReadOnlyList<FilmOffer> Offers { get; set; } = offers;

        public bool Unreachable { get; set; }

        public int Calls { get; private set; }

        public Task<FilmAvailability?> GetAvailabilityAsync(
            int tmdbId,
            string region,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult<FilmAvailability?>(
                Unreachable ? null : new FilmAvailability(Offers, "https://example.com/watch"));
        }
    }
}
