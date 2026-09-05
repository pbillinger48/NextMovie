using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Genre"/> to the <c>genres</c> table.</summary>
public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.HasKey(g => g.Id);

        // Critical: without this EF assumes an identity column and refuses to
        // let us write TMDb's own ids, which is the entire point of this key.
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(g => g.Name).IsUnique();

        // Seeded as migration data rather than fetched at runtime. TMDb search
        // returns genre ids only, so without this table every search would need
        // a second TMDb call to resolve names. The list is small and effectively
        // static; if TMDb ever adds one, MovieCatalog logs and skips the unknown
        // id rather than failing, and a follow-up migration adds it here.
        builder.HasData(
            new Genre { Id = 28, Name = "Action" },
            new Genre { Id = 12, Name = "Adventure" },
            new Genre { Id = 16, Name = "Animation" },
            new Genre { Id = 35, Name = "Comedy" },
            new Genre { Id = 80, Name = "Crime" },
            new Genre { Id = 99, Name = "Documentary" },
            new Genre { Id = 18, Name = "Drama" },
            new Genre { Id = 10751, Name = "Family" },
            new Genre { Id = 14, Name = "Fantasy" },
            new Genre { Id = 36, Name = "History" },
            new Genre { Id = 27, Name = "Horror" },
            new Genre { Id = 10402, Name = "Music" },
            new Genre { Id = 9648, Name = "Mystery" },
            new Genre { Id = 10749, Name = "Romance" },
            new Genre { Id = 878, Name = "Science Fiction" },
            new Genre { Id = 10770, Name = "TV Movie" },
            new Genre { Id = 53, Name = "Thriller" },
            new Genre { Id = 10752, Name = "War" },
            new Genre { Id = 37, Name = "Western" });
    }
}
