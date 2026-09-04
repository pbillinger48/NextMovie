using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Movie"/> to the <c>movies</c> table.</summary>
public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.HasKey(m => m.Id);

        // The single most important constraint in this migration. Letterboxd
        // imports and TMDb syncs both resolve films by TMDb id, and without a
        // unique index a retried or concurrent import silently duplicates the
        // catalogue. docs/database.md omits this.
        builder.HasIndex(m => m.TmdbId).IsUnique();

        builder.Property(m => m.Title).HasMaxLength(500).IsRequired();
        builder.Property(m => m.OriginalTitle).HasMaxLength(500);

        // Overview is left unbounded (Postgres `text`): synopses have no natural
        // ceiling, and in Postgres a length limit buys no performance.
        builder.Property(m => m.Overview);

        builder.Property(m => m.PosterPath).HasMaxLength(255);
        builder.Property(m => m.BackdropPath).HasMaxLength(255);
        builder.Property(m => m.Language).HasMaxLength(10);
        builder.Property(m => m.Status).HasMaxLength(50);

        // Title search arrives in Step 7. This plain index serves prefix and
        // equality lookups; full-text or trigram search is a later, deliberate
        // change once the query shape is known.
        builder.HasIndex(m => m.Title);

        builder
            .HasMany(m => m.Genres)
            .WithMany(g => g.Movies)
            .UsingEntity(join =>
            {
                // Named explicitly: EF's default join-table name here would be
                // `genre_movie` (alphabetical). docs/database.md specifies MovieGenre.
                join.ToTable("movie_genre");

                // EF derives join keys from the navigation names, yielding
                // `movies_id` / `genres_id`. Singular reads correctly in SQL and
                // matches docs/database.md. Renaming later would mean a migration
                // against live data, so it is worth fixing before first apply.
                join.Property<Guid>("MoviesId").HasColumnName("movie_id");
                join.Property<int>("GenresId").HasColumnName("genre_id");
            });
    }
}
