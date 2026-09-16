using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Infrastructure.Streaming;

/// <summary>
/// Tests the catalogue behind the settings screen.
/// </summary>
/// <remarks>
/// The failure that matters here is the mirror of the availability cache's: this
/// data barely changes, so the risk is not staleness but an outage emptying the
/// list somebody chooses from — and, worse, taking their saved choices with it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ServiceCatalogTests(PostgresFixture postgres) : IAsyncLifetime
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
            + "delete from user_streaming_providers; delete from regional_providers; "
            + "delete from region_catalogs; delete from streaming_providers;",
            Ct);
    }

    [Fact]
    public async Task Fetches_and_stores_a_country_catalogue()
    {
        var source = new StubDirectory(Service(Netflix, "Netflix", priority: 2), Service(Max, "Max", priority: 1));

        var services = await AskAsync(source);

        // Ordered by the source's own priority, not alphabetically. Alphabetical
        // would put obscure regional services above the ones almost everyone has.
        Assert.Equal(["Max", "Netflix"], services.Select(service => service.Name));
    }

    [Fact]
    public async Task Does_not_ask_again_within_the_month()
    {
        var source = new StubDirectory(Service(Netflix, "Netflix"));

        await AskAsync(source);
        await AskAsync(source, at: Now.AddDays(29));

        // Whether Netflix exists in a country changes approximately never. Asking
        // on every settings page view would be a network call for nothing.
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Asks_again_once_the_catalogue_is_a_month_old()
    {
        var source = new StubDirectory(Service(Netflix, "Netflix"));

        await AskAsync(source);
        await AskAsync(source, at: Now.AddDays(31));

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task A_service_that_has_left_the_country_stops_being_offered()
    {
        var source = new StubDirectory(Service(Netflix, "Netflix"), Service(Max, "Max"));
        await AskAsync(source);

        source.Services = [Service(Netflix, "Netflix")];
        var services = await AskAsync(source, at: Now.AddDays(31));

        // A service somebody can still tick after it has closed makes every
        // recommendation built on it wrong.
        Assert.Equal(["Netflix"], services.Select(service => service.Name));
    }

    [Fact]
    public async Task An_outage_keeps_the_previous_catalogue()
    {
        var source = new StubDirectory(Service(Netflix, "Netflix"));
        await AskAsync(source);

        source.Unreachable = true;
        var services = await AskAsync(source, at: Now.AddDays(31));

        // An empty settings page would look like "you subscribe to nothing",
        // which is a claim about the user rather than about our upstream.
        Assert.Single(services);
    }

    [Fact]
    public async Task An_outage_does_not_count_as_a_fresh_answer()
    {
        var source = new StubDirectory(Service(Netflix, "Netflix")) { Unreachable = true };

        await AskAsync(source);
        await AskAsync(source, at: Now.AddMinutes(1));

        // Nothing was stamped, so the next request tries again rather than
        // treating a failure as an answer good for the next month.
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task A_country_with_no_services_is_remembered_rather_than_re_asked()
    {
        var source = new StubDirectory();

        Assert.Empty(await AskAsync(source));
        await AskAsync(source, at: Now.AddDays(1));

        // "Asked, and the answer was nothing" is a real answer. Storing the stamp
        // separately from the rows is what makes it distinguishable from never
        // having asked.
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Countries_are_answered_separately()
    {
        await AskAsync(new StubDirectory(Service(Netflix, "Netflix")));

        var british = await AskAsync(new StubDirectory(Service(Max, "Max")), region: "GB");

        Assert.Equal(["Max"], british.Select(service => service.Name));

        await using var db = postgres.CreateContext();

        // One provider row per service, referenced from as many countries as
        // offer it — which is why the region cannot live on the provider.
        Assert.Equal(2, await db.StreamingProviders.CountAsync(Ct));
        Assert.Equal(2, await db.RegionalProviders.CountAsync(Ct));
    }

    [Fact]
    public async Task A_renamed_service_is_updated_rather_than_duplicated()
    {
        var source = new StubDirectory(Service(Max, "HBO Max"));
        await AskAsync(source);

        source.Services = [Service(Max, "Max")];
        await AskAsync(source, at: Now.AddDays(31));

        await using var db = postgres.CreateContext();
        Assert.Equal("Max", (await db.StreamingProviders.SingleAsync(Ct)).Name);
    }

    // --- helpers ---

    private static RegionalService Service(int id, string name, int priority = 1) =>
        new(id, name, "/logo.jpg", priority);

    private async Task<IReadOnlyList<StreamingProvider>> AskAsync(
        StubDirectory source,
        DateTimeOffset? at = null,
        string region = "US")
    {
        await using var db = postgres.CreateContext();

        var catalog = new ServiceCatalog(
            db,
            source,
            new FixedTimeProvider(at ?? Now),
            NullLogger<ServiceCatalog>.Instance);

        return await catalog.ForRegionAsync(region, Ct);
    }

    private sealed class StubDirectory(params RegionalService[] services) : IServiceDirectory
    {
        public IReadOnlyList<RegionalService> Services { get; set; } = services;

        public bool Unreachable { get; set; }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<RegionalService>?> GetServicesAsync(
            string region,
            CancellationToken cancellationToken)
        {
            Calls++;

            // Null, not empty: an outage must not read as "this country has no
            // streaming services".
            return Task.FromResult<IReadOnlyList<RegionalService>?>(Unreachable ? null : Services);
        }
    }
}
