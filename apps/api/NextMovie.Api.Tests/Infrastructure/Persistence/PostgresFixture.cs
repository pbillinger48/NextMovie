using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NextMovie.Api.Tests.Infrastructure.Persistence;

/// <summary>
/// A real PostgreSQL instance, started once per test class collection.
/// </summary>
/// <remarks>
/// Deliberately a real database rather than EF's InMemory provider. InMemory does
/// not enforce unique constraints or foreign keys, so it would happily accept the
/// duplicate <c>tmdb_id</c> that production PostgreSQL rejects — it would test a
/// database that does not exist, and pass while the real system broke.
/// <para>
/// The image is pinned to the same tag as docker-compose.yml so tests run against
/// the engine version the application actually uses.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("nextmovie_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Schema comes from the real migrations, not EnsureCreated(). If a
        // migration is broken, these tests should fail — that is a feature.
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public NextMovieDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NextMovieDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new NextMovieDbContext(options);
    }
}

/// <summary>
/// Shares one container across every test class in the collection, rather than
/// paying container startup per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
