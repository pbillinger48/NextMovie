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
    }
}
