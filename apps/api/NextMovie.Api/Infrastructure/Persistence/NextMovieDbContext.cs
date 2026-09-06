using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurations are applied explicitly rather than via
        // ApplyConfigurationsFromAssembly. Reflection would save two lines and
        // cost the ability to see, from here, exactly what shapes the model.
        modelBuilder.ApplyConfiguration(new MovieConfiguration());
        modelBuilder.ApplyConfiguration(new GenreConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
