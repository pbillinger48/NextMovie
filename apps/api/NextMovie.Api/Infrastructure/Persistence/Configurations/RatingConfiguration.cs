using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Library;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Rating"/> to the <c>ratings</c> table.</summary>
public sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.HasKey(rating => rating.Id);

        // numeric(2,1): exact decimal, four bytes of meaning, and no floating
        // point. Precision 2 / scale 1 is exactly wide enough for 0.5 to 5.0.
        builder.Property(rating => rating.Value)
            .HasColumnType("numeric(2,1)")
            .IsRequired();

        builder.Property(rating => rating.Source)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // The application validates this too, but the database is what makes it
        // true: an import, a migration or a future service writing directly must
        // not be able to introduce a 4.3 or a 0.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_ratings_value_half_stars",
            $"value >= {RatingScale.Minimum} AND value <= {RatingScale.Maximum} AND (value * 2) % 1 = 0"));

        // One opinion per person per film. Re-rating updates; it does not
        // accumulate.
        builder.HasIndex(rating => new { rating.UserId, rating.MovieId }).IsUnique();

        builder
            .HasOne(rating => rating.User)
            .WithMany()
            .HasForeignKey(rating => rating.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(rating => rating.Movie)
            .WithMany()
            .HasForeignKey(rating => rating.MovieId)
            // Restrict, not Cascade: a film is shared catalogue data, and
            // deleting one must not silently erase everybody's opinion of it.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
