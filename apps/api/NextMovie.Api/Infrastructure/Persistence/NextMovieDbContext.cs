using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Domain.Recommendations;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Infrastructure.Persistence.Configurations;

namespace NextMovie.Api.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the NextMovie database.
/// </summary>
public sealed class NextMovieDbContext(DbContextOptions<NextMovieDbContext> options)
    : DbContext(options)
{
    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();

    public DbSet<Rating> Ratings => Set<Rating>();

    public DbSet<WatchHistoryEntry> WatchHistory => Set<WatchHistoryEntry>();

    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    public DbSet<ImportItem> ImportItems => Set<ImportItem>();

    public DbSet<RecommendationEvent> RecommendationEvents => Set<RecommendationEvent>();

    public DbSet<RecommendationResponse> RecommendationResponses => Set<RecommendationResponse>();

    public DbSet<StreamingProvider> StreamingProviders => Set<StreamingProvider>();

    public DbSet<MovieAvailability> MovieAvailability => Set<MovieAvailability>();

    public DbSet<UserStreamingProvider> UserStreamingProviders => Set<UserStreamingProvider>();

    public DbSet<RegionalProvider> RegionalProviders => Set<RegionalProvider>();

    public DbSet<RegionCatalog> RegionCatalogs => Set<RegionCatalog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurations are applied explicitly rather than via
        // ApplyConfigurationsFromAssembly. Reflection would save two lines and
        // cost the ability to see, from here, exactly what shapes the model.
        modelBuilder.ApplyConfiguration(new MovieConfiguration());
        modelBuilder.ApplyConfiguration(new GenreConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new UserExternalLoginConfiguration());
        modelBuilder.ApplyConfiguration(new RatingConfiguration());
        modelBuilder.ApplyConfiguration(new WatchHistoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new ImportJobConfiguration());
        modelBuilder.ApplyConfiguration(new ImportItemConfiguration());
        modelBuilder.ApplyConfiguration(new RecommendationEventConfiguration());
        modelBuilder.ApplyConfiguration(new RecommendationResponseConfiguration());
        modelBuilder.ApplyConfiguration(new StreamingProviderConfiguration());
        modelBuilder.ApplyConfiguration(new MovieAvailabilityConfiguration());
        modelBuilder.ApplyConfiguration(new AvailabilityOfferConfiguration());
        modelBuilder.ApplyConfiguration(new UserStreamingProviderConfiguration());
        modelBuilder.ApplyConfiguration(new RegionalProviderConfiguration());
        modelBuilder.ApplyConfiguration(new RegionCatalogConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
